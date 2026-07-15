from __future__ import annotations

import json
from pathlib import Path
import re
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
SAMPLE_DIRECTORY = REPOSITORY_ROOT / "Samples~" / "Quickstart"


def read_guid(path: Path) -> str:
    match = re.search(
        r"^guid: ([0-9a-f]{32})$",
        path.read_text(encoding="utf-8"),
        re.MULTILINE,
    )
    if match is None:
        raise AssertionError(f"Unity GUID not found in {path}")
    return match.group(1)


class QuickstartSampleTests(unittest.TestCase):
    def test_package_registers_importable_sample(self) -> None:
        package = json.loads(
            (REPOSITORY_ROOT / "package.json").read_text(encoding="utf-8")
        )

        self.assertEqual(
            package["samples"],
            [
                {
                    "displayName": "Quickstart",
                    "description": (
                        "Submit, monitor, cancel, and download a "
                        "fixed-environment EmbodiedLab training job."
                    ),
                    "path": "Samples~/Quickstart",
                }
            ],
        )

    def test_sample_assembly_references_runtime_only(self) -> None:
        assembly = json.loads(
            (
                SAMPLE_DIRECTORY / "EmbodiedLab.Unity.Samples.Quickstart.asmdef"
            ).read_text(encoding="utf-8")
        )

        self.assertEqual(
            assembly["name"],
            "EmbodiedLab.Unity.Samples.Quickstart",
        )
        self.assertEqual(assembly["references"], ["EmbodiedLab.Unity"])

    def test_scene_references_controller_and_fixed_scenario(self) -> None:
        scene = (SAMPLE_DIRECTORY / "Quickstart.unity").read_text(encoding="utf-8")
        controller_guid = read_guid(SAMPLE_DIRECTORY / "QuickstartController.cs.meta")
        scenario_guid = read_guid(SAMPLE_DIRECTORY / "NavigationScenario.json.meta")

        self.assertIn(
            f"m_Script: {{fileID: 11500000, guid: {controller_guid}, type: 3}}",
            scene,
        )
        self.assertIn(
            f"scenarioJson: {{fileID: 4900000, guid: {scenario_guid}, type: 3}}",
            scene,
        )
        self.assertIn(
            "m_EditorClassIdentifier: "
            "EmbodiedLab.Unity.Samples.Quickstart::"
            "EmbodiedLab.Unity.Samples.Quickstart.QuickstartController",
            scene,
        )

    def test_controller_exercises_supported_job_flow(self) -> None:
        controller = (SAMPLE_DIRECTORY / "QuickstartController.cs").read_text(
            encoding="utf-8"
        )

        for required_call in (
            "ScenarioBundleJson.Deserialize",
            "EmbodiedLabJob.SubmitAsync",
            "WaitForCompletionAsync",
            "CancelAsync",
            "DownloadModelAsync",
        ):
            with self.subTest(required_call=required_call):
                self.assertIn(required_call, controller)

    def test_sample_scenario_matches_canonical_fixture(self) -> None:
        sample_scenario = json.loads(
            (SAMPLE_DIRECTORY / "NavigationScenario.json").read_text(encoding="utf-8")
        )
        canonical_scenario = json.loads(
            (
                REPOSITORY_ROOT
                / "Tests~"
                / "Fixtures"
                / "navigation_default_scenario_bundle.json"
            ).read_text(encoding="utf-8")
        )

        self.assertEqual(sample_scenario, canonical_scenario)


if __name__ == "__main__":
    unittest.main()
