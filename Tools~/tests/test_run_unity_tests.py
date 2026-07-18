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

    @staticmethod
    def create_repository_layout(
        repository_root: Path,
        *,
        invalid_sample: bool = False,
    ) -> Path:
        sample_source = repository_root / run_unity_tests.SAMPLE_SOURCE_RELATIVE_PATH
        sample_source.mkdir(parents=True)
        sample_filename = "Broken.cs" if invalid_sample else "QuickstartController.cs"
        sample_content = "this is not valid C#" if invalid_sample else "class Valid {}"
        (sample_source / sample_filename).write_text(sample_content, encoding="utf-8")

        imported_tests = (
            repository_root / run_unity_tests.IMPORTED_TESTS_SOURCE_RELATIVE_PATH
        )
        imported_tests.mkdir(parents=True)
        (imported_tests / "QuickstartWorldBuilderTests.cs").write_text(
            "class ImportedTests {}",
            encoding="utf-8",
        )

        project_path = repository_root / "TestProjects~" / "Unity6000.3"
        (project_path / "Assets").mkdir(parents=True)
        return project_path

    def test_main_stages_fresh_sample_and_removes_existing_results(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_path = Path(temporary_directory)
            repository_root = temporary_path / "repository"
            project_path = self.create_repository_layout(repository_root)
            unity_editor = temporary_path / "Unity.exe"
            unity_editor.touch()
            output_directory = temporary_path / "output"
            output_directory.mkdir()
            results_path = output_directory / "results.xml"
            results_path.write_text(
                '<test-run total="99" failed="0" result="Passed" />',
                encoding="utf-8",
            )
            stale_stage = project_path / run_unity_tests.STAGED_SAMPLE_RELATIVE_PATH
            stale_stage.mkdir(parents=True)
            (stale_stage / "Stale.cs").write_text("stale", encoding="utf-8")

            def run_unity(
                command: list[str], *, check: bool
            ) -> subprocess.CompletedProcess:
                self.assertFalse(results_path.exists())
                self.assertTrue(
                    (
                        project_path
                        / run_unity_tests.STAGED_SAMPLE_RELATIVE_PATH
                        / "QuickstartController.cs"
                    ).is_file()
                )
                self.assertFalse((stale_stage / "Stale.cs").exists())
                self.assertTrue(
                    (
                        project_path
                        / run_unity_tests.STAGED_TESTS_RELATIVE_PATH
                        / "QuickstartWorldBuilderTests.cs"
                    ).is_file()
                )
                self.assertIn(
                    (
                        f"{run_unity_tests.TEST_ASSEMBLY};"
                        f"{run_unity_tests.IMPORTED_SAMPLE_TEST_ASSEMBLY}"
                    ),
                    command,
                )
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
                    run_unity_tests,
                    "REPOSITORY_ROOT",
                    repository_root,
                ),
                mock.patch.object(
                    run_unity_tests.subprocess,
                    "run",
                    side_effect=run_unity,
                ) as run_mock,
            ):
                exit_code = run_unity_tests.main()

            self.assertEqual(0, exit_code)
            run_mock.assert_called_once()
            self.assertFalse(
                (project_path / run_unity_tests.STAGING_ROOT_RELATIVE_PATH).exists()
            )
            self.assertIn(
                f'total="{len(run_unity_tests.REQUIRED_TEST_NAMES)}"',
                results_path.read_text(encoding="utf-8"),
            )

    def test_main_propagates_compile_failure_and_cleans_invalid_sample(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            temporary_path = Path(temporary_directory)
            repository_root = temporary_path / "repository"
            project_path = self.create_repository_layout(
                repository_root,
                invalid_sample=True,
            )
            unity_editor = temporary_path / "Unity.exe"
            unity_editor.touch()
            output_directory = temporary_path / "output"

            def fail_compile(
                command: list[str], *, check: bool
            ) -> subprocess.CompletedProcess:
                invalid_source = (
                    project_path
                    / run_unity_tests.STAGED_SAMPLE_RELATIVE_PATH
                    / "Broken.cs"
                )
                self.assertEqual(
                    "this is not valid C#",
                    invalid_source.read_text(encoding="utf-8"),
                )
                return subprocess.CompletedProcess(command, 1)

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
                    run_unity_tests,
                    "REPOSITORY_ROOT",
                    repository_root,
                ),
                mock.patch.object(
                    run_unity_tests.subprocess,
                    "run",
                    side_effect=fail_compile,
                ),
            ):
                exit_code = run_unity_tests.main()

            self.assertEqual(1, exit_code)
            self.assertFalse(
                (project_path / run_unity_tests.STAGING_ROOT_RELATIVE_PATH).exists()
            )

    def test_build_unity_command_selects_both_test_assemblies(self) -> None:
        command = run_unity_tests.build_unity_command(
            Path("Unity"),
            Path("project"),
            Path("results.xml"),
            Path("editor.log"),
        )

        self.assertEqual("-assemblyNames", command[7])
        self.assertEqual(
            (
                f"{run_unity_tests.TEST_ASSEMBLY};"
                f"{run_unity_tests.IMPORTED_SAMPLE_TEST_ASSEMBLY}"
            ),
            command[8],
        )
        self.assertEqual("project", command[10])
        self.assertEqual("results.xml", command[12])
        self.assertEqual("editor.log", command[14])

    @unittest.skipIf(
        sys.platform == "win32", "WSL path conversion is not used on Windows"
    )
    def test_build_unity_command_converts_wsl_paths_for_windows_editor(self) -> None:
        command = run_unity_tests.build_unity_command(
            Path("/mnt/c/Unity/Editor/Unity.exe"),
            Path("/mnt/c/repository/project"),
            Path("/mnt/c/repository/results.xml"),
            Path("/mnt/c/repository/editor.log"),
        )

        self.assertEqual("C:\\repository\\project", command[10])
        self.assertEqual("C:\\repository\\results.xml", command[12])
        self.assertEqual("C:\\repository\\editor.log", command[14])

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
