#nullable enable

using System;
using System.IO;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal static class QuickstartLocalPaths
    {
        private static readonly char[] PortableInvalidFileNameCharacters =
            { '<', '>', ':', '"', '|', '?', '*' };

        internal static string GetSubmissionDirectory(
            string persistentDataPath,
            string submissionId)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath))
            {
                throw new ArgumentException(
                    "Persistent data path cannot be empty.",
                    nameof(persistentDataPath));
            }

            if (string.IsNullOrWhiteSpace(submissionId) ||
                Path.IsPathRooted(submissionId) ||
                submissionId == "." ||
                submissionId == ".." ||
                submissionId.IndexOf('/') >= 0 ||
                submissionId.IndexOf('\\') >= 0 ||
                submissionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidDataException(
                    "Submission ID cannot be used as a local directory name.");
            }

            string root = Path.GetFullPath(
                Path.Combine(persistentDataPath, "EmbodiedLabQuickstart"));
            string candidate = Path.GetFullPath(Path.Combine(root, submissionId));
            string rootPrefix = root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Submission directory must remain inside Quickstart storage.");
            }

            return candidate;
        }

        internal static string GetReplayManifestPath(
            string persistentDataPath,
            string submissionId)
        {
            return Path.Combine(
                GetSubmissionDirectory(persistentDataPath, submissionId),
                "replay",
                "manifest.json");
        }

        internal static string GetModelPath(
            string persistentDataPath,
            string submissionId)
        {
            return Path.Combine(
                GetSubmissionDirectory(persistentDataPath, submissionId),
                "policy.onnx");
        }

        internal static string GetReplayChunkPath(
            string persistentDataPath,
            string submissionId,
            string chunkPath)
        {
            if (string.IsNullOrWhiteSpace(chunkPath) ||
                Path.IsPathRooted(chunkPath) ||
                chunkPath.IndexOf('\\') >= 0 ||
                chunkPath.IndexOf('#') >= 0 ||
                chunkPath.IndexOf('?') >= 0)
            {
                throw new InvalidDataException(
                    "Replay chunk path must be a relative object path.");
            }

            string replayRoot = Path.GetFullPath(
                Path.Combine(
                    GetSubmissionDirectory(persistentDataPath, submissionId),
                    "replay"));
            string candidate = replayRoot;
            foreach (string segment in chunkPath.Split('/'))
            {
                if (segment.Length == 0 ||
                    segment == "." ||
                    segment == ".." ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    segment.IndexOfAny(PortableInvalidFileNameCharacters) >= 0)
                {
                    throw new InvalidDataException(
                        "Replay chunk path contains an invalid segment.");
                }

                candidate = Path.Combine(candidate, segment);
            }

            candidate = Path.GetFullPath(candidate);
            string replayPrefix = replayRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(replayPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Replay chunk must remain inside Quickstart storage.");
            }

            return candidate;
        }
    }
}
