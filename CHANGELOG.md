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
- Quickstart replay download and playback using the latest deterministic
  evaluation chunk, replay timestamps, episode pauses, and the shared robot.
- Local Unity validation that imports and compiles the real Quickstart sample
  and asserts the canonical world's contract-derived hierarchy and transforms.

### Security

- Require HTTPS and WSS for non-loopback deployment endpoints while preserving
  HTTP and WS for parsed localhost, IPv4 loopback, and IPv6 loopback addresses.
- Bound artifact downloads by format using both response metadata and streamed
  byte counts, while preserving existing destinations and cleaning temporary
  files on rejection or interruption.
- Bound replay manifest metadata, gzip expansion, JSONL row size, and total
  replay steps before untrusted artifacts can exhaust disk, memory, or CPU.
