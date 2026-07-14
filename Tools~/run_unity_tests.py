#!/usr/bin/env python3
from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET


TEST_ASSEMBLY = "EmbodiedLab.Unity.Editor.Tests"
REQUIRED_TEST_NAMES = frozenset(
    {
        "EmbodiedLab.Unity.Tests.ContractRoundTripTests.ReplayBundleManifestRoundTrips",
        "EmbodiedLab.Unity.Tests.ContractRoundTripTests.ReplayLogContainsTwoRoundTrippableSteps",
        "EmbodiedLab.Unity.Tests.ContractRoundTripTests.ResultDocumentAndResultBundleRoundTrip",
        "EmbodiedLab.Unity.Tests.ContractRoundTripTests.ScenarioBundleRoundTripPreservesConcreteTypes",
    }
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run EmbodiedLab.Unity contract tests in Unity 6000.3."
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

    repository_root = Path(__file__).resolve().parents[1]
    project_path = repository_root / "TestProjects~" / "Unity6000.3"
    output_directory = (
        args.output_directory.expanduser().resolve()
        if args.output_directory
        else project_path / "TestResults"
    )
    output_directory.mkdir(parents=True, exist_ok=True)
    results_path = output_directory / "results.xml"
    log_path = output_directory / "editor.log"

    command = [
        str(unity_editor),
        "-batchmode",
        "-nographics",
        "-forgetProjectPath",
        "-runTests",
        "-testPlatform",
        "EditMode",
        "-assemblyNames",
        TEST_ASSEMBLY,
        "-projectPath",
        str(project_path),
        "-testResults",
        str(results_path),
        "-logFile",
        str(log_path),
    ]
    try:
        results_path.unlink(missing_ok=True)
    except OSError as error:
        print(
            f"Could not remove existing Unity test results: {error}",
            file=sys.stderr,
        )
        return 1

    completed = subprocess.run(command, check=False)
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
        f"Unity contract tests passed: total={total}, failed={failed}, "
        f"results={results_path}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
