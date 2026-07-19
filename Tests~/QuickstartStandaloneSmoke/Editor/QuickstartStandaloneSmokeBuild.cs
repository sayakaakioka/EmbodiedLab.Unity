#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart.StandaloneSmoke.Editor
{
    public static class QuickstartStandaloneSmokeBuild
    {
        private const string ScenePath =
            "Assets/EmbodiedLabQuickstartStandaloneValidation/Smoke/QuickstartStandaloneSmoke.unity";

        public static void Build()
        {
            string outputPath = RequireArgument("--embodiedlab-build-path");
            string? directory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("Standalone build directory is invalid.");
            }

            Directory.CreateDirectory(directory);
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            var smokeObject = new GameObject("EmbodiedLab Quickstart Standalone Smoke");
            smokeObject.AddComponent<QuickstartStandaloneSmoke>();
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException("Standalone smoke scene could not be saved.");
            }

            BuildReport report = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = outputPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development,
                });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Standalone build failed: {report.summary.result}; " +
                    $"errors={report.summary.totalErrors}.");
            }

            Debug.Log(
                $"EmbodiedLab Quickstart standalone smoke build succeeded: {outputPath}");
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
                $"Required command-line argument '{name}' is missing.");
        }
    }
}
