#nullable enable

using System;
using System.Globalization;
using System.IO;
using EmbodiedLab.Contracts;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart.StandaloneSmoke
{
    public sealed class QuickstartStandaloneSmoke : MonoBehaviour
    {
        private const int MaximumFrames = 600;

        private QuickstartWorldBuilder? world;
        private QuickstartInferenceRunner? runner;
        private string resultPath = string.Empty;
        private int frameCount;
        private bool finished;

        private void Start()
        {
            try
            {
                resultPath = RequireArgument("--embodiedlab-smoke-result");
                string scenarioPath = RequireArgument("--embodiedlab-scenario");
                string modelPath = RequireArgument("--embodiedlab-policy");
                ScenarioBundle scenario = ScenarioBundleJson.Deserialize(
                    File.ReadAllText(scenarioPath));
                world = new QuickstartWorldBuilder();
                world.Build(scenario);
                runner = new QuickstartInferenceRunner(world);
                runner.Start(modelPath);
                if (!runner.IsRunning)
                {
                    Finish(false, runner.Status);
                }
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private void Update()
        {
            if (finished || runner == null || world == null)
            {
                return;
            }

            try
            {
                frameCount++;
                runner.Tick(QuickstartInferenceRunner.DecisionSeconds);
                if (runner.ObservationStatus.StartsWith(
                    "angle=",
                    StringComparison.Ordinal))
                {
                    string observation = runner.ObservationStatus;
                    string action = runner.ActionStatus;
                    runner.Stop();
                    Transform robot = world.RobotTransform ??
                        throw new InvalidOperationException("Robot disappeared during smoke test.");
                    if (Vector3.Distance(robot.position, world.RobotStartPosition) > 0.0001f ||
                        Quaternion.Angle(robot.rotation, world.RobotStartRotation) > 0.0001f)
                    {
                        throw new InvalidOperationException(
                            "Stopping inference did not reset the shared robot.");
                    }

                    Finish(
                        true,
                        $"Unity={Application.unityVersion}; ONNX Runtime=1.24.4; " +
                        $"{observation}; {action}");
                    return;
                }

                if (!runner.IsRunning)
                {
                    Finish(false, runner.Status);
                }
                else if (frameCount >= MaximumFrames)
                {
                    Finish(false, "Timed out before the first inference decision.");
                }
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private void OnDestroy()
        {
            runner?.Dispose();
            runner = null;
            world?.Dispose();
            world = null;
        }

        private void Finish(bool passed, string details)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(resultPath))
                {
                    string? directory = Path.GetDirectoryName(resultPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(
                        resultPath,
                        $"{(passed ? "PASS" : "FAIL")}\n{details}\n",
                        System.Text.Encoding.UTF8);
                }
            }
            finally
            {
                Application.Quit(passed ? 0 : 1);
            }
        }

        private static string RequireArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.Ordinal))
                {
                    return arguments[index + 1];
                }
            }

            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Required command-line argument '{0}' is missing.",
                    name));
        }
    }
}
