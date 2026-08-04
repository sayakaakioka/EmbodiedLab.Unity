#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
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

        public static ReplayBundleManifest ReadManifest(
            string path,
            string expectedJobId,
            string expectedScenarioId)
        {
            ReplayBundleManifest manifest = ReadManifest(path);
            if (!string.Equals(
                    manifest.JobId,
                    RequireIdentity(expectedJobId, nameof(expectedJobId)),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.ScenarioId,
                    RequireIdentity(expectedScenarioId, nameof(expectedScenarioId)),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Replay manifest belongs to a different job or Scenario.");
            }

            return manifest;
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

        public static IReadOnlyList<ReplayLogStep> ReadChunk(
            string path,
            ReplayBundleChunk chunk,
            string expectedJobId,
            string expectedScenarioId)
        {
            return ReadChunk(
                path,
                chunk,
                expectedJobId,
                expectedScenarioId,
                CancellationToken.None);
        }

        internal static IReadOnlyList<ReplayLogStep> ReadChunk(
            string path,
            ReplayBundleChunk chunk,
            string expectedJobId,
            string expectedScenarioId,
            CancellationToken cancellationToken)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ReplayLogStep> steps = ReadSteps(
                path,
                ReplayResourceLimits.Default,
                compressed: IsCompressedChunk(chunk),
                cancellationToken);
            ValidateChunkSteps(
                chunk,
                steps,
                RequireIdentity(expectedJobId, nameof(expectedJobId)),
                RequireIdentity(expectedScenarioId, nameof(expectedScenarioId)),
                cancellationToken);
            return steps;
        }

        internal static IReadOnlyList<ReplayLogStep> ReadSteps(
            string path,
            ReplayResourceLimits limits)
        {
            return ReadSteps(
                path,
                limits,
                compressed: path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase),
                CancellationToken.None);
        }

        private static IReadOnlyList<ReplayLogStep> ReadSteps(
            string path,
            ReplayResourceLimits limits,
            bool compressed,
            CancellationToken cancellationToken)
        {
            RequirePath(path);
            RequireLimits(limits);
            cancellationToken.ThrowIfCancellationRequested();
            using var file = File.OpenRead(path);
            if (compressed)
            {
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                return ReadSteps(gzip, limits, cancellationToken);
            }

            return ReadSteps(file, limits, cancellationToken);
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
            return ReadSteps(reader, limits, CancellationToken.None);
        }

        internal static void ValidateChunkMetadata(ReplayBundleChunk chunk)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            ValidateChunkMetadata(chunk, ReplayResourceLimits.Default);
        }

        internal static void ValidateChunkSteps(
            ReplayBundleChunk chunk,
            IReadOnlyList<ReplayLogStep> steps,
            string expectedJobId,
            string expectedScenarioId,
            CancellationToken cancellationToken = default)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            if (steps == null)
            {
                throw new ArgumentNullException(nameof(steps));
            }

            int expectedStepCount = GetChunkStepCount(chunk);
            if (steps.Count != expectedStepCount)
            {
                throw new InvalidDataException(
                    "Replay chunk step count does not match its manifest entry.");
            }

            ReplayLogStepPhase expectedPhase;
            ReplayLogStepPolicyMode expectedPolicyMode;
            int? expectedEpisodeCount;
            int? expectedStartStep;
            int? expectedEndStep;
            switch (chunk)
            {
                case TrainReplayBundleChunk training:
                    expectedPhase = ReplayLogStepPhase.Train;
                    expectedPolicyMode = ReplayLogStepPolicyMode.Stochastic;
                    expectedEpisodeCount = null;
                    expectedStartStep = training.StartStep;
                    expectedEndStep = training.EndStep;
                    if (training.StepCount <= 0 ||
                        training.StartStep < 0 ||
                        training.EndStep < training.StartStep ||
                        training.CheckpointStep != training.EndStep)
                    {
                        throw new InvalidDataException(
                            "Training replay chunk step metadata is inconsistent.");
                    }

                    break;
                case EvalReplayBundleChunk evaluation:
                    expectedPhase = ReplayLogStepPhase.Eval;
                    expectedPolicyMode = ReplayLogStepPolicyMode.Deterministic;
                    expectedEpisodeCount = evaluation.EpisodeCount;
                    expectedStartStep = null;
                    expectedEndStep = null;
                    break;
                default:
                    throw new InvalidDataException(
                        "Replay manifest contains an unsupported chunk type.");
            }

            var episodeIds = new HashSet<string>(StringComparer.Ordinal);
            int minimumCheckpointStep = int.MaxValue;
            int maximumCheckpointStep = int.MinValue;
            foreach (ReplayLogStep step in steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (step == null ||
                    !string.Equals(step.JobId, expectedJobId, StringComparison.Ordinal) ||
                    !string.Equals(
                        step.ScenarioId,
                        expectedScenarioId,
                        StringComparison.Ordinal) ||
                    step.Phase != expectedPhase ||
                    step.PolicyMode != expectedPolicyMode)
                {
                    throw new InvalidDataException(
                        "Replay chunk contains a step outside its manifest entry.");
                }

                if (expectedStartStep.HasValue)
                {
                    if (step.CheckpointStep < expectedStartStep.Value ||
                        step.CheckpointStep > expectedEndStep!.Value)
                    {
                        throw new InvalidDataException(
                            "Replay chunk contains a step outside its manifest range.");
                    }

                    minimumCheckpointStep = Math.Min(
                        minimumCheckpointStep,
                        step.CheckpointStep);
                    maximumCheckpointStep = Math.Max(
                        maximumCheckpointStep,
                        step.CheckpointStep);
                }
                else if (step.CheckpointStep != chunk.CheckpointStep)
                {
                    throw new InvalidDataException(
                        "Replay chunk contains a step outside its manifest checkpoint.");
                }

                episodeIds.Add(step.EpisodeId);
            }

            if (expectedStartStep.HasValue &&
                (minimumCheckpointStep != expectedStartStep.Value ||
                    maximumCheckpointStep != expectedEndStep!.Value))
            {
                throw new InvalidDataException(
                    "Training replay chunk range does not match its manifest entry.");
            }

            if (expectedEpisodeCount.HasValue &&
                episodeIds.Count != expectedEpisodeCount.Value)
            {
                throw new InvalidDataException(
                    "Replay chunk episode count does not match its manifest entry.");
            }
        }

        private static IReadOnlyList<ReplayLogStep> ReadSteps(
            Stream stream,
            ReplayResourceLimits limits,
            CancellationToken cancellationToken)
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
            return ReadSteps(reader, limits, cancellationToken);
        }

        internal static IReadOnlyList<ReplayLogStep> ReadSteps(
            TextReader reader,
            ReplayResourceLimits limits,
            CancellationToken cancellationToken)
        {
            var steps = new List<ReplayLogStep>();
            var lineReader = new BoundedLineReader(
                reader,
                limits.MaximumLineBytes,
                cancellationToken);
            string? line;
            while ((line = lineReader.ReadLine()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                ContractSemanticValidator.ValidateReplayStep(step);
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

            ContractSemanticValidator.ValidateReplayManifest(manifest);
        }

        private static void ValidateChunkMetadata(
            ReplayBundleChunk chunk,
            ReplayResourceLimits limits)
        {
            string path = GetChunkPath(chunk);
            int stepCount = GetChunkStepCount(chunk);
            if (string.IsNullOrEmpty(path) ||
                path.Length > limits.MaximumChunkPathCharacters)
            {
                throw new InvalidDataException(
                    $"Replay chunk path must contain between 1 and " +
                    $"{limits.MaximumChunkPathCharacters} characters.");
            }

            if (stepCount < 0 ||
                stepCount > limits.MaximumDeclaredChunkSteps)
            {
                throw new InvalidDataException(
                    $"Replay chunk step count must be between 0 and " +
                    $"{limits.MaximumDeclaredChunkSteps}.");
            }
        }

        internal static string GetChunkPath(ReplayBundleChunk chunk)
        {
            return chunk switch
            {
                TrainReplayBundleChunk train => train.Path,
                EvalReplayBundleChunk evaluation => evaluation.Path,
                _ => throw new InvalidDataException(
                    "Replay manifest contains an unsupported chunk type."),
            };
        }

        private static int GetChunkStepCount(ReplayBundleChunk chunk)
        {
            return chunk switch
            {
                TrainReplayBundleChunk train => train.StepCount,
                EvalReplayBundleChunk evaluation => evaluation.StepCount,
                _ => throw new InvalidDataException(
                    "Replay manifest contains an unsupported chunk type."),
            };
        }

        private static bool IsCompressedChunk(ReplayBundleChunk chunk)
        {
            return chunk switch
            {
                TrainReplayBundleChunk train =>
                    train.Format == TrainReplayBundleChunkFormat.JsonlGz,
                EvalReplayBundleChunk evaluation =>
                    evaluation.Format == EvalReplayBundleChunkFormat.JsonlGz,
                _ => throw new InvalidDataException(
                    "Replay manifest contains an unsupported chunk type."),
            };
        }

        private static void RequirePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be empty.", nameof(path));
            }
        }

        private static string RequireIdentity(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Identity cannot be empty.", parameterName);
            }

            return value;
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
            private readonly CancellationToken cancellationToken;
            private readonly char[] buffer = new char[4096];
            private int bufferLength;
            private int bufferOffset;
            private bool reachedEnd;

            internal BoundedLineReader(
                TextReader reader,
                int maximumCharacters,
                CancellationToken cancellationToken)
            {
                this.reader = reader;
                this.maximumCharacters = maximumCharacters;
                this.cancellationToken = cancellationToken;
            }

            internal string? ReadLine()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reachedEnd && bufferOffset >= bufferLength)
                {
                    return null;
                }

                StringBuilder? line = null;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                cancellationToken.ThrowIfCancellationRequested();
                if (bufferOffset < bufferLength)
                {
                    return true;
                }

                bufferLength = reader.Read(buffer, 0, buffer.Length);
                cancellationToken.ThrowIfCancellationRequested();
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
