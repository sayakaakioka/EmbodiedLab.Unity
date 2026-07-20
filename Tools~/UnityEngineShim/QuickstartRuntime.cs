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

        public T? GetComponent<T>()
            where T : Component, new()
        {
            return new T();
        }
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

        public int layer { get; set; }

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

        public Vector3 localPosition { get; set; }

        public Quaternion localRotation { get; set; }

        public Vector3 lossyScale => new(1f, 1f, 1f);

        public Vector3 forward => new(0f, 0f, 1f);

        public void SetParent(Transform parent, bool worldPositionStays)
        {
        }

        public void Rotate(float x, float y, float z, Space space)
        {
        }

        public void SetPositionAndRotation(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }

        public T? GetComponent<T>()
            where T : Component, new()
        {
            return new T();
        }
    }

    public enum PrimitiveType
    {
        Cube,
        Capsule,
        Cylinder,
    }

    public enum Space
    {
        World,
        Self,
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

        public static Vector3 up => new(0f, 1f, 0f);

        public static Vector3 operator +(Vector3 left, Vector3 right)
        {
            return new Vector3(left.x + right.x, left.y + right.y, left.z + right.z);
        }

        public static Vector3 operator -(Vector3 left, Vector3 right)
        {
            return new Vector3(left.x - right.x, left.y - right.y, left.z - right.z);
        }

        public static Vector3 operator *(Vector3 value, float multiplier)
        {
            return new Vector3(
                value.x * multiplier,
                value.y * multiplier,
                value.z * multiplier);
        }
    }

    public readonly struct Quaternion
    {
        public static Quaternion Euler(float x, float y, float z)
        {
            return new Quaternion();
        }

        public Vector3 eulerAngles => new(0f, 0f, 0f);
    }

    public readonly struct Color
    {
        public Color(float red, float green, float blue, float alpha = 1f)
        {
        }

        public static Color green => new(0f, 1f, 0f);

        public static Color blue => new(0f, 0f, 1f);
    }

    public readonly struct Color32
    {
        public Color32(byte red, byte green, byte blue, byte alpha)
        {
            r = red;
            g = green;
            b = blue;
            a = alpha;
        }

        public byte r { get; }

        public byte g { get; }

        public byte b { get; }

        public byte a { get; }
    }

    public static class Mathf
    {
        public const float Rad2Deg = 57.29578f;

        public static float Atan2(float y, float x)
        {
            return (float)Math.Atan2(y, x);
        }

        public static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }

        public static float Clamp01(float value)
        {
            return Clamp(value, 0f, 1f);
        }

        public static float Repeat(float value, float length)
        {
            return Clamp(value - (float)Math.Floor(value / length) * length, 0f, length);
        }

        public static float Sqrt(float value)
        {
            return (float)Math.Sqrt(value);
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
        public bool enabled { get; set; }

        public bool orthographic { get; set; }

        public float orthographicSize { get; set; }

        public float nearClipPlane { get; set; }

        public float farClipPlane { get; set; }

        public CameraClearFlags clearFlags { get; set; }

        public Color backgroundColor { get; set; }

        public float fieldOfView { get; set; }

        public float aspect { get; set; }

        public int cullingMask { get; set; }

        public RenderTexture? targetTexture { get; set; }

        public void Render()
        {
        }
    }

    public enum CameraClearFlags
    {
        SolidColor,
    }

    public class Collider : Component
    {
    }

    public sealed class CapsuleCollider : Collider
    {
        public Bounds bounds { get; set; }
    }

    public readonly struct Bounds
    {
        public Vector3 center => new(0f, 0.5f, 0f);

        public Vector3 extents => new(0.5f, 0.5f, 0.5f);
    }

    public readonly struct RaycastHit
    {
        public Collider? collider => null;
    }

    public enum QueryTriggerInteraction
    {
        Ignore,
    }

    public static class Physics
    {
        public static bool CapsuleCast(
            Vector3 point1,
            Vector3 point2,
            float radius,
            Vector3 direction,
            out RaycastHit hit,
            float maxDistance,
            int layerMask,
            QueryTriggerInteraction queryTriggerInteraction)
        {
            hit = new RaycastHit();
            return false;
        }

        public static void SyncTransforms()
        {
        }
    }

    public sealed class RenderTexture : Object
    {
        public RenderTexture(
            int width,
            int height,
            int depth,
            RenderTextureFormat format)
        {
        }

        public static RenderTexture? active { get; set; }

        public bool Create()
        {
            return true;
        }

        public void Release()
        {
        }
    }

    public enum RenderTextureFormat
    {
        ARGB32,
    }

    public sealed class Texture2D : Object
    {
        public Texture2D(
            int width,
            int height,
            TextureFormat textureFormat,
            bool mipChain)
        {
        }

        public void ReadPixels(Rect source, int destinationX, int destinationY)
        {
        }

        public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
        {
        }

        public Color32[] GetPixels32()
        {
            return new Color32[QuickstartImageValueCount / 3];
        }

        private const int QuickstartImageValueCount = 3 * 84 * 112;
    }

    public enum TextureFormat
    {
        RGB24,
    }

    public static class SystemInfo
    {
        public static Rendering.GraphicsDeviceType graphicsDeviceType =>
            Rendering.GraphicsDeviceType.Direct3D11;
    }

    public static class Resources
    {
        public static T? Load<T>(string path)
            where T : Object
        {
            return null;
        }
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
        public GUIStyle()
        {
        }

        public GUIStyle(GUIStyle source)
        {
        }

        public int fontSize { get; set; }

        public TextClipping clipping { get; set; }
    }

    public enum TextClipping
    {
        Overflow,
        Clip,
    }

    public sealed class GUISkin
    {
        public GUIStyle box => new();

        public GUIStyle label => new();
    }

    public static class GUI
    {
        public static bool enabled { get; set; }

        public static Color color { get; set; } = new Color(1f, 1f, 1f);

        public static GUISkin skin => new();

        public static void Label(Rect position, string text, GUIStyle style)
        {
        }
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

        public static void LogException(Exception exception)
        {
        }
    }
}

namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType
    {
        Null,
        Direct3D11,
    }
}
