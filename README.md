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

Reusable Editor UI, application-level job history, and EnvForge-specific scene
authoring remain in [EnvForge](https://github.com/sayakaakioka/EnvForge). The
Quickstart includes sample-local history only to teach restore, monitoring, and
replay artifact reuse.
Server behavior and the source contract models remain in
[EmbodiedLab](https://github.com/sayakaakioka/EmbodiedLab).

## Requirements

- Unity 6000.3 or later
- Git 2.14 or later when installing from a Git URL

## Installation

Until versioned releases are available, add the repository from Unity Package
Manager using this Git URL:

```text
https://github.com/sayakaakioka/EmbodiedLab.Unity.git
```

The package identifier is `com.embodiedlab.unity`.

## Import the Quickstart sample

In Package Manager, select **EmbodiedLab Unity SDK**, open the **Samples** tab,
and import **Quickstart**. Then open
`Assets/Samples/EmbodiedLab Unity SDK/0.1.0/Quickstart/Quickstart.unity`
and enter the API and result WebSocket base URLs in Play Mode.

The sample builds a visible navigation world from the exact included scenario,
submits it, displays WebSocket result updates, requests cloud cancellation, and
downloads a completed ONNX model under `Application.persistentDataPath`. Its
sample-local history restores prior jobs and resumes monitoring across restarts.
For a completed record, **Download Replay** retrieves the manifest and only its
latest deterministic evaluation chunk. **Play Replay** drives the same visible
robot from replay time, and **Stop Replay** resets it to the first loaded step.
The sample intentionally does not include EnvForge's scene authoring, reusable
history UI, or model inference.

## Quick start

Create the deployment endpoints once, build a contract `ScenarioBundle`, and
submit it through the stateful job handle:

```csharp
using System.IO;
using System.Threading;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity;
using UnityEngine;

public async Awaitable RunTrainingAsync(
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

`SubmitAsync` creates the submission and starts training. Result monitoring uses
the WebSocket stream while it is healthy; HTTP result reads are reserved for
explicit `RefreshAsync` calls and recovery after a failed, disconnected, or
silent stream. `ResultUpdated` is dispatched through the synchronization
context captured when the job handle is created, which is normally Unity's main
thread context.

The replay-bundle artifact currently points to its manifest, so
`DownloadReplayBundleAsync` saves that manifest. `DownloadModelAsync` selects
`onnx_model` first, then the Unity Sentis model, then the generic model artifact.
Result artifacts exist only at `job.Result?.ResultBundle?.Artifacts`; the SDK does
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
ReplayBundleChunk selectedChunk = manifest.Chunks.First();
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
artifacts to 64 MiB, and ONNX or ZIP artifacts to 1 GiB. Downloads check both
`Content-Length` and the bytes actually streamed. A rejected or interrupted
download removes its temporary `.part` file and leaves an existing destination
unchanged.

Replay manifests are limited to 1 MiB, 4,096 chunks, 1,024 characters per chunk
path, and 100,000 declared steps per chunk. Replay readers allow at most 256 MiB
after decompression, 1 MiB per UTF-8 JSONL row, and 100,000 returned steps. These
budgets are internal invariants rather than configurable public API.

Persist both `SubmissionId` and `CancelToken` if a job must survive an Editor or
application restart:

```csharp
EmbodiedLabJob restored = EmbodiedLabJob.Restore(
    endpoints,
    savedSubmissionId,
    savedCancelToken);
```

The cancellation token returned by the server is a capability: store it as a
secret and do not log it. Restoring without it still permits monitoring and
downloads, but `CanCancel` is false. A C# `CancellationToken` only stops the
local SDK operation. Call `CancelAsync` to request cancellation of the cloud
job.

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

The generated DTOs are a serialization contract, not a client-side validation
layer. They retain Newtonsoft.Json wire-name, enum, and discriminator metadata,
but intentionally omit `DataAnnotations`. DTOs store only declared fields unless
the upstream schema explicitly enables additional properties.

See [the product direction](docs/vision/product-direction.md) and
[the implementation roadmap](docs/implementation/sdk-roadmap.md) for the
current boundaries and progress.

## License

A repository license has not been selected yet. Treat the current source as
pre-release material until licensing is resolved.
