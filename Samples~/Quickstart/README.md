# EmbodiedLab Quickstart

This sample demonstrates the smallest supported fixed-environment cloud
training flow without depending on EnvForge.

## Run the sample

1. Import **Quickstart** from the EmbodiedLab Unity SDK package's **Samples**
   tab.
2. Open `Quickstart.unity`.
3. Enter your EmbodiedLab API base URL and result WebSocket base URL.
4. Enter Play Mode and select **Submit and Train**.
5. Watch the submission ID, status, and progress update.
6. Select **Cancel Cloud Job** to stop an active remote job, or select
   **Download Model** after training completes.

The model is saved to:

    <Application.persistentDataPath>/EmbodiedLabQuickstart/<submission-id>/policy.onnx

`NavigationScenario.json` is the fixed scenario submitted by this sample. Edit
that contract JSON to try another fixed map. The sample does not instantiate the
map in the local scene and does not run the downloaded policy.

Replace the `example.com` endpoints before submitting. Running training in your
cloud deployment may incur costs.

Closing the scene or leaving Play Mode cancels local monitoring only. It does
not cancel the cloud job; use **Cancel Cloud Job** for that operation.
