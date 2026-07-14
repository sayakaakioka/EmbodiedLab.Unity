from __future__ import annotations

import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET


MODULE_PATH = Path(__file__).resolve().parents[1] / "run_unity_tests.py"
MODULE_SPEC = importlib.util.spec_from_file_location("run_unity_tests", MODULE_PATH)
if MODULE_SPEC is None or MODULE_SPEC.loader is None:
    raise RuntimeError(f"Could not load {MODULE_PATH}")
run_unity_tests = importlib.util.module_from_spec(MODULE_SPEC)
MODULE_SPEC.loader.exec_module(run_unity_tests)


class RunUnityTestsTests(unittest.TestCase):
    @staticmethod
    def write_results(results_path: Path, test_results: dict[str, str]) -> None:
        passed = sum(result == "Passed" for result in test_results.values())
        failed = sum(result == "Failed" for result in test_results.values())
        skipped = sum(result == "Skipped" for result in test_results.values())
        inconclusive = sum(result == "Inconclusive" for result in test_results.values())
        root = ET.Element(
            "test-run",
            {
                "total": str(len(test_results)),
                "passed": str(passed),
                "failed": str(failed),
                "skipped": str(skipped),
                "inconclusive": str(inconclusive),
                "result": "Passed" if failed == 0 else "Failed",
            },
        )
        suite = ET.SubElement(root, "test-suite")
        for name, result in sorted(test_results.items()):
            ET.SubElement(
                suite,
                "test-case",
                {
                    "fullname": name,
                    "result": result,
                },
            )
        ET.ElementTree(root).write(results_path, encoding="unicode")

    @staticmethod
    def passing_results() -> dict[str, str]:
        return {name: "Passed" for name in run_unity_tests.REQUIRED_TEST_NAMES}

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
                self.write_results(results_path, self.passing_results())
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
            self.assertIn(
                f'total="{len(run_unity_tests.REQUIRED_TEST_NAMES)}"',
                results_path.read_text(encoding="utf-8"),
            )

    def test_validate_results_rejects_a_missing_required_test(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            results_path = Path(temporary_directory) / "results.xml"
            test_results = self.passing_results()
            test_results.pop(sorted(test_results)[0])
            self.write_results(results_path, test_results)

            with self.assertRaisesRegex(RuntimeError, "did not execute required tests"):
                run_unity_tests.validate_results(results_path)

    def test_validate_results_rejects_a_skipped_required_test(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            results_path = Path(temporary_directory) / "results.xml"
            test_results = self.passing_results()
            test_results[sorted(test_results)[0]] = "Skipped"
            self.write_results(results_path, test_results)

            with self.assertRaisesRegex(RuntimeError, "did not pass required tests"):
                run_unity_tests.validate_results(results_path)


if __name__ == "__main__":
    unittest.main()
