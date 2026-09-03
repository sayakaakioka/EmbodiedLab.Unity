# EmbodiedLab.Unity

Unity SDK for submitting and monitoring EmbodiedLab cloud training jobs and
downloading results, replays, and trained models.

> [!IMPORTANT]
> This package is in early development. It does not have a stable public API or
> a published release yet.

## Scope

This repository provides the reusable Unity-side cloud job functionality
shared by EnvForge and custom Unity frontends. The first supported workflow is:

- submit a fixed-environment training job;
- monitor its lifecycle;
- download its result document, replay bundle, and trained model.

Reusable Editor UI, application-level job history, credential persistence, and
EnvForge-specific scene authoring remain in
[EnvForge](https://github.com/sayakaakioka/EnvForge). The package tutorial keeps
one in-memory job and teaches restore as an explicit SDK operation instead of
implementing a second application workflow.
Server behavior and the source contract models remain in
[EmbodiedLab](https://github.com/sayakaakioka/EmbodiedLab).

## Requirements

- Unity 2022.3.19f1 or later
- Git 2.14 or later when installing from a Git URL

Direct ONNX inference in the tutorial is verified with Unity 2022.3.19f1 and
6000.3.11f1 on Windows x64 Editor and Windows x64 Standalone. The package owns
the required CPU ONNX Runtime 1.24.4 managed and native binaries; no separate
ONNX Runtime installation is required on that target.

## Installation

Until versioned releases are available, add the repository from Unity Package
Manager using this Git URL:

```text
https://github.com/sayakaakioka/EmbodiedLab.Unity.git
```

The package identifier is `com.embodiedlab.unity`.

## Import the tutorial

In Package Manager, select **EmbodiedLab Unity SDK**, open the **Samples** tab,
and import **Tutorial**. Then open
`Assets/Samples/EmbodiedLab Unity SDK/0.1.0/Tutorial/Quickstart.unity`
and enter the API and result WebSocket base URLs in Play Mode.

The tutorial has six ordered sections: load the exact scenario, configure
endpoints, submit and monitor one job, download artifacts, play the deterministic
evaluation replay, and run the downloaded ONNX policy on Windows x64. The
responsibilities are separated into sample-internal classes. Replay and inference use
the same visible robot and are mutually exclusive. The tutorial intentionally
does not include EnvForge's scene authoring, job history, or credential store.

## Quick start

Create the deployment endpoints once, build a contract `ScenarioBundle`, and
submit it through the stateful job handle:

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity;
using UnityEngine;

public async Task RunTrainingAsync(
    ScenarioBundle scenario,
    CancellationToken cancellationToken)
{
    var endpoints = new EmbodiedLabEndpoints(
        "https://api.example.com/",
        "wss://results.example.com/");

    using EmbodiedLabJob job = await EmbodiedLabJob.SubmitAsync(
        endpoints,
        scenario,
        cancellationToken);
    job.ResultUpdated += result =>
        Debug.Log($"{result.Status}: {result.Progress?.CurrentStep}");

    ResultDocument result = await job.WaitForCompletionAsync(cancellationToken);
    if (result.Status != ResultStatus.Completed)
    {
        return;
    }

    string outputDirectory = Path.Combine(Application.persistentDataPath, "job-1");
    await job.DownloadReplayBundleAsync(
        Path.Combine(outputDirectory, "replay", "manifest.json"),
        cancellationToken);
    await job.DownloadModelAsync(
        Path.Combine(outputDirectory, "policy.onnx"),
        cancellationToken);
}
```

Remote deployments must use `https` for the API and `wss` for the result
stream. Plaintext `http` and `ws` are accepted only when the parsed host is
loopback (`localhost`, an IPv4 loopback address, or an IPv6 loopback address),
so local development remains possible without exposing job data or cancellation
capabilities over a remote plaintext connection.

`SubmitAsync` sends one submission request. The server validates and stores the
scenario, creates the queued result, and owns training dispatch. Result
monitoring uses the WebSocket stream while it is healthy; HTTP result reads are
reserved for explicit `RefreshAsync` calls and recovery after a failed,
disconnected, or silent stream. `ResultUpdated` is dispatched through the
synchronization context captured when the job handle is created, which is
normally Unity's main thread context.

If submission creation fails, `SubmitAsync` fails without returning a job
handle. Dispatch or training failures after acceptance are server-owned and are
reported through the job's terminal `failed` Result Document.

Submission creation uses a client-generated idempotency key and cancellation
capability. If the HTTP response is lost ambiguously, the transport retries once
with the same values. A compatible API resolves the retry to the original
submission and echoes the same capability; a different capability is rejected.

The replay-bundle artifact currently points to its manifest, so
`DownloadReplayBundleAsync` saves that manifest. `DownloadModelAsync` requires
an `onnx_model` artifact that declares the ONNX format; it does not fall back to
Sentis or generic model artifacts. Result artifacts exist only at
`job.LatestResult?.ResultBundle?.Artifacts`; the SDK does
not expose the removed top-level result artifact field.

Read a saved scenario with the generated concrete sensor and reward types intact:

```csharp
string scenarioJson = ScenarioBundleJson.Serialize(scenario, indented: true);
ScenarioBundle restoredScenario = ScenarioBundleJson.Deserialize(scenarioJson);
```

Replay bundles remain lazy. Download and read the manifest first, then download
only the selected compressed chunk:

```csharp
string manifestPath = Path.Combine(
    outputDirectory,
    "replay",
    "manifest.json");
ReplayBundleManifest manifest = EmbodiedLabReplay.ReadManifest(manifestPath);
EvalReplayBundleChunk selectedChunk = manifest.Chunks
    .OfType<EvalReplayBundleChunk>()
    .First();
string replayChunkPath = Path.Combine(
    outputDirectory,
    "replay",
    selectedChunk.Path);

await job.DownloadReplayChunkAsync(
    selectedChunk,
    replayChunkPath,
    cancellationToken);
IReadOnlyList<ReplayLogStep> steps =
    EmbodiedLabReplay.ReadSteps(replayChunkPath);
```

Add `System.Collections.Generic` and `System.Linq` for the collection types and
`First` call in this example. `EmbodiedLabReplay.ParseSteps` reads bundled or
otherwise in-memory JSON Lines without creating a temporary file.

Artifact and replay reads fail closed when their fixed resource budgets are
exceeded. JSON artifacts are limited to 1 MiB, JSONL and compressed JSONL
artifacts to 64 MiB, and ONNX artifacts to 1 GiB. Downloads require the
contract's exact `size_bytes` and lowercase SHA-256 digest, then check them
against `Content-Length` when present and the bytes actually streamed. A
rejected or interrupted download removes its temporary `.part` file and leaves
an existing destination unchanged.

Replay manifests are limited to 1 MiB, 4,096 chunks, 1,024 characters per chunk
path, and 100,000 declared steps per chunk. Replay readers allow at most 256 MiB
after decompression, 1 MiB per UTF-8 JSONL row, and 100,000 returned steps. These
budgets are internal invariants rather than configurable public API.

Persist `SubmissionId`, `ScenarioId`, and the optional `CancelToken` if a job
must survive an Editor or application restart:

```csharp
EmbodiedLabJob restored = EmbodiedLabJob.Restore(
    endpoints,
    savedSubmissionId,
    savedScenarioId,
    savedCancelToken);
```

Persist the exact Scenario ID with the submission ID so restored result and
Replay identities remain verifiable. The cancellation token returned by the
server is a capability: store it as a secret and do not log it. Restoring
without it still permits monitoring and downloads, but `CanCancel` is false. A
C# `CancellationToken` only stops the local SDK operation. Call `CancelAsync`
to request cancellation of the cloud job.

## Development

The implementation is intentionally incremental. EmbodiedLab publishes the
versioned JSON Schemas, this repository generates and commits matching C# DTOs,
and handwritten Unity APIs are added only for current use cases. The public
surface is the generated contracts plus `EmbodiedLabEndpoints`,
`EmbodiedLabJob`, `ScenarioBundleJson`, and `EmbodiedLabReplay`; HTTP and
WebSocket transport types remain internal.

The contract generator requires Python 3 and the .NET 8 SDK. To regenerate the
DTOs from the committed schemas:

```bash
python3 Tools~/contract_schemas.py normalize \
  --output /tmp/embodiedlab-contracts.schema.json
dotnet run --project Tools~/ContractCodeGen/ContractCodeGen.csproj \
  --configuration Release -- \
  /tmp/embodiedlab-contracts.schema.json \
  Runtime/Contracts/EmbodiedLabContracts.g.cs
```

`Schemas~/upstream.json` records the exact EmbodiedLab commit and SHA-256 hash
of every synchronized schema. The CI workflow regenerates the DTOs, compiles
them, exercises the canonical JSON fixtures, and rejects drift.

The generated DTOs are the structural serialization contract rather than a
general JSON Schema validator. They retain Newtonsoft.Json wire-name, enum, and
discriminator metadata but intentionally omit `DataAnnotations`. Result JSON is
semantically validated by the transport before it is published, and Replay
manifest/row JSON is semantically validated by `EmbodiedLabReplay`. Scenario
JSON is structurally deserialized by `ScenarioBundleJson`; the EmbodiedLab server
owns complete Scenario validation when a submission is accepted. DTOs store only
declared fields unless the upstream schema explicitly enables additional
properties.

Run the local validation in both supported Unity baselines with:

```bash
python3 Tools~/run_unity_tests.py \
  --unity-version 2022.3 \
  --unity-editor <path-to-unity-2022.3.19f1>
python3 Tools~/run_unity_tests.py \
  --unity-version 6000.3 \
  --unity-editor <path-to-unity-6000.3.11f1>
```

Pass a real completed EmbodiedLab model and keep graphics enabled to exercise
the semantic camera and ONNX session in the Editor:

```bash
python3 Tools~/run_unity_tests.py \
  --unity-version <2022.3-or-6000.3> \
  --unity-editor <path-to-matching-unity-editor> \
  --policy <path-to-policy.onnx> \
  --with-graphics
```

Build and launch the Windows x64 player smoke test with the same real model:

```bash
python3 Tools~/run_unity_standalone_smoke.py \
  --unity-version <2022.3-or-6000.3> \
  --unity-editor <path-to-matching-unity-editor> \
  --policy <path-to-policy.onnx> \
  --output-directory <temporary-output-directory>
```

The runner stages the committed tutorial source from `Samples~/Quickstart` into
the disposable validation project, compiles it with Unity's real C# compiler,
runs the package Editor tests and canonical-world hierarchy tests, and removes it
after both successful and failed runs. Unity remains a local requirement; CI
continues to run the Python and .NET checks without a licensed Editor.

See [the product direction](docs/vision/product-direction.md) and
[the implementation roadmap](docs/implementation/sdk-roadmap.md) for the
current boundaries and progress.

## License

A repository license has not been selected yet. Treat the current source as
pre-release material until licensing is resolved.

The bundled ONNX Runtime dependency has its own upstream MIT license and
third-party notices under `Runtime/Plugins/ONNXRuntime/`.
