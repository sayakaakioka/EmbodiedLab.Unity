#nullable enable

using System;
using System.Collections.Generic;
using EmbodiedLab.Contracts;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal sealed class QuickstartWorldBuilder : IDisposable
    {
        internal const int TraversableLayer = 8;
        internal const int BlockedLayer = 9;
        private const int HiddenFromSemanticCameraLayer = 2;

        private static readonly Color FloorColor = Color.green;
        private static readonly Color WallColor = Color.blue;
        private static readonly Color ObstacleColor = Color.blue;
        private static readonly Color RobotColor = new(0.85f, 0.14f, 0.62f);
        private static readonly Color GoalColor = new(0.95f, 0.72f, 0.12f);

        private readonly List<Material> materials = new();
        private GameObject? root;

        internal ScenarioBundle? Scenario { get; private set; }

        internal Transform? RobotTransform { get; private set; }

        internal Transform? GoalTransform { get; private set; }

        internal Camera? ForwardCamera { get; private set; }

        internal Vector3 RobotStartPosition { get; private set; }

        internal Quaternion RobotStartRotation { get; private set; }

        internal float GoalRadius { get; private set; }

        internal void Build(ScenarioBundle scenario)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            Clear();
            WorldSpec world = scenario.World ??
                throw new InvalidOperationException("Scenario world is missing.");
            Bounds2D bounds = world.Bounds ??
                throw new InvalidOperationException("Scenario world bounds are missing.");
            float minX = ToFloat(bounds.Min.X);
            float maxX = ToFloat(bounds.Max.X);
            float minZ = ToFloat(bounds.Min.Z);
            float maxZ = ToFloat(bounds.Max.Z);
            float width = maxX - minX;
            float depth = maxZ - minZ;
            if (width <= 0f || depth <= 0f)
            {
                throw new InvalidOperationException("Scenario world bounds must have positive size.");
            }

            root = new GameObject("Canonical Navigation World");
            CreateBox(
                "Floor",
                new Vector3((minX + maxX) * 0.5f, -0.05f, (minZ + maxZ) * 0.5f),
                new Vector3(width, 0.1f, depth),
                0f,
                FloorColor,
                TraversableLayer);

            foreach (StaticWall wall in world.StaticWalls)
            {
                float height = ToFloat(wall.Height);
                CreateBox(
                    $"Wall {wall.Id}",
                    new Vector3(
                        ToFloat(wall.Center.X),
                        height * 0.5f,
                        ToFloat(wall.Center.Z)),
                    new Vector3(ToFloat(wall.Size.X), height, ToFloat(wall.Size.Z)),
                    ToFloat(wall.RotationYDegrees),
                    WallColor,
                    BlockedLayer);
            }

            foreach (StaticObstacle obstacle in world.StaticObstacles)
            {
                if (obstacle.Shape != StaticObstacleShape.Box)
                {
                    throw new InvalidOperationException(
                        $"Quickstart cannot display obstacle shape '{obstacle.Shape}'.");
                }

                float height = ToFloat(obstacle.Height);
                CreateBox(
                    $"Obstacle {obstacle.Id}",
                    new Vector3(
                        ToFloat(obstacle.Center.X),
                        height * 0.5f,
                        ToFloat(obstacle.Center.Z)),
                    new Vector3(
                        ToFloat(obstacle.Size.X),
                        height,
                        ToFloat(obstacle.Size.Z)),
                    ToFloat(obstacle.RotationYDegrees),
                    ObstacleColor,
                    BlockedLayer);
            }

            CreateRobot(scenario.Robot);
            CreateGoal(world.Goal);
            CreateForwardCamera(FindForwardCameraSensor(scenario));
            CreateOverviewCamera(width, depth, minX, maxX, minZ, maxZ);
            CreateLighting();
            Scenario = scenario;
        }

        public void Dispose()
        {
            Clear();
        }

        private static float ToFloat(double value)
        {
            return Convert.ToSingle(value);
        }

        private void CreateBox(
            string name,
            Vector3 position,
            Vector3 scale,
            float rotationYDegrees,
            Color color,
            int layer)
        {
            GameObject box = CreatePrimitive(PrimitiveType.Cube, name, color);
            box.layer = layer;
            box.transform.position = position;
            box.transform.localScale = scale;
            box.transform.rotation = Quaternion.Euler(0f, rotationYDegrees, 0f);
        }

        private void CreateRobot(RobotSpec robot)
        {
            if (robot == null || robot.StartPose == null)
            {
                throw new InvalidOperationException("Scenario robot start pose is missing.");
            }

            float radius = ToFloat(robot.Radius);
            GameObject visual = CreatePrimitive(PrimitiveType.Capsule, "Robot", RobotColor);
            visual.layer = HiddenFromSemanticCameraLayer;
            RobotTransform = visual.transform;
            RobotStartPosition = new Vector3(
                ToFloat(robot.StartPose.Position.X),
                0.5f,
                ToFloat(robot.StartPose.Position.Z));
            RobotStartRotation = Quaternion.Euler(
                0f,
                ToFloat(robot.StartPose.RotationYDegrees),
                0f);
            visual.transform.position = RobotStartPosition;
            visual.transform.localScale = new Vector3(radius * 2f, 0.5f, radius * 2f);
            visual.transform.rotation = RobotStartRotation;
        }

        private void CreateGoal(GoalSpec goal)
        {
            if (goal == null)
            {
                throw new InvalidOperationException("Scenario goal is missing.");
            }

            float radius = ToFloat(goal.Radius);
            GameObject visual = CreatePrimitive(PrimitiveType.Cylinder, "Goal", GoalColor);
            visual.layer = HiddenFromSemanticCameraLayer;
            GoalTransform = visual.transform;
            GoalRadius = radius;
            visual.transform.position = new Vector3(
                ToFloat(goal.Position.X),
                0.05f,
                ToFloat(goal.Position.Z));
            visual.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);
        }

        private void CreateForwardCamera(ForwardCameraSensor sensor)
        {
            if (sensor.Width != QuickstartOnnxContract.ImageWidth ||
                sensor.Height != QuickstartOnnxContract.ImageHeight)
            {
                throw new InvalidOperationException(
                    "Quickstart ONNX inference requires a 112x84 forward camera sensor.");
            }

            if (sensor.SemanticMode != SemanticMode.TraversableVsBlocked)
            {
                throw new InvalidOperationException(
                    "Quickstart ONNX inference requires traversable_vs_blocked semantics.");
            }

            Transform robot = RobotTransform ??
                throw new InvalidOperationException("Quickstart robot is unavailable.");
            var cameraObject = new GameObject("Forward Semantic Camera");
            cameraObject.transform.SetParent(robot, false);
            cameraObject.transform.localPosition = new Vector3(
                0f,
                (ToFloat(sensor.MountHeightMeters) - robot.position.y) /
                    robot.lossyScale.y,
                0f);
            cameraObject.transform.localRotation = Quaternion.Euler(
                ToFloat(sensor.PitchDegrees),
                0f,
                0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.blue;
            camera.fieldOfView = ToFloat(sensor.VerticalFovDegrees);
            camera.nearClipPlane = ToFloat(sensor.NearClipMeters);
            camera.farClipPlane = ToFloat(sensor.FarClipMeters);
            camera.aspect = sensor.Width / (float)sensor.Height;
            camera.cullingMask = ~(1 << HiddenFromSemanticCameraLayer);
            ForwardCamera = camera;
        }

        private static ForwardCameraSensor FindForwardCameraSensor(ScenarioBundle scenario)
        {
            if (scenario.Sensors != null)
            {
                foreach (SensorSpec sensor in scenario.Sensors)
                {
                    if (sensor is ForwardCameraSensor forwardCamera)
                    {
                        return forwardCamera;
                    }
                }
            }

            throw new InvalidOperationException(
                "Scenario does not define a forward camera sensor.");
        }

        private void CreateOverviewCamera(
            float width,
            float depth,
            float minX,
            float maxX,
            float minZ,
            float maxZ)
        {
            var cameraObject = new GameObject("Overview Camera");
            cameraObject.transform.SetParent(RequireRoot().transform, false);
            cameraObject.transform.position = new Vector3(
                (minX + maxX) * 0.5f,
                Math.Max(width, depth),
                (minZ + maxZ) * 0.5f);
            cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = Math.Max(width, depth) * 0.62f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = Math.Max(width, depth) * 3f;
        }

        private void CreateLighting()
        {
            var lightObject = new GameObject("Tutorial Light");
            lightObject.transform.SetParent(RequireRoot().transform, false);
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
        }

        private GameObject CreatePrimitive(PrimitiveType type, string name, Color color)
        {
            GameObject visual = GameObject.CreatePrimitive(type);
            visual.name = name;
            visual.transform.SetParent(RequireRoot().transform, false);
            Renderer renderer = visual.GetComponent<Renderer>() ??
                throw new InvalidOperationException($"Primitive '{name}' has no renderer.");
            renderer.sharedMaterial = CreateMaterial(name, color);
            return visual;
        }

        private Material CreateMaterial(string name, Color color)
        {
            Shader shader = Resources.Load<Shader>("QuickstartSemantic") ??
                Shader.Find("EmbodiedLab/QuickstartSemantic") ??
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                throw new InvalidOperationException("No supported tutorial shader is available.");
            var material = new Material(shader)
            {
                name = $"{name} Material",
                color = color,
            };
            materials.Add(material);
            return material;
        }

        private GameObject RequireRoot()
        {
            return root ?? throw new InvalidOperationException("World root has not been created.");
        }

        private void Clear()
        {
            Scenario = null;
            RobotTransform = null;
            GoalTransform = null;
            ForwardCamera = null;
            RobotStartPosition = default;
            RobotStartRotation = default;
            GoalRadius = 0f;
            if (root != null)
            {
                DestroyObject(root);
                root = null;
            }

            foreach (Material material in materials)
            {
                DestroyObject(material);
            }

            materials.Clear();
        }

        private static void DestroyObject(UnityEngine.Object value)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }
    }
}
