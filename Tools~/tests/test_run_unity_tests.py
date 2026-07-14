from __future__ import annotations

import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


MODULE_PATH = Path(__file__).resolve().parents[1] / "run_unity_tests.py"
MODULE_SPEC = importlib.util.spec_from_file_location("run_unity_tests", MODULE_PATH)
if MODULE_SPEC is None or MODULE_SPEC.loader is None:
    raise RuntimeError(f"Could not load {MODULE_PATH}")
run_unity_tests = importlib.util.module_from_spec(MODULE_SPEC)
MODULE_SPEC.loader.exec_module(run_unity_tests)


class RunUnityTestsTests(unittest.TestCase):
    def test_main_removes_existing_results_before_starting_unity(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_path = Path(temporary_directory)
            unity_editor = temporary_path / "Unity.exe"
            unity_editor.touch()
            output_directory = temporary_path / "output"
            output_directory.mkdir()
            results_path = output_directory / "results.xml"
            results_path.write_text(
                '<test-run total="99" failed="0" result="Passed" />',
                encoding="utf-8",
            )

            def run_unity(
                command: list[str], *, check: bool
            ) -> subprocess.CompletedProcess:
                self.assertFalse(results_path.exists())
                results_path.write_text(
                    '<test-run total="4" failed="0" result="Passed" />',
                    encoding="utf-8",
                )
                return subprocess.CompletedProcess(command, 0)

            arguments = [
                str(MODULE_PATH),
                "--unity-editor",
                str(unity_editor),
                "--output-directory",
                str(output_directory),
            ]
            with (
                mock.patch.object(sys, "argv", arguments),
                mock.patch.object(
                    run_unity_tests.subprocess,
                    "run",
                    side_effect=run_unity,
                ) as run_mock,
            ):
                exit_code = run_unity_tests.main()

            self.assertEqual(0, exit_code)
            run_mock.assert_called_once()
            self.assertIn('total="4"', results_path.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
