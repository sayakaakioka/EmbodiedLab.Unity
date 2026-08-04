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
- Importable six-step tutorial for the fixed-environment cloud job workflow.
- Canonical tutorial world rendering, guarded cloud cancellation, and safe
  local artifact paths without an application-level history or credential store.
- Tutorial replay download and playback using the latest deterministic
  evaluation chunk, replay timestamps, episode pauses, and the shared robot.
- Local Unity validation that imports and compiles the real tutorial sample
  and asserts the canonical world's contract-derived hierarchy and transforms.
- Package-owned CPU ONNX Runtime 1.24.4 binaries, upstream license/notices, and
  Windows x64-only native plugin import settings.
- Sample-local tutorial ONNX inference using the submitted semantic camera,
  exact current observation/action contract, shared replay robot, deterministic
  Run/Stop reset, and visible contract violations.
- Real-policy Unity Editor inference and Windows x64 Standalone build/run smoke
  validation without adding a public SDK inference API or Sentis dependency.
- Cross-field validation for Result Document, Result Bundle, Replay manifest,
  and Replay Log state invariants.
- Exact downloaded artifact size and SHA-256 verification before atomic replace.
- Scenario-, model-, and session-derived ONNX input and output validation for
  tutorial inference without duplicate observation constants.
- Job and scenario identity validation before Result and Replay artifacts are
  committed to their destinations.

### Changed

- Submission acceptance is now a single server-owned operation; the removed
  client-visible training-start request and recovery exception are no longer
  part of the SDK.
- Synchronized the six strict EmbodiedLab v0 schemas, canonical fixtures, and
  generated DTOs, including explicit training, observation, artifact, and
  train/evaluation Replay metadata.
- Idempotent submission recovery using client-generated request and cancellation
  capabilities, with one safe retry after an ambiguous response loss.

- Replace the operational Quickstart, local history, Advanced panel, and status
  overlay with ordered scenario, connection, job, artifact, replay, and inference
  tutorial responsibilities.
- Display queued results without a known total as waiting for trainer startup
  instead of ambiguous `0/0` progress.
- Keep terminal job states sticky, ignore timestamped stale updates, and still
  accept newer enrichment for the same terminal state.
- Require the canonical ONNX artifact and a declared ONNX format for policy
  download, and require a successful action before Standalone smoke can pass.
- Switch the Unity 6.3 validation project from the deprecated Input Manager to
  Input System 1.17.0 without adding an SDK package dependency.
- Support Unity 2022.3.19f1 as the minimum Editor version by exposing standard
  `Task`-based asynchronous APIs across both Unity 2022.3 and Unity 6.

### Security

- Require HTTPS and WSS for non-loopback deployment endpoints while preserving
  HTTP and WS for parsed localhost, IPv4 loopback, and IPv6 loopback addresses.
- Bound artifact downloads by format using both response metadata and streamed
  byte counts, while preserving existing destinations and cleaning temporary
  files on rejection or interruption.
- Bound replay manifest metadata, gzip expansion, JSONL row size, and total
  replay steps before untrusted artifacts can exhaust disk, memory, or CPU.
- Bound each accumulated result WebSocket message to 1 MiB and one silence
  interval, aborting oversized or indefinitely fragmented streams.
- Bound HTTP Result payloads and error bodies before JSON deserialization.
- Isolate concurrent downloads with operation-specific temporary files and a
  serialized verified commit step for shared destinations.
