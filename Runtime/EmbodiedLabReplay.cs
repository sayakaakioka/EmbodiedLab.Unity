#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using EmbodiedLab.Contracts;
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
            RequirePath(path);
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<ReplayBundleManifest>(
                json,
                SerializerSettings) ??
                throw new JsonSerializationException(
                    "Replay manifest did not contain a replay bundle.");
        }

        public static IReadOnlyList<ReplayLogStep> ReadSteps(string path)
        {
            RequirePath(path);
            using var file = File.OpenRead(path);
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip);
                return ReadSteps(reader);
            }

            using var plainReader = new StreamReader(file);
            return ReadSteps(plainReader);
        }

        public static IReadOnlyList<ReplayLogStep> ParseSteps(string jsonLines)
        {
            if (jsonLines == null)
            {
                throw new ArgumentNullException(nameof(jsonLines));
            }

            using var reader = new StringReader(jsonLines);
            return ReadSteps(reader);
        }

        private static IReadOnlyList<ReplayLogStep> ReadSteps(TextReader reader)
        {
            var steps = new List<ReplayLogStep>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
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

        private static void RequirePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be empty.", nameof(path));
            }
        }
    }
}
