#!/usr/bin/env python3
from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
PROJECT_PATH = REPOSITORY_ROOT / "TestProjects~" / "Unity6000.3"
STAGING_ROOT = Path("Assets") / "EmbodiedLabQuickstartStandaloneValidation"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build and run the Quickstart Windows x64 ONNX smoke test."
    )
    parser.add_argument("--unity-editor", type=Path, required=True)
    parser.add_argument("--policy", type=Path, required=True)
    parser.add_argument("--output-directory", type=Path, required=True)
    return parser.parse_args()


def mounted_windows_path(path: Path) -> str:
    absolute = path.expanduser().resolve()
    if os.name == "nt":
        return str(absolute)

    parts = absolute.parts
    if (
        len(parts) < 4
        or parts[0] != os.path.sep
        or parts[1] != "mnt"
        or len(parts[2]) != 1
        or not parts[2].isalpha()
    ):
        raise ValueError(f"Windows cannot access path: {absolute}")

    remainder = "\\".join(parts[3:])
    return f"{parts[2].upper()}:\\{remainder}"


def remove_path(path: Path) -> None:
    if path.is_symlink() or path.is_file():
        path.unlink()
    elif path.is_dir():
        shutil.rmtree(path)


def stage_sources() -> None:
    staging = PROJECT_PATH / STAGING_ROOT
    remove_path(staging)
    remove_path(staging.with_name(f"{staging.name}.meta"))
    staging.mkdir(parents=True)
    shutil.copytree(REPOSITORY_ROOT / "Samples~" / "Quickstart", staging / "Quickstart")
    shutil.copytree(
        REPOSITORY_ROOT / "Tests~" / "QuickstartStandaloneSmoke",
        staging / "Smoke",
    )


def cleanup_sources() -> None:
    staging = PROJECT_PATH / STAGING_ROOT
    remove_path(staging)
    remove_path(staging.with_name(f"{staging.name}.meta"))


def print_log_tail(path: Path, count: int = 100) -> None:
    if path.is_file():
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
        print("\n".join(lines[-count:]), file=sys.stderr)


def main() -> int:
    args = parse_args()
    unity_editor = args.unity_editor.expanduser().resolve()
    policy = args.policy.expanduser().resolve()
    output = args.output_directory.expanduser().resolve()
    scenario = REPOSITORY_ROOT / "Samples~" / "Quickstart" / "NavigationScenario.json"
    if not unity_editor.is_file() or not policy.is_file() or not scenario.is_file():
        print("Unity editor, policy, or scenario input is missing.", file=sys.stderr)
        return 2

    remove_path(output)
    output.mkdir(parents=True)
    executable = output / "EmbodiedLabQuickstartSmoke.exe"
    editor_log = output / "editor-build.log"
    player_log = output / "player-smoke.log"
    result = output / "smoke-result.txt"

    try:
        stage_sources()
        build_command = [
            str(unity_editor),
            "-batchmode",
            "-quit",
            "-forgetProjectPath",
            "-projectPath",
            mounted_windows_path(PROJECT_PATH),
            "-executeMethod",
            (
                "EmbodiedLab.Unity.Samples.Quickstart.StandaloneSmoke.Editor."
                "QuickstartStandaloneSmokeBuild.Build"
            ),
            "--embodiedlab-build-path",
            mounted_windows_path(executable),
            "-logFile",
            mounted_windows_path(editor_log),
        ]
        built = subprocess.run(build_command, check=False)
        if built.returncode != 0 or not executable.is_file():
            print(
                f"Unity standalone build failed with code {built.returncode}.",
                file=sys.stderr,
            )
            print_log_tail(editor_log)
            return 1

        player_command = [
            str(executable),
            "-batchmode",
            "-logFile",
            mounted_windows_path(player_log),
            "--embodiedlab-policy",
            mounted_windows_path(policy),
            "--embodiedlab-scenario",
            mounted_windows_path(scenario),
            "--embodiedlab-smoke-result",
            mounted_windows_path(result),
        ]
        try:
            played = subprocess.run(player_command, check=False, timeout=180)
        except subprocess.TimeoutExpired:
            print("Standalone smoke test timed out.", file=sys.stderr)
            print_log_tail(player_log)
            return 1

        if played.returncode != 0 or not result.is_file():
            print(
                f"Standalone smoke failed with code {played.returncode}.",
                file=sys.stderr,
            )
            print_log_tail(player_log)
            return 1

        result_text = result.read_text(encoding="utf-8-sig", errors="replace")
        if not result_text.startswith("PASS\n"):
            print(result_text, file=sys.stderr)
            print_log_tail(player_log)
            return 1

        print(result_text.strip())
        print(f"Windows x64 standalone smoke passed: {executable}")
        return 0
    finally:
        cleanup_sources()


if __name__ == "__main__":
    raise SystemExit(main())
