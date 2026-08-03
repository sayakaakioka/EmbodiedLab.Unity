#nullable enable

using System;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    public sealed partial class QuickstartController
    {
        private const int TitleFontSize = 44;
        private const int SectionFontSize = 36;
        private const int BodyFontSize = 30;
        private const float PanelLeft = 40f;
        private const float PanelTop = 40f;
        private const float PanelMaximumWidth = 1040f;
        private const float PanelBottomMargin = 40f;
        private const float FieldLabelWidth = 300f;
        private const float ButtonHeight = 60f;
        private const float TextFieldHeight = 56f;

        private Vector2 panelScrollPosition;
        private GUIStyle panelStyle = null!;
        private GUIStyle titleStyle = null!;
        private GUIStyle sectionStyle = null!;
        private GUIStyle bodyStyle = null!;
        private GUIStyle buttonStyle = null!;
        private GUIStyle textFieldStyle = null!;

        private void OnGUI()
        {
            EnsureGuiStyles();
            float panelWidth = Mathf.Min(PanelMaximumWidth, Screen.width - (PanelLeft * 2f));
            float panelHeight = Mathf.Max(
                360f,
                Screen.height - PanelTop - PanelBottomMargin);
            GUILayout.BeginArea(
                new Rect(PanelLeft, PanelTop, panelWidth, panelHeight),
                panelStyle);
            panelScrollPosition = GUILayout.BeginScrollView(panelScrollPosition);

            GUILayout.Label("EmbodiedLab Tutorial", titleStyle);
            GUILayout.Label(
                "Follow six small steps from a fixed scenario to replay and inference.",
                bodyStyle);
            DrawScenarioStep();
            DrawConnectionStep();
            DrawTrainingStep();
            DrawArtifactStep();
            DrawReplayStep();
            DrawInferenceStep();

            GUILayout.Space(20);
            GUILayout.Label(
                "Leaving Play Mode stops local monitoring only. It does not cancel the cloud job.",
                bodyStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawScenarioStep()
        {
            GUILayout.Space(16);
            GUILayout.Label("1. Load the fixed scenario", sectionStyle);
            DrawValue("Scenario", scenario?.ScenarioId ?? "Unavailable");
            DrawValue(
                "World",
                worldBuilder?.RobotTransform == null
                    ? "Unavailable"
                    : "Built from NavigationScenario.json");
        }

        private void DrawConnectionStep()
        {
            GUILayout.Space(16);
            GUILayout.Label("2. Connect", sectionStyle);
            DrawTextField("API base URL", ref apiBaseUrl);
            DrawTextField("Result WebSocket URL", ref resultWebSocketBaseUrl);
            if (UsesExampleEndpoint(apiBaseUrl) ||
                UsesExampleEndpoint(resultWebSocketBaseUrl))
            {
                GUILayout.Label(
                    "Replace both example endpoints with your EmbodiedLab deployment.",
                    bodyStyle);
            }
        }

        private void DrawTrainingStep()
        {
            GUILayout.Space(16);
            GUILayout.Label("3. Submit and monitor", sectionStyle);
            DrawButton("Submit and Train", CanSubmit(), StartSubmission);
            DrawValue("Submission ID", submissionIdText);
            DrawValue("Job status", jobStatusText);
            DrawValue("Progress", progressText);
            DrawValue("Activity", activityText);
            DrawCloudCancellation();
        }

        private void DrawArtifactStep()
        {
            GUILayout.Space(16);
            GUILayout.Label("4. Download the result", sectionStyle);
            DrawButton("Download Model", CanDownloadArtifacts(), StartModelDownload);
            DrawButton("Download Replay", CanDownloadArtifacts(), StartReplayDownload);
            DrawValue("Model", artifacts?.ModelPath ?? "-");
            DrawValue("Replay", artifacts?.ReplayPath ?? "-");
        }

        private void DrawReplayStep()
        {
            GUILayout.Space(16);
            GUILayout.Label("5. Play the replay", sectionStyle);
            DrawButton("Play Replay", CanPlayReplay(), StartReplayPlayback);
            if (replayPlayer?.IsPlaying == true)
            {
                DrawButton("Stop Replay", true, StopReplayPlayback);
            }

            DrawValue("Replay status", replayPlayer?.Status ?? "Unavailable");
        }

        private void DrawInferenceStep()
        {
            GUILayout.Space(16);
            GUILayout.Label("6. Run the policy (Windows x64)", sectionStyle);
            DrawButton("Run Inference", CanRunInference(), StartInference);
            if (inferenceRunner?.IsRunning == true)
            {
                DrawButton("Stop Inference", true, StopInference);
            }

            DrawValue("Inference status", inferenceRunner?.Status ?? "Unavailable");
        }

        private void DrawCloudCancellation()
        {
            if (!cloudCancellationArmed)
            {
                DrawButton("Cancel Cloud Job", CanCancel(), ArmCloudCancellation);
                return;
            }

            GUILayout.Label(
                $"This stops cloud training for submission {submissionIdText}.",
                bodyStyle);
            DrawButton(
                "Confirm: Cancel Cloud Job",
                CanCancel(),
                StartCloudCancellation);
            DrawButton("Keep Cloud Job Running", true, DisarmCloudCancellation);
        }

        private void EnsureGuiStyles()
        {
            if (panelStyle != null)
            {
                return;
            }

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(28, 28, 24, 24),
            };
            titleStyle = CreateLabelStyle(TitleFontSize, FontStyle.Bold);
            sectionStyle = CreateLabelStyle(SectionFontSize, FontStyle.Bold);
            bodyStyle = CreateLabelStyle(BodyFontSize, FontStyle.Normal);
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = BodyFontSize,
                wordWrap = true,
            };
            textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = BodyFontSize,
            };
        }

        private static GUIStyle CreateLabelStyle(int fontSize, FontStyle fontStyle)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                wordWrap = true,
            };
        }

        private void DrawTextField(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, bodyStyle, GUILayout.Width(FieldLabelWidth));
            value = GUILayout.TextField(
                value,
                textFieldStyle,
                GUILayout.Height(TextFieldHeight));
            GUILayout.EndHorizontal();
        }

        private void DrawButton(string label, bool enabled, Action action)
        {
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && enabled;
            if (GUILayout.Button(label, buttonStyle, GUILayout.Height(ButtonHeight)))
            {
                action();
            }

            GUI.enabled = previousEnabled;
        }

        private void DrawValue(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, bodyStyle, GUILayout.Width(FieldLabelWidth));
            GUILayout.Label(value, bodyStyle);
            GUILayout.EndHorizontal();
        }
    }
}
