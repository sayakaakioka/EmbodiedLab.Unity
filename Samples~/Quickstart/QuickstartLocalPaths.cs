#nullable enable

using System;
using System.IO;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal static class QuickstartLocalPaths
    {
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
    }
}
