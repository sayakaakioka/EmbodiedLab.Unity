# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial Unity Package Manager manifest and runtime assembly definition.
- Versioned EmbodiedLab v0 schemas and deterministic NJsonSchema C# DTO
  generation.
- Canonical contract fixture checks and generated-code drift detection.
- WebSocket-first job monitoring with conditional HTTP reconciliation, cloud
  cancellation, and streamed public-GCS artifact downloads.
- Stateful `EmbodiedLabJob` Unity API for submit, restore, refresh, monitor,
  cancel, replay-manifest download, and trained-model download.
- Domain-specific scenario JSON persistence, replay manifest and step readers,
  and lazy replay chunk downloads.
- Importable Quickstart sample for the fixed-environment cloud job workflow.
- Canonical Quickstart world rendering and sample-local job history with
  restore, refresh, resumed monitoring, guarded cloud cancellation, safe local
  artifact paths, and record-only removal.
