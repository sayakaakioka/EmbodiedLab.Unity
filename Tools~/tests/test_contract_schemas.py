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
        self.assertIn("ReplayLogStep", first["definitions"])
        self.assertIn("ResultDocument", first["definitions"])
        self.assertIn("ScenarioBundle", first["definitions"])

        schema_version = first["definitions"]["ScenarioBundle"]["properties"][
            "schema_version"
        ]
        self.assertEqual(["scenario-bundle.v0"], schema_version["enum"])
        self.assertEqual("ScenarioBundleSchemaVersion", schema_version["title"])

        nullable_number = first["definitions"]["ForwardCameraSensor"]["properties"][
            "mount_height_max_meters"
        ]
        self.assertEqual("number", nullable_number["type"])
        self.assertIs(nullable_number["x-nullable"], True)

        nullable_reference = first["definitions"]["ResultDocument"]["properties"][
            "progress"
        ]
        self.assertEqual("#/definitions/Progress", nullable_reference["$ref"])
        self.assertIs(nullable_reference["x-nullable"], True)

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
                "mapping": {
                    "collision": "CollisionRewardComponent",
                    "distance_delta": "DistanceDeltaRewardComponent",
                    "per_step": "PerStepRewardComponent",
                    "terminal_reward": "TerminalRewardComponent",
                },
            },
            "SensorSpec": {
                "container": "ScenarioBundle",
                "property": "sensors",
                "mapping": {
                    "distance_sensor": "DistanceSensor",
                    "forward_camera": "ForwardCameraSensor",
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
            self.assertEqual(["type"], base["required"])
            self.assertEqual({"type": {"type": "string"}}, base["properties"])
            self.assertEqual("type", base["discriminator"]["propertyName"])
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
                self.assertNotIn("type", derived["properties"])

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

    def test_conflicting_repeated_definition_is_rejected(self) -> None:
        schemas = contract_schemas.verify_provenance(
            self.schema_directory, self.manifest
        )
        changed = copy.deepcopy(schemas)
        raw, result_document = changed["result-document.schema.json"]
        result_document["$defs"]["ArtifactFormat"]["enum"].append("conflict")
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
