#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity.Internal;
using Newtonsoft.Json;

namespace EmbodiedLab.Unity
{
    /// <summary>
    /// Reads versioned EmbodiedLab replay manifests and step logs.
    /// </summary>
    public static class EmbodiedLabReplay
    {
        private static readonly JsonSerializerSettings SerializerSettings = new()
        {
            MissingMemberHandling = MissingMemberHandling.Error,
        };

        public static ReplayBundleManifest ReadManifest(string path)
        {
            return ReadManifest(path, ReplayResourceLimits.Default);
        }

        internal static ReplayBundleManifest ReadManifest(
            string path,
            ReplayResourceLimits limits)
        {
            RequirePath(path);
            RequireLimits(limits);
            using var file = File.OpenRead(path);
            using var limited = new ResourceLimitedReadStream(
                file,
                limits.MaximumManifestBytes,
                "Replay manifest",
                leaveOpen: true);
            using var reader = new StreamReader(
                limited,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: false);
            string json = reader.ReadToEnd();
            ReplayBundleManifest manifest =
                JsonConvert.DeserializeObject<ReplayBundleManifest>(
                    json,
                    SerializerSettings) ??
                throw new JsonSerializationException(
                    "Replay manifest did not contain a replay bundle.");
            ValidateManifest(manifest, limits);
            return manifest;
        }

        public static IReadOnlyList<ReplayLogStep> ReadSteps(string path)
        {
            return ReadSteps(path, ReplayResourceLimits.Default);
        }

        internal static IReadOnlyList<ReplayLogStep> ReadSteps(
            string path,
            ReplayResourceLimits limits)
        {
            RequirePath(path);
            RequireLimits(limits);
            using var file = File.OpenRead(path);
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                return ReadSteps(gzip, limits);
            }

            return ReadSteps(file, limits);
        }

        public static IReadOnlyList<ReplayLogStep> ParseSteps(string jsonLines)
        {
            return ParseSteps(jsonLines, ReplayResourceLimits.Default);
        }

        internal static IReadOnlyList<ReplayLogStep> ParseSteps(
            string jsonLines,
            ReplayResourceLimits limits)
        {
            if (jsonLines == null)
            {
                throw new ArgumentNullException(nameof(jsonLines));
            }

            RequireLimits(limits);
            if (Encoding.UTF8.GetByteCount(jsonLines) > limits.MaximumDecompressedBytes)
            {
                throw new InvalidDataException(
                    $"Replay log exceeds the maximum size of " +
                    $"{limits.MaximumDecompressedBytes} bytes.");
            }

            using var reader = new StringReader(jsonLines);
            return ReadSteps(reader, limits);
        }

        internal static void ValidateChunkMetadata(ReplayBundleChunk chunk)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            ValidateChunkMetadata(chunk, ReplayResourceLimits.Default);
        }

        private static IReadOnlyList<ReplayLogStep> ReadSteps(
            Stream stream,
            ReplayResourceLimits limits)
        {
            using var limited = new ResourceLimitedReadStream(
                stream,
                limits.MaximumDecompressedBytes,
                "Decompressed replay log",
                leaveOpen: true);
            using var reader = new StreamReader(
                limited,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: false);
            return ReadSteps(reader, limits);
        }

        private static IReadOnlyList<ReplayLogStep> ReadSteps(
            TextReader reader,
            ReplayResourceLimits limits)
        {
            var steps = new List<ReplayLogStep>();
            var lineReader = new BoundedLineReader(
                reader,
                limits.MaximumLineBytes);
            string? line;
            while ((line = lineReader.ReadLine()) != null)
            {
                if (Encoding.UTF8.GetByteCount(line) > limits.MaximumLineBytes)
                {
                    throw new InvalidDataException(
                        $"Replay JSONL row exceeds the maximum size of " +
                        $"{limits.MaximumLineBytes} bytes.");
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (steps.Count >= limits.MaximumSteps)
                {
                    throw new InvalidDataException(
                        $"Replay log exceeds the maximum step count of " +
                        $"{limits.MaximumSteps}.");
                }

                ReplayLogStep step = JsonConvert.DeserializeObject<ReplayLogStep>(
                    line,
                    SerializerSettings) ??
                    throw new JsonSerializationException(
                        "Replay log row did not contain a replay step.");
                steps.Add(step);
            }

            return steps;
        }

        private static void ValidateManifest(
            ReplayBundleManifest manifest,
            ReplayResourceLimits limits)
        {
            ICollection<ReplayBundleChunk>? chunks = manifest.Chunks;
            if (chunks == null)
            {
                return;
            }

            if (chunks.Count > limits.MaximumManifestChunks)
            {
                throw new InvalidDataException(
                    $"Replay manifest exceeds the maximum chunk count of " +
                    $"{limits.MaximumManifestChunks}.");
            }

            foreach (ReplayBundleChunk? chunk in chunks)
            {
                if (chunk == null)
                {
                    throw new InvalidDataException(
                        "Replay manifest contains an empty chunk entry.");
                }

                ValidateChunkMetadata(chunk, limits);
            }
        }

        private static void ValidateChunkMetadata(
            ReplayBundleChunk chunk,
            ReplayResourceLimits limits)
        {
            if (string.IsNullOrEmpty(chunk.Path) ||
                chunk.Path.Length > limits.MaximumChunkPathCharacters)
            {
                throw new InvalidDataException(
                    $"Replay chunk path must contain between 1 and " +
                    $"{limits.MaximumChunkPathCharacters} characters.");
            }

            if (chunk.StepCount < 0 ||
                chunk.StepCount > limits.MaximumDeclaredChunkSteps)
            {
                throw new InvalidDataException(
                    $"Replay chunk step count must be between 0 and " +
                    $"{limits.MaximumDeclaredChunkSteps}.");
            }
        }

        private static void RequirePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be empty.", nameof(path));
            }
        }

        private static void RequireLimits(ReplayResourceLimits limits)
        {
            if (limits == null)
            {
                throw new ArgumentNullException(nameof(limits));
            }
        }

        private sealed class BoundedLineReader
        {
            private readonly TextReader reader;
            private readonly int maximumCharacters;
            private readonly char[] buffer = new char[4096];
            private int bufferLength;
            private int bufferOffset;
            private bool reachedEnd;

            internal BoundedLineReader(TextReader reader, int maximumCharacters)
            {
                this.reader = reader;
                this.maximumCharacters = maximumCharacters;
            }

            internal string? ReadLine()
            {
                if (reachedEnd && bufferOffset >= bufferLength)
                {
                    return null;
                }

                StringBuilder? line = null;
                while (true)
                {
                    if (!EnsureBuffer())
                    {
                        return line?.ToString();
                    }

                    int segmentStart = bufferOffset;
                    while (bufferOffset < bufferLength &&
                        buffer[bufferOffset] != '\r' &&
                        buffer[bufferOffset] != '\n')
                    {
                        bufferOffset++;
                    }

                    AppendSegment(ref line, segmentStart, bufferOffset - segmentStart);
                    if (bufferOffset >= bufferLength)
                    {
                        continue;
                    }

                    char terminator = buffer[bufferOffset++];
                    if (terminator == '\r' && EnsureBuffer() &&
                        buffer[bufferOffset] == '\n')
                    {
                        bufferOffset++;
                    }

                    return line?.ToString() ?? string.Empty;
                }
            }

            private bool EnsureBuffer()
            {
                if (bufferOffset < bufferLength)
                {
                    return true;
                }

                bufferLength = reader.Read(buffer, 0, buffer.Length);
                bufferOffset = 0;
                reachedEnd = bufferLength == 0;
                return !reachedEnd;
            }

            private void AppendSegment(
                ref StringBuilder? line,
                int segmentStart,
                int segmentLength)
            {
                int currentLength = line?.Length ?? 0;
                if (segmentLength > maximumCharacters - currentLength)
                {
                    throw new InvalidDataException(
                        $"Replay JSONL row exceeds the maximum size of " +
                        $"{maximumCharacters} bytes.");
                }

                if (segmentLength == 0)
                {
                    return;
                }

                line ??= new StringBuilder(Math.Min(maximumCharacters, 4096));
                line.Append(buffer, segmentStart, segmentLength);
            }
        }
    }

    internal sealed class ReplayResourceLimits
    {
        internal static ReplayResourceLimits Default { get; } = new(
            maximumManifestBytes: 1024L * 1024L,
            maximumManifestChunks: 4096,
            maximumChunkPathCharacters: 1024,
            maximumDeclaredChunkSteps: 100000,
            maximumDecompressedBytes: 256L * 1024L * 1024L,
            maximumLineBytes: 1024 * 1024,
            maximumSteps: 100000);

        internal ReplayResourceLimits(
            long maximumManifestBytes,
            int maximumManifestChunks,
            int maximumChunkPathCharacters,
            int maximumDeclaredChunkSteps,
            long maximumDecompressedBytes,
            int maximumLineBytes,
            int maximumSteps)
        {
            MaximumManifestBytes = RequirePositive(
                maximumManifestBytes,
                nameof(maximumManifestBytes));
            MaximumManifestChunks = RequirePositive(
                maximumManifestChunks,
                nameof(maximumManifestChunks));
            MaximumChunkPathCharacters = RequirePositive(
                maximumChunkPathCharacters,
                nameof(maximumChunkPathCharacters));
            MaximumDeclaredChunkSteps = RequirePositive(
                maximumDeclaredChunkSteps,
                nameof(maximumDeclaredChunkSteps));
            MaximumDecompressedBytes = RequirePositive(
                maximumDecompressedBytes,
                nameof(maximumDecompressedBytes));
            MaximumLineBytes = RequirePositive(
                maximumLineBytes,
                nameof(maximumLineBytes));
            MaximumSteps = RequirePositive(maximumSteps, nameof(maximumSteps));
        }

        internal long MaximumManifestBytes { get; }

        internal int MaximumManifestChunks { get; }

        internal int MaximumChunkPathCharacters { get; }

        internal int MaximumDeclaredChunkSteps { get; }

        internal long MaximumDecompressedBytes { get; }

        internal int MaximumLineBytes { get; }

        internal int MaximumSteps { get; }

        private static int RequirePositive(int value, string parameterName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }

        private static long RequirePositive(long value, string parameterName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }
    }
}
