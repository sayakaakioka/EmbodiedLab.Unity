#nullable enable

using System;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; } = string.Empty;

        public static void Destroy(Object target)
        {
        }

        public static void DestroyImmediate(Object target)
        {
        }
    }

    public class Component : Object
    {
        public Transform transform { get; } = new();
    }

    public class MonoBehaviour : Component
    {
    }

    public sealed class GameObject : Object
    {
        public GameObject(string name = "")
        {
            this.name = name;
        }

        public Transform transform { get; } = new();

        public static GameObject CreatePrimitive(PrimitiveType type)
        {
            return new GameObject(type.ToString());
        }

        public T AddComponent<T>()
            where T : Component, new()
        {
            return new T();
        }

        public T? GetComponent<T>()
            where T : Component, new()
        {
            return new T();
        }
    }

    public sealed class Transform
    {
        public Vector3 position { get; set; }

        public Vector3 localScale { get; set; }

        public Quaternion rotation { get; set; }

        public void SetParent(Transform parent, bool worldPositionStays)
        {
        }
    }

    public enum PrimitiveType
    {
        Cube,
        Capsule,
        Cylinder,
    }

    public readonly struct Vector2
    {
        public Vector2(float x, float y)
        {
        }
    }

    public readonly struct Vector3
    {
        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public float x { get; }

        public float y { get; }

        public float z { get; }
    }

    public readonly struct Quaternion
    {
        public static Quaternion Euler(float x, float y, float z)
        {
            return new Quaternion();
        }
    }

    public readonly struct Color
    {
        public Color(float red, float green, float blue, float alpha = 1f)
        {
        }
    }

    public sealed class Shader : Object
    {
        public static Shader? Find(string name)
        {
            return new Shader { name = name };
        }
    }

    public sealed class Material : Object
    {
        public Material(Shader shader)
        {
        }

        public Color color { get; set; }
    }

    public sealed class Renderer : Component
    {
        public Material? sharedMaterial { get; set; }
    }

    public sealed class Camera : Component
    {
        public bool orthographic { get; set; }

        public float orthographicSize { get; set; }

        public float nearClipPlane { get; set; }

        public float farClipPlane { get; set; }
    }

    public sealed class Light : Component
    {
        public LightType type { get; set; }

        public float intensity { get; set; }
    }

    public enum LightType
    {
        Directional,
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponentAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute
    {
    }

    public sealed class TextAsset : Object
    {
        public string text => string.Empty;
    }

    public readonly struct Rect
    {
        public Rect(float x, float y, float width, float height)
        {
        }
    }

    public sealed class GUILayoutOption
    {
    }

    public sealed class GUIStyle
    {
    }

    public sealed class GUISkin
    {
        public GUIStyle box => new();
    }

    public static class GUI
    {
        public static bool enabled { get; set; }

        public static GUISkin skin => new();
    }

    public static class GUILayout
    {
        public static void BeginArea(Rect rect, GUIStyle style)
        {
        }

        public static void EndArea()
        {
        }

        public static void BeginHorizontal()
        {
        }

        public static void EndHorizontal()
        {
        }

        public static Vector2 BeginScrollView(
            Vector2 scrollPosition,
            params GUILayoutOption[] options)
        {
            return scrollPosition;
        }

        public static void EndScrollView()
        {
        }

        public static void Label(string text, params GUILayoutOption[] options)
        {
        }

        public static void Space(float pixels)
        {
        }

        public static string TextField(
            string text,
            params GUILayoutOption[] options)
        {
            return text;
        }

        public static bool Button(
            string text,
            params GUILayoutOption[] options)
        {
            return false;
        }

        public static GUILayoutOption Width(float width)
        {
            return new GUILayoutOption();
        }

        public static GUILayoutOption Height(float height)
        {
            return new GUILayoutOption();
        }
    }

    public static class Application
    {
        public static bool isPlaying => false;

        public static string persistentDataPath => string.Empty;
    }

    public static class Time
    {
        public static float deltaTime => 0f;
    }

    public static class Debug
    {
        public static void LogException(Exception exception, Object context)
        {
        }
    }
}
