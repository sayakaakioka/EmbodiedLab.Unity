#!/usr/bin/env python3
from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET


TEST_ASSEMBLY = "EmbodiedLab.Unity.Editor.Tests"
IMPORTED_SAMPLE_TEST_ASSEMBLY = "EmbodiedLab.Unity.Samples.Quickstart.Imported.Tests"
REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SAMPLE_SOURCE_RELATIVE_PATH = Path("Samples~") / "Quickstart"
IMPORTED_TESTS_SOURCE_RELATIVE_PATH = Path("Tests~") / "QuickstartImported"
STAGING_ROOT_RELATIVE_PATH = Path("Assets") / "EmbodiedLabQuickstartValidation"
STAGED_SAMPLE_RELATIVE_PATH = STAGING_ROOT_RELATIVE_PATH / "Quickstart"
STAGED_TESTS_RELATIVE_PATH = STAGING_ROOT_RELATIVE_PATH / "ImportedTests"
REQUIRED_TEST_NAMES = frozenset(
    {
        "EmbodiedLab.Unity.Tests.ContractRoundTripTests.ReplayBundleManifestRoundTrips",
        "EmbodiedLab.Unity.Tests.ContractRoundTripTests.ReplayLogContainsTwoRoundTrippableSteps",
        "EmbodiedLab.Unity.Tests.ContractRoundTripTests.ResultDocumentAndResultBundleRoundTrip",
        "EmbodiedLab.Unity.Tests.ContractRoundTripTests.ScenarioBundleRoundTripPreservesConcreteTypes",
        "EmbodiedLab.Unity.Tests.ContractRoundTripTests.ScenarioBundleJsonRoundTripsCanonicalScenario",
        "EmbodiedLab.Unity.Tests.ContractRoundTripTests.ReplayReadersHandlePlainAndCompressedLogs",
        "EmbodiedLab.Unity.Tests.ContractRoundTripTests.ReplayReadersEnforceResourceLimits",
        "EmbodiedLab.Unity.Tests.EmbodiedLabJobTests.RestorePreservesCancellationCapability",
        (
            "EmbodiedLab.Unity.Samples.Quickstart.Imported.Tests."
            "QuickstartWorldBuilderTests.CanonicalScenarioBuildsExpectedWorld"
        ),
        (
            "EmbodiedLab.Unity.Samples.Quickstart.Imported.Tests."
            "QuickstartWorldBuilderTests.DisposeRemovesGeneratedWorld"
        ),
    }
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run EmbodiedLab.Unity package tests in Unity 6000.3."
    )
    parser.add_argument(
        "--unity-editor",
        type=Path,
        default=os.environ.get("UNITY_EDITOR"),
        help="Path to the Unity executable. Defaults to UNITY_EDITOR.",
    )
    parser.add_argument(
        "--output-directory",
        type=Path,
        help="Directory for the NUnit XML and Unity editor log.",
    )
    return parser.parse_args()


def print_log_tail(log_path: Path, line_count: int = 80) -> None:
    if not log_path.is_file():
        return

    lines = log_path.read_text(encoding="utf-8", errors="replace").splitlines()
    print("\n".join(lines[-line_count:]), file=sys.stderr)


def validate_results(results_path: Path) -> tuple[int, int]:
    try:
        root = ET.parse(results_path).getroot()
    except (ET.ParseError, OSError) as error:
        raise RuntimeError(f"Could not read Unity test results: {error}") from error

    total = int(root.attrib.get("total") or root.attrib.get("testcasecount") or 0)
    passed = int(root.attrib.get("passed") or 0)
    failed = int(root.attrib.get("failed") or 0)
    skipped = int(root.attrib.get("skipped") or 0)
    inconclusive = int(root.attrib.get("inconclusive") or 0)
    result = root.attrib.get("result")
    if total == 0:
        raise RuntimeError("Unity reported zero executed tests.")

    test_results = {
        test_case.attrib.get("fullname"): test_case.attrib.get("result")
        for test_case in root.iter("test-case")
    }
    missing_tests = sorted(REQUIRED_TEST_NAMES - test_results.keys())
    if missing_tests:
        raise RuntimeError(
            f"Unity did not execute required tests: {', '.join(missing_tests)}."
        )

    non_passing_tests = sorted(
        name for name in REQUIRED_TEST_NAMES if test_results[name] != "Passed"
    )
    if non_passing_tests:
        raise RuntimeError(
            f"Unity did not pass required tests: {', '.join(non_passing_tests)}."
        )

    if (
        failed != 0
        or skipped != 0
        or inconclusive != 0
        or passed != total
        or result not in {"Passed", "Success"}
    ):
        raise RuntimeError(
            "Unity tests did not pass completely: "
            f"result={result!r}, total={total}, passed={passed}, failed={failed}, "
            f"skipped={skipped}, inconclusive={inconclusive}."
        )

    return total, failed


def remove_path(path: Path) -> None:
    if path.is_symlink() or path.is_file():
        path.unlink()
    elif path.is_dir():
        shutil.rmtree(path)


def cleanup_staged_sample(project_path: Path) -> None:
    staging_root = project_path / STAGING_ROOT_RELATIVE_PATH
    remove_path(staging_root)
    remove_path(staging_root.with_name(f"{staging_root.name}.meta"))


def stage_quickstart_sample(repository_root: Path, project_path: Path) -> Path:
    sample_source = repository_root / SAMPLE_SOURCE_RELATIVE_PATH
    imported_tests_source = repository_root / IMPORTED_TESTS_SOURCE_RELATIVE_PATH
    if not sample_source.is_dir():
        raise FileNotFoundError(f"Quickstart sample not found: {sample_source}")

    if not imported_tests_source.is_dir():
        raise FileNotFoundError(
            f"Imported Quickstart tests not found: {imported_tests_source}"
        )

    cleanup_staged_sample(project_path)
    staged_sample = project_path / STAGED_SAMPLE_RELATIVE_PATH
    staged_tests = project_path / STAGED_TESTS_RELATIVE_PATH
    staged_sample.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(sample_source, staged_sample)
    shutil.copytree(imported_tests_source, staged_tests)
    return staged_sample


def mounted_windows_path(path: Path) -> str | None:
    if os.name == "nt":
        return None

    absolute_path = path.expanduser().resolve()
    parts = absolute_path.parts
    if (
        len(parts) < 4
        or parts[0] != os.path.sep
        or parts[1] != "mnt"
        or len(parts[2]) != 1
        or not parts[2].isalpha()
    ):
        return None

    drive = parts[2].upper()
    remainder = "\\".join(parts[3:])
    return f"{drive}:\\{remainder}"


def build_unity_command(
    unity_editor: Path,
    project_path: Path,
    results_path: Path,
    log_path: Path,
) -> list[str]:
    windows_editor = (
        mounted_windows_path(unity_editor)
        if unity_editor.suffix.lower() == ".exe"
        else None
    )

    def editor_argument(path: Path) -> str:
        if windows_editor is None:
            return str(path)

        converted = mounted_windows_path(path)
        if converted is None:
            raise ValueError(
                f"Windows Unity cannot access the non-Windows path: {path}"
            )

        return converted

    return [
        str(unity_editor),
        "-batchmode",
        "-nographics",
        "-forgetProjectPath",
        "-runTests",
        "-testPlatform",
        "EditMode",
        "-assemblyNames",
        f"{TEST_ASSEMBLY};{IMPORTED_SAMPLE_TEST_ASSEMBLY}",
        "-projectPath",
        editor_argument(project_path),
        "-testResults",
        editor_argument(results_path),
        "-logFile",
        editor_argument(log_path),
    ]


def main() -> int:
    args = parse_args()
    if args.unity_editor is None:
        print(
            "Specify --unity-editor or set the UNITY_EDITOR environment variable.",
            file=sys.stderr,
        )
        return 2

    unity_editor = args.unity_editor.expanduser().resolve()
    if not unity_editor.is_file():
        print(f"Unity executable not found: {unity_editor}", file=sys.stderr)
        return 2

    project_path = REPOSITORY_ROOT / "TestProjects~" / "Unity6000.3"
    output_directory = (
        args.output_directory.expanduser().resolve()
        if args.output_directory
        else project_path / "TestResults"
    )
    output_directory.mkdir(parents=True, exist_ok=True)
    results_path = output_directory / "results.xml"
    log_path = output_directory / "editor.log"

    command = build_unity_command(
        unity_editor,
        project_path,
        results_path,
        log_path,
    )
    try:
        results_path.unlink(missing_ok=True)
    except OSError as error:
        print(
            f"Could not remove existing Unity test results: {error}",
            file=sys.stderr,
        )
        return 1

    try:
        stage_quickstart_sample(REPOSITORY_ROOT, project_path)
        completed = subprocess.run(command, check=False)
    except OSError as error:
        print(
            f"Could not stage or run the Quickstart validation: {error}",
            file=sys.stderr,
        )
        return 1
    finally:
        try:
            cleanup_staged_sample(project_path)
        except OSError as error:
            print(
                f"Could not clean the staged Quickstart sample: {error}",
                file=sys.stderr,
            )
            return 1

    if completed.returncode != 0:
        print(
            f"Unity exited with code {completed.returncode}. Log tail:",
            file=sys.stderr,
        )
        print_log_tail(log_path)
        return completed.returncode

    if not results_path.is_file():
        print("Unity did not create the NUnit result file. Log tail:", file=sys.stderr)
        print_log_tail(log_path)
        return 1

    try:
        total, failed = validate_results(results_path)
    except RuntimeError as error:
        print(str(error), file=sys.stderr)
        print_log_tail(log_path)
        return 1

    print(
        f"Unity package tests passed: total={total}, failed={failed}, "
        f"results={results_path}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
