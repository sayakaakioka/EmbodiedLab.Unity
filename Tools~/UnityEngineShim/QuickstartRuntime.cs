#nullable enable

using System;

namespace UnityEngine
{
    public class Object
    {
    }

    public class MonoBehaviour : Object
    {
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
    }

    public static class Application
    {
        public static string persistentDataPath => string.Empty;
    }

    public static class Debug
    {
        public static void LogException(Exception exception, Object context)
        {
        }
    }
}
