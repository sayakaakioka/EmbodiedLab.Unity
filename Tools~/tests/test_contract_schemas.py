from __future__ import annotations

import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "contract_schemas",
    ROOT / "Tools~" / "contract_schemas.py",
)
assert SPEC is not None and SPEC.loader is not None
contract_schemas = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(contract_schemas)


class ContractSchemaTests(unittest.TestCase):
    def setUp(self) -> None:
        self.schema_directory = ROOT / "Schemas~" / "v0"
        self.manifest = ROOT / "Schemas~" / "upstream.json"

    def test_normalized_bundle_is_deterministic_and_uses_supported_references(
        self,
    ) -> None:
        schemas = contract_schemas.verify_provenance(
            self.schema_directory, self.manifest
        )
        first = contract_schemas.build_normalized_bundle(schemas)
        second = contract_schemas.build_normalized_bundle(schemas)

        self.assertEqual(first, second)
        serialized = json.dumps(first, sort_keys=True)
        self.assertNotIn("$defs", serialized)
        self.assertNotIn("#/$defs/", serialized)
        self.assertNotIn('"anyOf"', serialized)
        self.assertNotIn('"oneOf"', serialized)
        self.assertNotIn('"prefixItems"', serialized)
        self.assertIn("ReplayLogStep", first["definitions"])
        self.assertIn("ResultDocument", first["definitions"])
        self.assertIn("ScenarioBundle", first["definitions"])
        self.assertNotIn("SentisModelArtifactLocation", first["definitions"])
        self.assertNotIn("TrainingResponse", first["definitions"])

        submission_response = first["definitions"]["SubmissionResponse"]
        self.assertIn("cancel_token", submission_response["required"])
        cancel_token = submission_response["properties"]["cancel_token"]
        self.assertEqual(32, cancel_token["minLength"])
        self.assertEqual(128, cancel_token["maxLength"])
        self.assertEqual("^[A-Za-z0-9_-]+$", cancel_token["pattern"])
        self.assertEqual(
            [
                "queued",
                "starting",
                "running",
                "cancelling",
                "cancelled",
                "completed",
                "failed",
            ],
            first["definitions"]["ResultStatus"]["enum"],
        )

        schema_version = first["definitions"]["ScenarioBundle"]["properties"][
            "schema_version"
        ]
        self.assertEqual(["scenario-bundle.v0"], schema_version["enum"])
        self.assertEqual("ScenarioBundleSchemaVersion", schema_version["title"])

        onnx_opset = first["definitions"]["OnnxModelArtifactLocation"]["properties"][
            "opset_version"
        ]
        self.assertEqual([18], onnx_opset["enum"])

        forward_step = first["definitions"]["ActionSpace"]["properties"][
            "forward_step_meters"
        ]
        self.assertEqual(10.0, forward_step["maximum"])

        replay_actions = first["definitions"]["ReplayAction"]["properties"]["values"]
        self.assertEqual(2, replay_actions["minItems"])
        self.assertEqual(2, replay_actions["maxItems"])
        self.assertEqual(
            {"$ref": "#/definitions/ReplayActionValue"},
            replay_actions["items"],
        )

        nullable_number = first["definitions"]["ForwardCameraSensor"]["properties"][
            "mount_height_max_meters"
        ]
        self.assertEqual("number", nullable_number["type"])
        self.assertIs(nullable_number["x-nullable"], True)

        nullable_reference = first["definitions"]["ResultDocument"]["properties"][
            "progress"
        ]
        self.assertEqual("#/definitions/Progress", nullable_reference["$ref"])
        self.assertNotIn("x-nullable", nullable_reference)

        self.assertIs(first["definitions"]["WorldSpec"]["additionalProperties"], False)
        self.assertIs(
            first["definitions"]["ResultBundle"]["additionalProperties"], False
        )
        self.assertIs(
            first["definitions"]["ResultDocument"]["additionalProperties"], False
        )
        self.assertEqual(
            {"type": "string"},
            first["definitions"]["ModelOutput"]["properties"]["action_mapping"][
                "additionalProperties"
            ],
        )

    def test_current_discriminated_unions_become_explicit_inheritance(self) -> None:
        schemas = contract_schemas.verify_provenance(
            self.schema_directory, self.manifest
        )
        bundle = contract_schemas.build_normalized_bundle(schemas)
        definitions = bundle["definitions"]

        expected = {
            "RewardComponent": {
                "container": "RewardSpec",
                "property": "components",
                "discriminator": "type",
                "mapping": {
                    "collision": "CollisionRewardComponent",
                    "distance_delta": "DistanceDeltaRewardComponent",
                    "maximum_absolute_forward": "MaximumAbsoluteForwardRewardComponent",
                    "minimum_absolute_angle": "MinimumAbsoluteAngleRewardComponent",
                    "per_step": "PerStepRewardComponent",
                    "terminal_reward": "TerminalRewardComponent",
                },
            },
            "SensorSpec": {
                "container": "ScenarioBundle",
                "property": "sensors",
                "discriminator": "type",
                "mapping": {
                    "distance_sensor": "DistanceSensor",
                    "forward_camera": "ForwardCameraSensor",
                    "goal_vector": "GoalVectorSensor",
                },
            },
            "ReplayBundleChunk": {
                "container": "ReplayBundleManifest",
                "property": "chunks",
                "discriminator": "phase",
                "mapping": {
                    "eval": "EvalReplayBundleChunk",
                    "train": "TrainReplayBundleChunk",
                },
            },
            "ReplayActionValue": {
                "container": "ReplayAction",
                "property": "values",
                "discriminator": "name",
                "mapping": {
                    "forward": "ReplayForwardActionValue",
                    "turn": "ReplayTurnActionValue",
                },
            },
        }

        for base_name, union in expected.items():
            item = definitions[union["container"]]["properties"][union["property"]][
                "items"
            ]
            self.assertEqual({"$ref": f"#/definitions/{base_name}"}, item)

            base = definitions[base_name]
            self.assertIs(base["x-abstract"], True)
            self.assertIs(base["additionalProperties"], False)
            discriminator = union["discriminator"]
            self.assertIn(discriminator, base["required"])
            self.assertEqual({"type": "string"}, base["properties"][discriminator])
            self.assertEqual(discriminator, base["discriminator"]["propertyName"])
            self.assertEqual(
                {
                    wire_value: f"#/definitions/{derived_name}"
                    for wire_value, derived_name in union["mapping"].items()
                },
                base["discriminator"]["mapping"],
            )

            for derived_name in union["mapping"].values():
                derived = definitions[derived_name]
                self.assertEqual(
                    [{"$ref": f"#/definitions/{base_name}"}], derived["allOf"]
                )
                self.assertNotIn(discriminator, derived["properties"])

        replay_chunk = definitions["ReplayBundleChunk"]
        self.assertIn("checkpoint_step", replay_chunk["properties"])
        self.assertIn("sha256", replay_chunk["properties"])
        self.assertIn("size_bytes", replay_chunk["properties"])
        for chunk_name in ("EvalReplayBundleChunk", "TrainReplayBundleChunk"):
            self.assertIn("path", definitions[chunk_name]["properties"])
            self.assertIn("step_count", definitions[chunk_name]["properties"])
        replay_action_value = definitions["ReplayActionValue"]
        self.assertIn("value", replay_action_value["properties"])

        null_only_properties = {
            "EvalReplayBundleChunk": ("end_step", "start_step"),
            "TrainReplayBundleChunk": (
                "avg_reward",
                "avg_steps",
                "episode_count",
                "success_rate",
            ),
        }
        for owner, property_names in null_only_properties.items():
            for property_name in property_names:
                property_schema = definitions[owner]["properties"][property_name]
                self.assertEqual("object", property_schema["type"])
                self.assertIs(property_schema["x-nullable"], True)

        for placeholder in (
            "AvgReward",
            "AvgSteps",
            "EndStep",
            "EpisodeCount",
            "StartStep",
            "SuccessRate",
        ):
            self.assertNotIn(placeholder, definitions)

    def test_changed_nullable_shape_is_rejected(self) -> None:
        schemas = contract_schemas.verify_provenance(
            self.schema_directory, self.manifest
        )
        changed = copy.deepcopy(schemas)
        raw, scenario = changed["scenario-bundle.schema.json"]
        scenario["$defs"]["ForwardCameraSensor"]["properties"][
            "mount_height_max_meters"
        ]["anyOf"].append({"type": "string"})
        changed["scenario-bundle.schema.json"] = (raw, scenario)

        with self.assertRaisesRegex(
            contract_schemas.ContractSchemaError, "nullable anyOf"
        ):
            contract_schemas.build_normalized_bundle(changed)

    def test_changed_discriminated_union_is_rejected(self) -> None:
        schemas = contract_schemas.verify_provenance(
            self.schema_directory, self.manifest
        )
        changed = copy.deepcopy(schemas)
        raw, scenario = changed["scenario-bundle.schema.json"]
        del scenario["properties"]["sensors"]["items"]["discriminator"]["mapping"][
            "distance_sensor"
        ]
        changed["scenario-bundle.schema.json"] = (raw, scenario)

        with self.assertRaisesRegex(
            contract_schemas.ContractSchemaError, "discriminated union"
        ):
            contract_schemas.build_normalized_bundle(changed)

    def test_changed_semantic_one_of_with_same_branch_count_is_rejected(self) -> None:
        schemas = contract_schemas.verify_provenance(
            self.schema_directory, self.manifest
        )
        changed = copy.deepcopy(schemas)
        raw, result_document = changed["result-document.schema.json"]
        result_document["oneOf"][0]["properties"]["result_bundle"] = {
            "not": {"type": "null"}
        }
        changed["result-document.schema.json"] = (raw, result_document)

        with self.assertRaisesRegex(
            contract_schemas.ContractSchemaError, "semantic oneOf"
        ):
            contract_schemas.build_normalized_bundle(changed)

    def test_conflicting_repeated_definition_is_rejected(self) -> None:
        schemas = contract_schemas.verify_provenance(
            self.schema_directory, self.manifest
        )
        changed = copy.deepcopy(schemas)
        raw, result_document = changed["result-document.schema.json"]
        result_document["$defs"]["ArtifactStorage"]["enum"].append("conflict")
        changed["result-document.schema.json"] = (raw, result_document)

        with self.assertRaisesRegex(
            contract_schemas.ContractSchemaError, "Conflicting definition"
        ):
            contract_schemas.build_normalized_bundle(changed)

    def test_sync_check_detects_changed_schema(self) -> None:
        revision = json.loads(self.manifest.read_text(encoding="utf-8"))["revision"]
        with tempfile.TemporaryDirectory() as temporary:
            destination = Path(temporary) / "Schemas~" / "v0"
            contract_schemas.sync_schemas(
                self.schema_directory, destination, revision, check=False
            )
            target = destination / "submission-response.schema.json"
            target.write_text(
                target.read_text(encoding="utf-8") + "\n", encoding="utf-8"
            )

            with self.assertRaisesRegex(
                contract_schemas.ContractSchemaError, "differs"
            ):
                contract_schemas.sync_schemas(
                    self.schema_directory, destination, revision, check=True
                )


if __name__ == "__main__":
    unittest.main()
