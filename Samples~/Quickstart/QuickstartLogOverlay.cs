#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal enum QuickstartLogLevel
    {
        Info,
        Warning,
        Error,
    }

    internal readonly struct QuickstartLogEntry
    {
        internal QuickstartLogEntry(string message, QuickstartLogLevel level)
        {
            Message = message;
            Level = level;
        }

        internal string Message { get; }

        internal QuickstartLogLevel Level { get; }
    }

    internal sealed class QuickstartLogOverlay
    {
        internal const int MaximumEntries = 7;

        private const float LineHeight = 20f;
        private readonly List<QuickstartLogEntry> entries = new(MaximumEntries);
        private GUIStyle? labelStyle;

        internal IReadOnlyList<QuickstartLogEntry> Entries => entries;

        internal void Add(
            string message,
            QuickstartLogLevel level = QuickstartLogLevel.Info)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (entries.Count > 0 &&
                string.Equals(entries[^1].Message, message, StringComparison.Ordinal))
            {
                if (level > entries[^1].Level)
                {
                    entries[^1] = new QuickstartLogEntry(message, level);
                }

                return;
            }

            if (entries.Count == MaximumEntries)
            {
                entries.RemoveAt(0);
            }

            entries.Add(new QuickstartLogEntry(message, level));
        }

        internal void Draw(float left, float top, float width)
        {
            labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                clipping = TextClipping.Clip,
            };
            Color previousColor = GUI.color;
            for (int index = 0; index < entries.Count; index++)
            {
                QuickstartLogEntry entry = entries[index];
                float lineTop = top + (index * LineHeight);
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.Label(
                    new Rect(left + 1f, lineTop + 1f, width, LineHeight),
                    entry.Message,
                    labelStyle);
                GUI.color = GetColor(entry.Level);
                GUI.Label(
                    new Rect(left, lineTop, width, LineHeight),
                    entry.Message,
                    labelStyle);
            }

            GUI.color = previousColor;
        }

        private static Color GetColor(QuickstartLogLevel level)
        {
            return level switch
            {
                QuickstartLogLevel.Warning => new Color(1f, 0.82f, 0.25f),
                QuickstartLogLevel.Error => new Color(1f, 0.38f, 0.38f),
                _ => new Color(0.92f, 0.95f, 1f),
            };
        }
    }
}
