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
   then use **Play Replay** and **Stop Replay**.

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

`NavigationScenario.json` is both the exact fixed scenario submitted by this
sample and the source for its visible floor, walls, obstacles, robot start,
goal, overview camera, and light. Edit that contract JSON to try another fixed
map. The sample does not run the downloaded policy.

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
