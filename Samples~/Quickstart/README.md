# EmbodiedLab Quickstart

This sample demonstrates the smallest supported fixed-environment cloud
training flow without depending on EnvForge.

## Run the sample

1. Import **Quickstart** from the EmbodiedLab Unity SDK package's **Samples**
   tab.
2. Open `Quickstart.unity`.
3. Enter your EmbodiedLab API base URL and result WebSocket base URL.
4. Enter Play Mode and select **Submit and Train**.
5. Watch the generated navigation world and the submission status update.
6. Select a saved record under **Local history (newest first)** to restore its
   scenario, refresh the result, and resume WebSocket monitoring when active.
7. Select **Cancel Cloud Job**, verify the read-only cloud target, and confirm
   the operation to stop an active remote job. Select **Download Model** after
   training completes. For a completed record, select **Download Replay**,
   then use **Play Replay** and **Stop Replay**. Select **Run Inference** to run
   the downloaded model and **Stop Inference** to release it and reset the robot.

The latest seven Quickstart activity messages appear from the Game view's
upper-left corner as a transparent overlay. Informational messages are light,
warnings are yellow, and errors are red; a small text shadow preserves
readability without adding an opaque background panel.

The model is saved to:

    <Application.persistentDataPath>/EmbodiedLabQuickstart/<submission-id>/policy.onnx

The replay manifest and selected chunk are saved under the same submission
directory. **Download Replay** downloads the manifest first, selects the latest
chunk whose phase is `eval` and policy mode is `deterministic`, then downloads
only that chunk and reads its steps. The selected manifest and chunk paths are
persisted in local history and loaded again when the completed record is
restored.

Playback applies authoritative replay X/Z position and yaw to the same robot
used by the visible world. The clock follows each step's `time_seconds` and
interpolates only between consecutive steps in the same episode. Episode
boundaries have a short pause. **Stop Replay**, history selection, world
rebuild, and leaving Play Mode stop playback; **Stop Replay** resets the robot
to the first loaded step.

## Run the downloaded policy

The inference flow is deliberately direct and sample-local:

1. **Download Model** stores the completed job's canonical `policy.onnx`.
2. **Run Inference** stops replay and resets the shared robot to the exact start
   pose in the submitted scenario.
3. One cached ONNX Runtime 1.24.4 CPU session loads that file.
4. Every 0.1 seconds, the robot's submitted `ForwardCameraSensor` renders a
   112x84 semantic image. Traversable space is green; blocked geometry and the
   background are blue; the robot and goal are hidden. The readback is flipped
   vertically and normalized into channel-first `obs_0` values.
5. `obs_1` contains signed relative goal angle in `[-180, 180]` degrees and
   straight-line goal distance in meters.
6. Output index 0 is clamped to forward `[0, 1]`, and index 1 is clamped to turn
   `[-1, 1]`. Any clamp remains visible as a contract violation.
7. Each decision applies at most 0.2 meters forward and 15 degrees of turn.

The model must expose exactly float `obs_0` (`3x84x112`, with an optional batch)
and float `obs_1` (two values, with an optional batch), plus a float output with
at least two actions. Incompatible metadata, malformed values, native runtime
failures, missing graphics, wall collision, and goal reach stop inference with
an explicit status. **Stop Inference**, history selection, world rebuild, and
leaving Play Mode dispose the session and camera resources and reset the robot
to the submitted start pose.

Replay and inference always use the same visible world and robot and cannot run
simultaneously. This package includes the CPU ONNX Runtime binaries needed for
Unity 6000.3 on Windows x64 Editor and Standalone. That is the only initially
verified target; no Sentis or model-format fallback is present.

`NavigationScenario.json` is both the exact fixed scenario submitted by this
sample and the source for its visible floor, walls, obstacles, robot start,
goal, forward semantic camera, overview camera, and light. Edit that contract
JSON to try another fixed map.

The sample stores resumable job records newest-first at:

    <Application.persistentDataPath>/EmbodiedLabQuickstart/job-history.json

Each record keeps the submitted scenario, endpoints, latest status and
progress, local artifact paths, and the cancellation capability while the job
is active. Treat this file as secret-bearing local data. Terminal records no
longer retain the cancellation capability.

**Remove Local Record** uses an explicit second confirmation. It removes only
the history entry; it never cancels or deletes the cloud job and never deletes
downloaded files. Removing an active record also permanently removes this
sample's saved cancellation capability, so the confirmation names that risk
and the exact cloud target.

World rendering and local history persistence are isolated from the active job
handle. If either local operation fails after submission, the sample keeps the
job attached so status monitoring and cloud cancellation remain available. A
complete temporary history file left by an interrupted atomic save is promoted
the next time the sample loads; a malformed temporary file is discarded.
Transient save failures remain dirty and retry while the sample stays open,
and a submission that completes during Play Mode shutdown gets one final
best-effort history save before its local handle is disposed.

Replace the `example.com` endpoints before submitting. Running training in your
cloud deployment may incur costs.

Closing the scene or leaving Play Mode cancels local monitoring only. It does
not cancel the cloud job; use **Cancel Cloud Job** for that operation.
