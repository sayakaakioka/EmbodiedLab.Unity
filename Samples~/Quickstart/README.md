# EmbodiedLab Tutorial

This tutorial builds one fixed-environment EmbodiedLab workflow in six small
steps. Each step points to the file that owns that responsibility. The final
scene can submit and monitor a cloud job, download its artifacts, play its
deterministic evaluation replay, and run its ONNX policy on Windows x64.

The tutorial does not implement a general job history, credential store, or
editor workflow. Those application concerns belong in a frontend such as
EnvForge.

## Import and open the tutorial

1. Import **Tutorial** from the EmbodiedLab Unity SDK package's **Samples** tab.
2. Open `Quickstart.unity`.
3. Enter Play Mode.

The scene contains one `QuickstartController`. Its presentation is in
`QuickstartController.View.cs`; its lifecycle composition is in
`QuickstartController.cs`. Read the following sections in order before reading
those two files end to end.

## 1. Load the fixed scenario

Start with `NavigationScenario.json`. It is the exact `ScenarioBundle` sent to
EmbodiedLab and the source for the visible floor, walls, obstacles, robot,
goal, semantic camera, overview camera, and light.

`QuickstartWorldBuilder.cs` shows how a frontend can turn that contract into a
Unity world. It does not define a second environment format.

The scene loads the contract with the public SDK helper:

```csharp
ScenarioBundle scenario = ScenarioBundleJson.Deserialize(scenarioJson.text);
```

The robot radius and goal radius are both 0.45 meters. The training contract,
goal display, and local inference therefore use the same reach condition.

## 2. Connect to EmbodiedLab

Replace both example endpoints in the Game view:

- **API base URL**: the HTTPS base URL for the EmbodiedLab API.
- **Result WebSocket URL**: the WSS base URL for result updates.

`EmbodiedLabEndpoints` validates and normalizes these values. Non-loopback
deployments must use HTTPS and WSS.

```csharp
var endpoints = new EmbodiedLabEndpoints(apiBaseUrl, resultWebSocketBaseUrl);
```

## 3. Submit and monitor a job

Select **Submit and Train**. `QuickstartCloudJob.cs` contains only this cloud
job lifecycle:

```csharp
EmbodiedLabJob job = await EmbodiedLabJob.SubmitAsync(
    endpoints,
    scenario,
    cancellationToken);

job.ResultUpdated += HandleResultUpdated;
ResultDocument completed = await job.WaitForCompletionAsync(cancellationToken);
```

The SDK uses WebSocket updates while the stream is healthy and performs HTTP
result reconciliation only after connection failure, disconnect, silence, or
an explicit refresh.

While a trainer is starting, a valid queued result can contain
`current_step = 0` and `total_steps = 0`. The tutorial displays this as
**Waiting for the trainer to start** instead of the ambiguous `0/0`. Numeric
progress appears after a total step count is available. The formatting rule is
isolated in `QuickstartProgressText.cs`.

### Optional: cancel the cloud job

**Cancel Cloud Job** requires a second confirmation. It calls `CancelAsync` on
the active job. A .NET `CancellationToken` stops only the local wait; it never
cancels cloud training.

```csharp
ResultDocument cancelling = await job.CancelAsync(cancellationToken);
```

### Optional: restore in an application

The SDK can restore a known job, but this tutorial deliberately does not store
credentials or build a history UI:

```csharp
EmbodiedLabJob restored = EmbodiedLabJob.Restore(
    endpoints,
    submissionId,
    scenarioId,
    cancelToken);
```

Store the submission ID, endpoint values, exact scenario, and optional cancel
capability according to the security and persistence requirements of the
application that embeds the SDK.

## 4. Download the result

After the job reaches `Completed`, use **Download Model** and **Download
Replay**. `QuickstartArtifacts.cs` keeps artifact selection and download
separate from the cloud lifecycle and the UI.

The model call downloads only the canonical ONNX artifact:

```csharp
await job.DownloadModelAsync(modelPath, cancellationToken);
```

The replay flow is deliberately explicit:

1. Refresh the completed result.
2. Download the Replay manifest.
3. Select the latest `eval` + `deterministic` chunk.
4. Download that chunk.
5. Read and validate its steps.

```csharp
await job.DownloadReplayBundleAsync(manifestPath, cancellationToken);
ReplayBundleManifest manifest = EmbodiedLabReplay.ReadManifest(manifestPath);
await job.DownloadReplayChunkAsync(chunk, chunkPath, cancellationToken);
IReadOnlyList<ReplayLogStep> steps = EmbodiedLabReplay.ReadSteps(chunkPath);
```

Artifacts are stored under:

    <Application.persistentDataPath>/EmbodiedLabQuickstart/<submission-id>/

Local paths reject rooted, traversing, or otherwise unsafe submission and
chunk paths. The SDK also enforces fixed artifact and replay resource limits.

## 5. Play the replay

Select **Play Replay**. `QuickstartReplayTimeline.cs` validates and advances
the replay clock, while `QuickstartReplayPlayer.cs` applies the resulting X/Z
position and yaw to the same visible robot.

Playback follows each step's `time_seconds`, interpolates only consecutive
steps in the same episode, pauses briefly at episode boundaries, and resets to
the first step when stopped. Step 0 is the episode reset state with zero action,
zero reward, and no event; the state after the first applied action is step 1.

## 6. Run the policy on Windows x64

Select **Run Inference** after downloading the model. The package includes ONNX
Runtime 1.24.4 CPU binaries verified with Unity 2022.3.19f1 and 6000.3.11f1 on
Windows x64 Editor and Standalone. Other operating systems remain unsupported.

Read the inference files in this order:

1. `QuickstartOnnxContract.cs` resolves observation names, shapes, and action
   order from the Scenario and downloaded model metadata, then checks the ONNX
   session against them.
2. `QuickstartSemanticCamera.cs` captures the submitted semantic camera.
3. `QuickstartInferenceMath.cs` creates observations and clamps actions.
4. `QuickstartOnnxPolicy.cs` owns one cached ONNX session.
5. `QuickstartInferenceRunner.cs` applies decisions to the shared robot.

Replay and inference are mutually exclusive. Starting one stops the other, and
stopping either resets the robot deterministically.

## What to read next

After completing the tutorial, continue with the SDK rather than adding more
sample-local infrastructure:

1. `Runtime/EmbodiedLabJob.cs`
2. `Runtime/EmbodiedLabEndpoints.cs`
3. `Runtime/EmbodiedLabReplay.cs`
4. `Runtime/Transport/EmbodiedLabTransport.cs`

Running training against a cloud deployment may incur costs. Leaving Play Mode
stops this tutorial's local monitoring and releases local resources; it does
not cancel the cloud job.
