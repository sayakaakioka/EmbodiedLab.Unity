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
                        "Submit, monitor, cancel, download, replay, and run ONNX "
                        "inference for a fixed-environment EmbodiedLab job."
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
            "EmbodiedLabJob.Restore",
            "RefreshAsync",
            "WaitForCompletionAsync",
            "CancelAsync",
            "DownloadModelAsync",
            "DownloadReplayBundleAsync",
            "EmbodiedLabReplay.ReadManifest",
            "DownloadReplayChunkAsync",
            "EmbodiedLabReplay.ReadSteps",
        ):
            with self.subTest(required_call=required_call):
                self.assertIn(required_call, controller)

    def test_sample_splits_history_and_world_responsibilities(self) -> None:
        expected_files = (
            "QuickstartController.cs",
            "QuickstartHistoryRecord.cs",
            "QuickstartHistoryStore.cs",
            "QuickstartWorldBuilder.cs",
            "QuickstartReplayPlayer.cs",
            "QuickstartReplayTimeline.cs",
            "QuickstartInferenceMath.cs",
            "QuickstartInferenceRunner.cs",
            "QuickstartModeCoordinator.cs",
            "QuickstartOnnxContract.cs",
            "QuickstartOnnxPolicy.cs",
            "QuickstartSemanticCamera.cs",
        )

        for filename in expected_files:
            with self.subTest(filename=filename):
                path = SAMPLE_DIRECTORY / filename
                self.assertTrue(path.is_file())
                self.assertTrue(path.with_suffix(path.suffix + ".meta").is_file())

        controller = (SAMPLE_DIRECTORY / "QuickstartController.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn('"job-history.json"', controller)
        self.assertIn("Application.persistentDataPath", controller)
        self.assertIn("Local history (newest first)", controller)
        self.assertIn("QuickstartLocalPaths.GetModelPath", controller)

    def test_controller_guards_cloud_job_operations(self) -> None:
        controller = (SAMPLE_DIRECTORY / "QuickstartController.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn("submissionRequestRunning = true;", controller)
        self.assertLess(
            controller.index("submissionRequestRunning = true;"),
            controller.index("EmbodiedLabJob.SubmitAsync"),
        )
        self.assertIn("!submissionRequestRunning", controller)
        self.assertIn("!cancelRequestRunning", controller)
        self.assertIn("ReferenceEquals(job, activeJob)", controller)
        self.assertIn("Confirm: Cancel Active Cloud Job", controller)
        self.assertIn("Cloud cancellation target:", controller)
        self.assertIn("Active cloud target", controller)
        self.assertIn("restorePhaseCompleted", controller)

        for method_name in ("CanSubmit", "CanDownloadModel", "CanSelectHistory"):
            method = re.search(
                rf"private bool {method_name}\(\)\s*\{{(?P<body>.*?)\n        \}}",
                controller,
                re.DOTALL,
            )
            self.assertIsNotNone(method)
            body = method.group("body") if method else ""
            for operation_guard in (
                "submissionRequestRunning",
                "restoreRunning",
                "cancelRequestRunning",
                "modelDownloadRunning",
                "replayDownloadRunning",
            ):
                with self.subTest(
                    method_name=method_name, operation_guard=operation_guard
                ):
                    self.assertIn(operation_guard, body)

    def test_replay_uses_selected_chunk_and_shared_robot(self) -> None:
        controller = (SAMPLE_DIRECTORY / "QuickstartController.cs").read_text(
            encoding="utf-8"
        )
        player = (SAMPLE_DIRECTORY / "QuickstartReplayPlayer.cs").read_text(
            encoding="utf-8"
        )
        timeline = (SAMPLE_DIRECTORY / "QuickstartReplayTimeline.cs").read_text(
            encoding="utf-8"
        )

        for control in ("Download Replay", "Play Replay", "Stop Replay"):
            with self.subTest(control=control):
                self.assertIn(f'"{control}"', controller)

        manifest_index = controller.index("EmbodiedLabReplay.ReadManifest")
        selection_index = controller.index(
            "SelectLatestDeterministicEvaluationChunk", manifest_index
        )
        chunk_index = controller.index("DownloadReplayChunkAsync", selection_index)
        steps_index = controller.index("EmbodiedLabReplay.ReadSteps", chunk_index)
        self.assertLess(manifest_index, selection_index)
        self.assertLess(selection_index, chunk_index)
        self.assertLess(chunk_index, steps_index)
        self.assertIn("worldBuilder?.RobotTransform", controller)
        self.assertIn("activeRobot.position", player)
        self.assertIn("activeRobot.rotation", player)
        self.assertIn("EpisodePauseSeconds", timeline)
        self.assertIn("TimeSeconds", timeline)

    def test_inference_uses_shared_world_and_exact_contract(self) -> None:
        controller = (SAMPLE_DIRECTORY / "QuickstartController.cs").read_text(
            encoding="utf-8"
        )
        contract = (SAMPLE_DIRECTORY / "QuickstartOnnxContract.cs").read_text(
            encoding="utf-8"
        )
        runner = (SAMPLE_DIRECTORY / "QuickstartInferenceRunner.cs").read_text(
            encoding="utf-8"
        )

        for control in ("Run Inference", "Stop Inference"):
            self.assertIn(f'"{control}"', controller)
        self.assertIn("new QuickstartInferenceRunner(activeWorld)", controller)
        self.assertIn("modeCoordinator?.EnterInference()", controller)
        self.assertIn('ImageInputName = "obs_0"', contract)
        self.assertIn('NumericInputName = "obs_1"', contract)
        self.assertIn("ImageHeight = 84", contract)
        self.assertIn("ImageWidth = 112", contract)
        self.assertIn("ForwardMetersPerDecision = 0.2f", runner)
        self.assertIn("TurnDegreesPerDecision = 15f", runner)
        self.assertNotIn(
            "Sentis", "\n".join(path.name for path in SAMPLE_DIRECTORY.iterdir())
        )

    def test_local_failures_do_not_discard_submitted_job(self) -> None:
        controller = (SAMPLE_DIRECTORY / "QuickstartController.cs").read_text(
            encoding="utf-8"
        )

        attach_index = controller.index("AttachJob(submittedJob)")
        persistence_index = controller.index("TryPersistHistoryRecord(record)")
        world_index = controller.index("TryBuildWorld(scenario)")
        self.assertLess(attach_index, persistence_index)
        self.assertLess(attach_index, world_index)
        self.assertIn("cloud monitoring remains active", controller)
        self.assertIn(
            "Keep this scene open to retain the active job handle", controller
        )
        self.assertIn("selectedHistoryRecordDirty", controller)
        self.assertIn("RetryDirtyHistory();", controller)
        self.assertIn("changed || selectedHistoryRecordDirty", controller)

        record_index = controller.index("var record = new QuickstartHistoryRecord")
        destroyed_index = controller.index("if (destroyed)", record_index)
        detached_save_index = controller.index(
            "TryPersistDetachedHistoryRecord(record)", destroyed_index
        )
        self.assertLess(record_index, destroyed_index)
        self.assertLess(destroyed_index, detached_save_index)

    def test_canonical_scenario_drives_visible_world(self) -> None:
        builder = (SAMPLE_DIRECTORY / "QuickstartWorldBuilder.cs").read_text(
            encoding="utf-8"
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

        for visual in (
            '"Floor"',
            '"Robot"',
            '"Goal"',
            '"Overview Camera"',
            '"Tutorial Light"',
        ):
            with self.subTest(visual=visual):
                self.assertIn(visual, builder)

        self.assertNotIn("JsonConvert", builder)

    def test_record_removal_is_explicit_and_local_only(self) -> None:
        controller = (SAMPLE_DIRECTORY / "QuickstartController.cs").read_text(
            encoding="utf-8"
        )
        confirmation = re.search(
            r"private void ConfirmRecordRemoval\(\)\s*\{(?P<body>.*?)\n        \}",
            controller,
            re.DOTALL,
        )
        self.assertIsNotNone(confirmation)
        body = confirmation.group("body") if confirmation else ""

        self.assertIn("historyStore.Remove", body)
        self.assertNotIn("CancelAsync", body)
        self.assertNotIn("File.Delete", body)
        self.assertNotIn("Directory.Delete", body)
        self.assertIn("Confirm: Remove Local Record Only", controller)
        self.assertIn("does not cancel the cloud job", controller)
        self.assertIn("cancellation capability will be", controller)

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


if __name__ == "__main__":
    unittest.main()
