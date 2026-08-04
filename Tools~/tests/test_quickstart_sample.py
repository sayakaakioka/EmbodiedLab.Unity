from __future__ import annotations

import json
import re
import unittest
from pathlib import Path

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
    def test_package_registers_tutorial(self) -> None:
        package = json.loads(
            (REPOSITORY_ROOT / "package.json").read_text(encoding="utf-8")
        )

        self.assertEqual(
            package["samples"],
            [
                {
                    "displayName": "Tutorial",
                    "description": (
                        "Learn the fixed-environment EmbodiedLab workflow in six small "
                        "steps, from scenario loading to replay and ONNX inference."
                    ),
                    "path": "Samples~/Quickstart",
                }
            ],
        )
        self.assertEqual(package["dependencies"]["com.unity.modules.imgui"], "1.0.0")
        self.assertEqual(package["dependencies"]["com.unity.modules.physics"], "1.0.0")

    def test_sample_assembly_references_runtime_only(self) -> None:
        assembly = json.loads(
            (
                SAMPLE_DIRECTORY / "EmbodiedLab.Unity.Samples.Quickstart.asmdef"
            ).read_text(encoding="utf-8")
        )

        self.assertEqual(assembly["name"], "EmbodiedLab.Unity.Samples.Quickstart")
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
            "EmbodiedLab.Unity.Samples.Quickstart::"
            "EmbodiedLab.Unity.Samples.Quickstart.QuickstartController",
            scene,
        )

    def test_tutorial_has_six_ordered_small_steps(self) -> None:
        readme = (SAMPLE_DIRECTORY / "README.md").read_text(encoding="utf-8")
        headings = (
            "## 1. Load the fixed scenario",
            "## 2. Connect to EmbodiedLab",
            "## 3. Submit and monitor a job",
            "## 4. Download the result",
            "## 5. Play the replay",
            "## 6. Run the policy on Windows x64",
        )

        positions = [readme.index(heading) for heading in headings]
        self.assertEqual(positions, sorted(positions))
        self.assertIn("QuickstartCloudJob.cs", readme)
        self.assertIn("QuickstartArtifacts.cs", readme)
        self.assertIn("QuickstartProgressText.cs", readme)
        self.assertIn("EmbodiedLabJob.Restore", readme)

    def test_tutorial_splits_real_responsibilities(self) -> None:
        expected_files = (
            "QuickstartController.cs",
            "QuickstartController.View.cs",
            "QuickstartCloudJob.cs",
            "QuickstartArtifacts.cs",
            "QuickstartProgressText.cs",
            "QuickstartWorldBuilder.cs",
            "QuickstartReplayPlayer.cs",
            "QuickstartReplayTimeline.cs",
            "QuickstartInferenceMath.cs",
            "QuickstartInferenceRunner.cs",
            "QuickstartOnnxContract.cs",
            "QuickstartOnnxPolicy.cs",
            "QuickstartSemanticCamera.cs",
        )

        for filename in expected_files:
            with self.subTest(filename=filename):
                path = SAMPLE_DIRECTORY / filename
                self.assertTrue(path.is_file())
                self.assertTrue(path.with_suffix(path.suffix + ".meta").is_file())

        removed_files = (
            "QuickstartHistoryRecord.cs",
            "QuickstartHistoryStore.cs",
            "QuickstartLogOverlay.cs",
            "QuickstartModeCoordinator.cs",
        )
        for filename in removed_files:
            with self.subTest(removed_filename=filename):
                self.assertFalse((SAMPLE_DIRECTORY / filename).exists())

    def test_job_and_artifact_files_use_supported_sdk_flow(self) -> None:
        cloud_job = (SAMPLE_DIRECTORY / "QuickstartCloudJob.cs").read_text(
            encoding="utf-8"
        )
        artifacts = (SAMPLE_DIRECTORY / "QuickstartArtifacts.cs").read_text(
            encoding="utf-8"
        )

        for required_call in (
            "EmbodiedLabJob.SubmitAsync",
            "WaitForCompletionAsync",
            "CancelAsync",
        ):
            with self.subTest(cloud_call=required_call):
                self.assertIn(required_call, cloud_job)

        for required_call in (
            "RefreshAsync",
            "DownloadModelAsync",
            "DownloadReplayBundleAsync",
            "EmbodiedLabReplay.ReadManifest",
            "DownloadReplayChunkAsync",
            "EmbodiedLabReplay.ReadSteps",
        ):
            with self.subTest(artifact_call=required_call):
                self.assertIn(required_call, artifacts)

    def test_removed_operational_ui_does_not_remain(self) -> None:
        sample_sources = "\n".join(
            path.read_text(encoding="utf-8") for path in SAMPLE_DIRECTORY.glob("*.cs")
        )
        readme = (SAMPLE_DIRECTORY / "README.md").read_text(encoding="utf-8")

        for removed_text in (
            "QuickstartHistoryStore",
            "QuickstartHistoryRecord",
            "job-history.json",
            "Show Advanced",
            "Local history (newest first)",
            "QuickstartLogOverlay",
        ):
            with self.subTest(removed_text=removed_text):
                self.assertNotIn(removed_text, sample_sources)
                self.assertNotIn(removed_text, readme)

    def test_tutorial_ui_is_large_scrollable_and_ordered(self) -> None:
        view = (SAMPLE_DIRECTORY / "QuickstartController.View.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn("BodyFontSize = 30", view)
        self.assertIn("TitleFontSize = 44", view)
        self.assertIn("GUILayout.BeginScrollView(panelScrollPosition)", view)
        self.assertIn("GUILayout.EndScrollView();", view)
        self.assertIn("CalculateOverviewViewport", view)
        self.assertIn("overviewCamera.rect", view)
        labels = tuple(f'GUILayout.Label("{step}.' for step in range(1, 7))
        positions = [view.index(label) for label in labels]
        self.assertEqual(positions, sorted(positions))

    def test_canonical_scenario_drives_visible_world(self) -> None:
        builder = (SAMPLE_DIRECTORY / "QuickstartWorldBuilder.cs").read_text(
            encoding="utf-8"
        )
        scenario = json.loads(
            (SAMPLE_DIRECTORY / "NavigationScenario.json").read_text(encoding="utf-8")
        )

        for contract_member in (
            "scenario.World",
            "world.Bounds",
            "world.StaticWalls",
            "world.StaticObstacles",
            "scenario.Robot",
            "world.Goal",
        ):
            with self.subTest(contract_member=contract_member):
                self.assertIn(contract_member, builder)

        self.assertNotIn("JsonConvert", builder)
        self.assertEqual(
            [wall["height"] for wall in scenario["world"]["static_walls"]],
            [2.0, 2.0, 2.0, 2.0],
        )
        self.assertEqual(
            [
                (obstacle["id"], obstacle["height"])
                for obstacle in scenario["world"]["static_obstacles"]
            ],
            [
                ("obstacle_a", 1.0),
                ("obstacle_b", 1.0),
                ("obstacle_c", 1.0),
                ("obstacle_d", 1.0),
            ],
        )
        forward_camera = next(
            sensor
            for sensor in scenario["sensors"]
            if sensor["type"] == "forward_camera"
        )
        self.assertEqual(
            forward_camera,
            {
                "id": "front_camera",
                "type": "forward_camera",
                "width": 112,
                "height": 84,
                "semantic_mode": "traversable_vs_blocked",
                "mount_height_meters": 0.6,
                "mount_height_min_meters": 0.6,
                "mount_height_max_meters": 0.6,
                "pitch_degrees": 0.0,
                "vertical_fov_degrees": 70.0,
                "near_clip_meters": 0.05,
                "far_clip_meters": 100.0,
            },
        )

    def test_replay_and_inference_use_the_shared_robot(self) -> None:
        artifacts = (SAMPLE_DIRECTORY / "QuickstartArtifacts.cs").read_text(
            encoding="utf-8"
        )
        player = (SAMPLE_DIRECTORY / "QuickstartReplayPlayer.cs").read_text(
            encoding="utf-8"
        )
        controller = (SAMPLE_DIRECTORY / "QuickstartController.cs").read_text(
            encoding="utf-8"
        )
        runner = (SAMPLE_DIRECTORY / "QuickstartInferenceRunner.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn("replayPlayer.Load(robot", artifacts)
        self.assertIn("activeRobot.position", player)
        self.assertIn("activeRobot.rotation", player)
        self.assertIn("new QuickstartInferenceRunner(activeWorld)", controller)
        self.assertLess(
            controller.index("worldBuilder.Build(scenario);"),
            controller.index(
                "UpdateOverviewCameraViewport();",
                controller.index("worldBuilder.Build(scenario);"),
            ),
        )
        self.assertIn("ForwardMetersPerDecision = 0.2f", runner)
        self.assertIn("TurnDegreesPerDecision = 15f", runner)
        self.assertNotIn(
            "Sentis", "\n".join(path.name for path in SAMPLE_DIRECTORY.iterdir())
        )

    def test_standalone_smoke_requires_a_successful_action(self) -> None:
        smoke = (
            REPOSITORY_ROOT
            / "Tests~"
            / "QuickstartStandaloneSmoke"
            / "QuickstartStandaloneSmoke.cs"
        ).read_text(encoding="utf-8")

        stopped_index = smoke.index("if (!runner.IsRunning)")
        action_index = smoke.index('runner.ActionStatus.StartsWith("raw f="')
        successful_finish = re.search(r"Finish\(\s*true,", smoke[action_index:])
        self.assertLess(stopped_index, action_index)
        self.assertIsNotNone(successful_finish)

    def test_all_sample_sources_compile_in_compatibility_project(self) -> None:
        project = (
            REPOSITORY_ROOT
            / "Tools~"
            / "TransportCompatibility"
            / "TransportCompatibility.csproj"
        ).read_text(encoding="utf-8")
        self.assertIn("../../Samples~/Quickstart/*.cs", project)

    def test_ci_runs_quickstart_behaviors(self) -> None:
        workflow = (
            REPOSITORY_ROOT / ".github" / "workflows" / "contracts.yml"
        ).read_text(encoding="utf-8")
        self.assertIn("Tools~/QuickstartTests/QuickstartTests.csproj", workflow)

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
        self.assertEqual(sample_scenario["world"]["goal"]["radius"], 0.45)
        self.assertEqual(
            sample_scenario["world"]["goal"]["radius"],
            sample_scenario["robot"]["radius"],
        )


if __name__ == "__main__":
    unittest.main()
