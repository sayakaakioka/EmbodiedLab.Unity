#nullable enable

using System;
using System.IO;
using EmbodiedLab.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart.Imported.Tests
{
    public sealed class QuickstartWorldBuilderTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void CanonicalScenarioBuildsExpectedWorld()
        {
            ScenarioBundle scenario = LoadScenario();
            using var builder = new QuickstartWorldBuilder();

            builder.Build(scenario);

            GameObject root = GameObject.Find("Canonical Navigation World") ??
                throw new AssertionException("Canonical world root was not created.");
            WorldSpec world = scenario.World;
            Bounds2D bounds = world.Bounds;
            float minX = Convert.ToSingle(bounds.Min.X);
            float maxX = Convert.ToSingle(bounds.Max.X);
            float minZ = Convert.ToSingle(bounds.Min.Z);
            float maxZ = Convert.ToSingle(bounds.Max.Z);
            float width = maxX - minX;
            float depth = maxZ - minZ;

            Assert.That(
                root.transform.childCount,
                Is.EqualTo(world.StaticWalls.Count + world.StaticObstacles.Count + 5));
            AssertTransform(
                root,
                "Floor",
                new Vector3((minX + maxX) * 0.5f, -0.05f, (minZ + maxZ) * 0.5f),
                new Vector3(width, 0.1f, depth),
                0f);

            foreach (StaticWall wall in world.StaticWalls)
            {
                float height = Convert.ToSingle(wall.Height);
                AssertTransform(
                    root,
                    $"Wall {wall.Id}",
                    new Vector3(
                        Convert.ToSingle(wall.Center.X),
                        height * 0.5f,
                        Convert.ToSingle(wall.Center.Z)),
                    new Vector3(
                        Convert.ToSingle(wall.Size.X),
                        height,
                        Convert.ToSingle(wall.Size.Z)),
                    Convert.ToSingle(wall.RotationYDegrees));
            }

            foreach (StaticObstacle obstacle in world.StaticObstacles)
            {
                float height = Convert.ToSingle(obstacle.Height);
                AssertTransform(
                    root,
                    $"Obstacle {obstacle.Id}",
                    new Vector3(
                        Convert.ToSingle(obstacle.Center.X),
                        height * 0.5f,
                        Convert.ToSingle(obstacle.Center.Z)),
                    new Vector3(
                        Convert.ToSingle(obstacle.Size.X),
                        height,
                        Convert.ToSingle(obstacle.Size.Z)),
                    Convert.ToSingle(obstacle.RotationYDegrees));
            }

            float robotRadius = Convert.ToSingle(scenario.Robot.Radius);
            AssertTransform(
                root,
                "Robot",
                new Vector3(
                    Convert.ToSingle(scenario.Robot.StartPose.Position.X),
                    0.5f,
                    Convert.ToSingle(scenario.Robot.StartPose.Position.Z)),
                new Vector3(robotRadius * 2f, 0.5f, robotRadius * 2f),
                Convert.ToSingle(scenario.Robot.StartPose.RotationYDegrees));

            float goalRadius = Convert.ToSingle(world.Goal.Radius);
            AssertTransform(
                root,
                "Goal",
                new Vector3(
                    Convert.ToSingle(world.Goal.Position.X),
                    0.05f,
                    Convert.ToSingle(world.Goal.Position.Z)),
                new Vector3(goalRadius * 2f, 0.05f, goalRadius * 2f),
                0f);

            Transform cameraTransform = FindRequired(root, "Overview Camera");
            AssertVector(
                new Vector3(
                    (minX + maxX) * 0.5f,
                    Math.Max(width, depth),
                    (minZ + maxZ) * 0.5f),
                cameraTransform.position,
                "Overview camera position");
            AssertRotation(Quaternion.Euler(90f, 0f, 0f), cameraTransform.rotation);
            Camera camera = cameraTransform.GetComponent<Camera>();
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera.orthographic, Is.True);
            Assert.That(
                camera.orthographicSize,
                Is.EqualTo(Math.Max(width, depth) * 0.62f).Within(Tolerance));

            Transform lightTransform = FindRequired(root, "Tutorial Light");
            Light light = lightTransform.GetComponent<Light>();
            Assert.That(light, Is.Not.Null);
            Assert.That(light.type, Is.EqualTo(LightType.Directional));
            Assert.That(light.intensity, Is.EqualTo(1.15f).Within(Tolerance));
            AssertRotation(Quaternion.Euler(50f, -30f, 0f), lightTransform.rotation);

            Transform forwardCameraTransform = FindRequired(
                root,
                "Robot/Forward Semantic Camera");
            Camera forwardCamera = forwardCameraTransform.GetComponent<Camera>();
            Assert.That(forwardCamera, Is.Not.Null);
            Assert.That(forwardCamera.enabled, Is.False);
            Assert.That(forwardCamera.fieldOfView, Is.EqualTo(70f).Within(Tolerance));
            Assert.That(forwardCamera.nearClipPlane, Is.EqualTo(0.05f).Within(Tolerance));
            Assert.That(forwardCamera.farClipPlane, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(
                forwardCameraTransform.position.y,
                Is.EqualTo(0.6f).Within(Tolerance));
            Assert.That(FindRequired(root, "Robot").gameObject.layer, Is.EqualTo(2));
            Assert.That(FindRequired(root, "Goal").gameObject.layer, Is.EqualTo(2));
            Assert.That(
                FindRequired(root, "Floor").gameObject.layer,
                Is.EqualTo(QuickstartWorldBuilder.TraversableLayer));
            Assert.That(
                FindRequired(root, "Wall wall_north").gameObject.layer,
                Is.EqualTo(QuickstartWorldBuilder.BlockedLayer));

            builder.Dispose();
            Assert.That(GameObject.Find("Canonical Navigation World"), Is.Null);
        }

        [Test]
        public void DisposeRemovesGeneratedWorld()
        {
            var builder = new QuickstartWorldBuilder();
            builder.Build(LoadScenario());
            Assert.That(GameObject.Find("Canonical Navigation World"), Is.Not.Null);

            builder.Dispose();

            Assert.That(GameObject.Find("Canonical Navigation World"), Is.Null);
        }

        [Test]
        public void InferenceStopAndDisposeResetSharedRobot()
        {
            using var builder = new QuickstartWorldBuilder();
            builder.Build(LoadScenario());
            Transform robot = builder.RobotTransform ??
                throw new AssertionException("Quickstart robot was not created.");
            using var runner = new QuickstartInferenceRunner(builder);

            robot.position = new Vector3(2f, 0.5f, 3f);
            robot.rotation = Quaternion.Euler(0f, 120f, 0f);
            runner.Stop();
            AssertVector(builder.RobotStartPosition, robot.position, "stop reset position");
            AssertRotation(builder.RobotStartRotation, robot.rotation);

            robot.position = new Vector3(-1f, 0.5f, 1f);
            runner.Dispose();
            AssertVector(builder.RobotStartPosition, robot.position, "dispose reset position");
            AssertRotation(builder.RobotStartRotation, robot.rotation);
            Assert.That(runner.IsRunning, Is.False);
        }

        [Test]
        public void StagedRealPolicyLoadsRunsAndDisposesWhenProvided()
        {
            string path = Path.Combine(
                Application.dataPath,
                "EmbodiedLabQuickstartValidation",
                "Fixtures",
                "policy.onnx");
            if (!File.Exists(path))
            {
                Assert.Pass("No external real-policy fixture was staged for this run.");
                return;
            }

            var policy = new QuickstartOnnxPolicy(path);
            QuickstartRawAction action = policy.Run(
                new float[QuickstartOnnxContract.ImageValueCount],
                new float[QuickstartOnnxContract.NumericValueCount]);
            Assert.That(float.IsNaN(action.Forward), Is.False);
            Assert.That(float.IsInfinity(action.Forward), Is.False);
            Assert.That(float.IsNaN(action.Turn), Is.False);
            Assert.That(float.IsInfinity(action.Turn), Is.False);

            policy.Dispose();
            Assert.Throws<ObjectDisposedException>(
                () => policy.Run(
                    new float[QuickstartOnnxContract.ImageValueCount],
                    new float[QuickstartOnnxContract.NumericValueCount]));
        }

        [Test]
        public void StagedRealPolicyRunsSemanticInferenceWhenProvided()
        {
            string path = Path.Combine(
                Application.dataPath,
                "EmbodiedLabQuickstartValidation",
                "Fixtures",
                "policy.onnx");
            if (!File.Exists(path))
            {
                Assert.Pass("No external real-policy fixture was staged for this run.");
                return;
            }

            using var builder = new QuickstartWorldBuilder();
            builder.Build(LoadScenario());
            using var runner = new QuickstartInferenceRunner(builder);
            var replay = new QuickstartReplayPlayer();
            replay.Load(
                builder.RobotTransform ??
                    throw new AssertionException("Quickstart robot was not created."),
                new[]
                {
                    CreateReplayStep(0, 0d, -6d, -4d, 45d),
                    CreateReplayStep(1, 0.1d, -5d, -3d, 60d),
                },
                "eval/real-policy-transition.jsonl.gz");
            var modes = new QuickstartModeCoordinator(replay.Stop, runner.Stop);
            modes.EnterReplay();
            replay.Play();
            replay.Tick(0.05d);
            Assert.That(replay.IsPlaying, Is.True);
            modes.EnterInference();
            Assert.That(replay.IsPlaying, Is.False);

            runner.Start(path);
            Assert.That(runner.IsRunning, Is.True, runner.Status);

            runner.Tick(QuickstartInferenceRunner.DecisionSeconds);
            Assert.That(
                runner.ObservationStatus,
                Does.StartWith("angle="),
                runner.Status);
            Assert.That(runner.ActionStatus, Does.StartWith("raw f="), runner.Status);

            runner.Stop();
            Assert.That(runner.IsRunning, Is.False);
            AssertVector(
                builder.RobotStartPosition,
                builder.RobotTransform!.position,
                "real inference stop reset");
        }

        private static ReplayLogStep CreateReplayStep(
            int step,
            double time,
            double x,
            double z,
            double yaw)
        {
            return new ReplayLogStep
            {
                JobId = "real-policy-smoke",
                ScenarioId = "navigation_default",
                EpisodeId = "episode-1",
                StepIndex = step,
                TimeSeconds = time,
                Phase = "eval",
                PolicyMode = "deterministic",
                Robot = new ReplayRobotState
                {
                    Position = new ReplayPosition { X = x, Z = z },
                    RotationYDegrees = yaw,
                },
            };
        }

        private static ScenarioBundle LoadScenario()
        {
            string path = Path.Combine(
                Application.dataPath,
                "EmbodiedLabQuickstartValidation",
                "Quickstart",
                "NavigationScenario.json");
            return ScenarioBundleJson.Deserialize(File.ReadAllText(path));
        }

        private static void AssertTransform(
            GameObject root,
            string name,
            Vector3 expectedPosition,
            Vector3 expectedScale,
            float expectedRotationY)
        {
            Transform actual = FindRequired(root, name);
            AssertVector(expectedPosition, actual.position, $"{name} position");
            AssertVector(expectedScale, actual.localScale, $"{name} scale");
            AssertRotation(
                Quaternion.Euler(0f, expectedRotationY, 0f),
                actual.rotation);
        }

        private static Transform FindRequired(GameObject root, string name)
        {
            return root.transform.Find(name) ??
                throw new AssertionException($"Expected world object '{name}' was not created.");
        }

        private static void AssertVector(
            Vector3 expected,
            Vector3 actual,
            string label)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Tolerance), $"{label} x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Tolerance), $"{label} y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Tolerance), $"{label} z");
        }

        private static void AssertRotation(Quaternion expected, Quaternion actual)
        {
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThan(Tolerance));
        }
    }
}
