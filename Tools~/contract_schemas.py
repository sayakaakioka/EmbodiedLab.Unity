#!/usr/bin/env python3
"""Sync and normalize the current EmbodiedLab v0 contract schemas."""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any


REPOSITORY = "https://github.com/sayakaakioka/EmbodiedLab"
SCHEMA_VERSION = "v0"
SCHEMA_DIALECT = "https://json-schema.org/draft/2020-12/schema"
EXPECTED_SCHEMAS = {
    "replay-bundle-manifest.schema.json": "ReplayBundleManifest",
    "replay-log-step.schema.json": "ReplayLogStep",
    "result-bundle.schema.json": "ResultBundle",
    "result-document.schema.json": "ResultDocument",
    "scenario-bundle.schema.json": "ScenarioBundle",
    "submission-response.schema.json": "SubmissionResponse",
}
UNSUPPORTED_KEYWORDS = {
    "$dynamicAnchor",
    "$dynamicRef",
    "$recursiveAnchor",
    "$recursiveRef",
    "$vocabulary",
    "dependentSchemas",
    "prefixItems",
    "unevaluatedItems",
    "unevaluatedProperties",
}
CURRENT_DISCRIMINATED_UNIONS = (
    (
        "RewardComponent",
        "RewardSpec",
        "components",
        {
            "collision": "CollisionRewardComponent",
            "distance_delta": "DistanceDeltaRewardComponent",
            "per_step": "PerStepRewardComponent",
            "terminal_reward": "TerminalRewardComponent",
        },
    ),
    (
        "SensorSpec",
        "ScenarioBundle",
        "sensors",
        {
            "distance_sensor": "DistanceSensor",
            "forward_camera": "ForwardCameraSensor",
        },
    ),
)

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SCHEMA_DIRECTORY = ROOT / "Schemas~" / SCHEMA_VERSION
DEFAULT_MANIFEST = ROOT / "Schemas~" / "upstream.json"


class ContractSchemaError(ValueError):
    """Raised when a schema is outside the agreed current contract subset."""


def _json_bytes(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode()


def _read_source_schemas(directory: Path) -> dict[str, tuple[bytes, dict[str, Any]]]:
    actual = {path.name for path in directory.glob("*.schema.json")}
    expected = set(EXPECTED_SCHEMAS)
    if actual != expected:
        missing = sorted(expected - actual)
        extra = sorted(actual - expected)
        raise ContractSchemaError(
            f"Schema set mismatch; missing={missing}, extra={extra}"
        )

    schemas: dict[str, tuple[bytes, dict[str, Any]]] = {}
    for filename, expected_title in EXPECTED_SCHEMAS.items():
        raw = (directory / filename).read_bytes()
        schema = json.loads(raw)
        if schema.get("$schema") != SCHEMA_DIALECT:
            raise ContractSchemaError(f"Unexpected schema dialect in {filename}")
        if schema.get("title") != expected_title:
            raise ContractSchemaError(f"Unexpected root title in {filename}")
        schemas[filename] = (raw, schema)
    return schemas


def _manifest(
    revision: str, schemas: dict[str, tuple[bytes, dict[str, Any]]]
) -> dict[str, Any]:
    if re.fullmatch(r"[0-9a-f]{40}", revision) is None:
        raise ContractSchemaError(
            "The upstream revision must be a lowercase 40-character commit SHA"
        )
    return {
        "files": {
            filename: hashlib.sha256(raw).hexdigest()
            for filename, (raw, _) in sorted(schemas.items())
        },
        "repository": REPOSITORY,
        "revision": revision,
        "schemaVersion": SCHEMA_VERSION,
    }


def sync_schemas(source: Path, destination: Path, revision: str, check: bool) -> None:
    schemas = _read_source_schemas(source)
    manifest = _manifest(revision, schemas)
    manifest_path = destination.parent / "upstream.json"

    if check:
        for filename, (raw, _) in schemas.items():
            target = destination / filename
            if not target.is_file() or target.read_bytes() != raw:
                raise ContractSchemaError(f"Synchronized schema differs: {filename}")
        if not manifest_path.is_file() or manifest_path.read_bytes() != _json_bytes(
            manifest
        ):
            raise ContractSchemaError("Synchronized schema provenance differs")
        return

    destination.mkdir(parents=True, exist_ok=True)
    for stale in destination.glob("*.schema.json"):
        if stale.name not in EXPECTED_SCHEMAS:
            stale.unlink()
    for filename, (raw, _) in schemas.items():
        (destination / filename).write_bytes(raw)
    manifest_path.write_bytes(_json_bytes(manifest))


def verify_provenance(
    directory: Path, manifest_path: Path
) -> dict[str, tuple[bytes, dict[str, Any]]]:
    schemas = _read_source_schemas(directory)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    expected = _manifest(manifest.get("revision", ""), schemas)
    if manifest != expected:
        raise ContractSchemaError(
            "Schema provenance does not match the committed schema bytes"
        )
    return schemas


def _pascal_case(value: str) -> str:
    return "".join(
        part[:1].upper() + part[1:]
        for part in re.split(r"[^A-Za-z0-9]+", value)
        if part
    )


def _normalize_nullable_any_of(node: dict[str, Any]) -> dict[str, Any]:
    choices = node.pop("anyOf")
    if not isinstance(choices, list) or len(choices) != 2:
        raise ContractSchemaError(
            "Only the current two-item nullable anyOf form is supported"
        )

    null_choices = [choice for choice in choices if choice == {"type": "null"}]
    non_null_choices = [choice for choice in choices if choice != {"type": "null"}]
    if len(null_choices) != 1 or len(non_null_choices) != 1:
        raise ContractSchemaError(
            "Only the current schema-plus-null nullable anyOf form is supported"
        )

    non_null = non_null_choices[0]
    if not isinstance(non_null, dict):
        raise ContractSchemaError("The non-null nullable anyOf choice must be a schema")

    overlap = {
        key for key in node.keys() & non_null.keys() if node[key] != non_null[key]
    }
    if overlap:
        raise ContractSchemaError(
            f"Conflicting nullable anyOf metadata: {sorted(overlap)}"
        )

    result = {**non_null, **node}
    result["x-nullable"] = True
    return result


def _normalize_node(
    node: Any,
    *,
    owner_title: str | None = None,
    property_name: str | None = None,
) -> Any:
    if isinstance(node, list):
        return [_normalize_node(item, owner_title=owner_title) for item in node]
    if isinstance(node, str):
        if node.startswith("#/$defs/"):
            return node.replace("#/$defs/", "#/definitions/", 1)
        return node
    if not isinstance(node, dict):
        return node

    unsupported = UNSUPPORTED_KEYWORDS.intersection(node)
    if unsupported:
        raise ContractSchemaError(
            f"Unsupported JSON Schema keywords: {sorted(unsupported)}"
        )

    current_owner = node.get("title") if "properties" in node else owner_title
    result: dict[str, Any] = {}
    for key, value in node.items():
        if key == "const":
            if not isinstance(value, str):
                raise ContractSchemaError(
                    "Only the current string const form is supported"
                )
            if "enum" in node:
                raise ContractSchemaError("A schema cannot contain both const and enum")
            result["enum"] = [value]
            continue
        if key == "properties":
            result[key] = {
                name: _normalize_node(
                    child,
                    owner_title=current_owner,
                    property_name=name,
                )
                for name, child in value.items()
            }
            continue
        result[key] = _normalize_node(value, owner_title=current_owner)

    if "anyOf" in result:
        result = _normalize_nullable_any_of(result)
    if "enum" in result and len(result["enum"]) == 1 and owner_title and property_name:
        result["title"] = owner_title + _pascal_case(property_name)
    if "$ref" in result and not result["$ref"].startswith("#/definitions/"):
        raise ContractSchemaError(
            f"Only local definition references are supported: {result['$ref']}"
        )
    return result


def _apply_current_discriminated_unions(definitions: dict[str, Any]) -> None:
    for (
        base_name,
        container_name,
        property_name,
        mapping,
    ) in CURRENT_DISCRIMINATED_UNIONS:
        try:
            items = definitions[container_name]["properties"][property_name]["items"]
        except (KeyError, TypeError) as error:
            raise ContractSchemaError(
                f"Changed current discriminated union: {container_name}.{property_name}"
            ) from error

        expected_mapping = {
            wire_value: f"#/definitions/{derived_name}"
            for wire_value, derived_name in mapping.items()
        }
        expected_references = sorted(expected_mapping.values())
        if (
            not isinstance(items, dict)
            or set(items) != {"discriminator", "oneOf"}
            or items["discriminator"]
            != {"mapping": expected_mapping, "propertyName": "type"}
            or not isinstance(items["oneOf"], list)
            or sorted(
                choice.get("$ref", "")
                for choice in items["oneOf"]
                if isinstance(choice, dict) and set(choice) == {"$ref"}
            )
            != expected_references
            or len(items["oneOf"]) != len(expected_references)
        ):
            raise ContractSchemaError(
                f"Changed current discriminated union: {container_name}.{property_name}"
            )

        if base_name in definitions:
            raise ContractSchemaError(
                f"Discriminated union base already exists: {base_name}"
            )

        for wire_value, derived_name in mapping.items():
            try:
                derived = definitions[derived_name]
                properties = derived["properties"]
                discriminator_property = properties["type"]
            except (KeyError, TypeError) as error:
                raise ContractSchemaError(
                    f"Changed current discriminated union member: {derived_name}"
                ) from error

            if (
                discriminator_property.get("enum") != [wire_value]
                or "allOf" in derived
                or "type" in derived.get("required", [])
            ):
                raise ContractSchemaError(
                    f"Changed current discriminated union member: {derived_name}"
                )

            del properties["type"]
            derived["allOf"] = [{"$ref": f"#/definitions/{base_name}"}]

        definitions[base_name] = {
            "discriminator": {
                "mapping": expected_mapping,
                "propertyName": "type",
            },
            "properties": {"type": {"type": "string"}},
            "required": ["type"],
            "title": base_name,
            "type": "object",
            "x-abstract": True,
        }
        items.clear()
        items["$ref"] = f"#/definitions/{base_name}"


def build_normalized_bundle(
    schemas: dict[str, tuple[bytes, dict[str, Any]]],
) -> dict[str, Any]:
    definitions: dict[str, Any] = {}
    properties: dict[str, Any] = {}

    def add_definition(name: str, value: dict[str, Any]) -> None:
        normalized = _normalize_node(value, owner_title=name)
        if name in definitions and definitions[name] != normalized:
            raise ContractSchemaError(f"Conflicting definition: {name}")
        definitions[name] = normalized

    for filename in sorted(schemas):
        schema = schemas[filename][1]
        for name, definition in schema.get("$defs", {}).items():
            add_definition(name, definition)

        root = copy.deepcopy(schema)
        root.pop("$schema")
        root.pop("$defs", None)
        title = root["title"]
        add_definition(title, root)
        property_name = filename.removesuffix(".schema.json").replace("-", "_")
        properties[property_name] = {"$ref": f"#/definitions/{title}"}

    _apply_current_discriminated_unions(definitions)

    return {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "additionalProperties": False,
        "definitions": definitions,
        "properties": properties,
        "title": "EmbodiedLabContracts",
        "type": "object",
    }


def normalize_schemas(directory: Path, manifest_path: Path, output: Path) -> None:
    schemas = verify_provenance(directory, manifest_path)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(_json_bytes(build_normalized_bundle(schemas)))


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    sync = subparsers.add_parser("sync", help="copy the exact upstream v0 schemas")
    sync.add_argument("--source", type=Path, required=True)
    sync.add_argument("--destination", type=Path, default=DEFAULT_SCHEMA_DIRECTORY)
    sync.add_argument("--revision", required=True)
    sync.add_argument("--check", action="store_true")

    normalize = subparsers.add_parser(
        "normalize", help="create the NJsonSchema input bundle"
    )
    normalize.add_argument("--source", type=Path, default=DEFAULT_SCHEMA_DIRECTORY)
    normalize.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    normalize.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    args = _parse_args()
    try:
        if args.command == "sync":
            sync_schemas(args.source, args.destination, args.revision, args.check)
        else:
            normalize_schemas(args.source, args.manifest, args.output)
    except (ContractSchemaError, FileNotFoundError, json.JSONDecodeError) as error:
        print(f"contract schema error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
