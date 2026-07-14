using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using EmbodiedLab.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace EmbodiedLab.Unity.Tests
{
    public sealed class ContractRoundTripTests
    {
        private static readonly JsonSerializerSettings SerializerSettings =
            new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
            };

        [Test]
        public void ScenarioBundleRoundTripPreservesConcreteTypes()
        {
            var scenario = RoundTrip<ScenarioBundle>(
                "navigation_default_scenario_bundle.json",
                "scenario-bundle.schema.json");

            AssertTypes(
                scenario.Sensors,
                typeof(ForwardCameraSensor),
                typeof(DistanceSensor));
            AssertTypes(
                scenario.Reward.Components,
                typeof(TerminalRewardComponent),
                typeof(DistanceDeltaRewardComponent),
                typeof(CollisionRewardComponent),
                typeof(PerStepRewardComponent));
        }

        [Test]
        public void ResultDocumentAndResultBundleRoundTrip()
        {
            var resultDocument = RoundTrip<ResultDocument>(
                "navigation_completed_result_document.json",
                "result-document.schema.json");
            Assert.That(resultDocument.ResultBundle, Is.Not.Null);

            var documentJson = JObject.Parse(
                ReadFixture("navigation_completed_result_document.json"));
            var resultBundleJson = documentJson["result_bundle"]?.ToString();
            Assert.That(resultBundleJson, Is.Not.Null);
            RoundTripJson<ResultBundle>(
                resultBundleJson,
                "result-bundle.schema.json");
        }

        [Test]
        public void ReplayBundleManifestRoundTrips()
        {
            RoundTrip<ReplayBundleManifest>(
                "navigation_replay_bundle_manifest.json",
                "replay-bundle-manifest.schema.json");
        }

        [Test]
        public void ReplayLogContainsTwoRoundTrippableSteps()
        {
            var replayPath = Path.Combine(
                FixtureDirectory,
                "navigation_default_replay_log.jsonl");
            var replayCount = 0;
            foreach (var line in File.ReadLines(replayPath))
            {
                RoundTripJson<ReplayLogStep>(
                    line,
                    "replay-log-step.schema.json");
                replayCount++;
            }

            Assert.That(replayCount, Is.EqualTo(2));
        }

        [Test]
        public void ScenarioBundleJsonRoundTripsCanonicalScenario()
        {
            ScenarioBundle scenario = ScenarioBundleJson.Deserialize(
                ReadFixture("navigation_default_scenario_bundle.json"));
            string json = ScenarioBundleJson.Serialize(scenario, indented: true);
            ScenarioBundle reparsed = ScenarioBundleJson.Deserialize(json);

            Assert.That(reparsed.ScenarioId, Is.EqualTo("navigation_default"));
            AssertTypes(
                reparsed.Sensors,
                typeof(ForwardCameraSensor),
                typeof(DistanceSensor));
        }

        [Test]
        public void ReplayReadersHandlePlainAndCompressedLogs()
        {
            ReplayBundleManifest manifest = EmbodiedLabReplay.ReadManifest(
                Path.Combine(
                    FixtureDirectory,
                    "navigation_replay_bundle_manifest.json"));
            Assert.That(manifest.Chunks, Has.Count.EqualTo(2));

            string replayPath = Path.Combine(
                FixtureDirectory,
                "navigation_default_replay_log.jsonl");
            Assert.That(EmbodiedLabReplay.ReadSteps(replayPath), Has.Count.EqualTo(2));
            Assert.That(
                EmbodiedLabReplay.ParseSteps(File.ReadAllText(replayPath)),
                Has.Count.EqualTo(2));

            string gzipPath = Path.Combine(
                Path.GetTempPath(),
                $"embodiedlab-replay-{Guid.NewGuid():N}.jsonl.gz");
            try
            {
                using (var file = File.Create(gzipPath))
                using (var gzip = new GZipStream(file, CompressionMode.Compress))
                using (var writer = new StreamWriter(gzip))
                {
                    writer.Write(File.ReadAllText(replayPath));
                }

                Assert.That(
                    EmbodiedLabReplay.ReadSteps(gzipPath),
                    Has.Count.EqualTo(2));
            }
            finally
            {
                File.Delete(gzipPath);
            }
        }

        private static string FixtureDirectory
        {
            get
            {
                var packageInfo = PackageInfo.FindForAssembly(
                    typeof(ScenarioBundle).Assembly);
                if (packageInfo == null)
                {
                    throw new InvalidOperationException(
                        "Could not resolve the EmbodiedLab Unity package path.");
                }

                var fixtureDirectory = Path.Combine(
                    packageInfo.resolvedPath,
                    "Tests~",
                    "Fixtures");
                if (!Directory.Exists(fixtureDirectory))
                {
                    throw new DirectoryNotFoundException(
                        $"Canonical fixture directory not found: {fixtureDirectory}");
                }

                return fixtureDirectory;
            }
        }

        private static T RoundTrip<T>(string filename, string schemaFilename)
        {
            return RoundTripJson<T>(ReadFixture(filename), schemaFilename);
        }

        private static T RoundTripJson<T>(string json, string schemaFilename)
        {
            var expectedJson = JToken.Parse(json);
            var value = JsonConvert.DeserializeObject<T>(json, SerializerSettings);
            Assert.That(value, Is.Not.Null, $"Could not deserialize {typeof(T).Name}.");

            var serialized = JsonConvert.SerializeObject(value);
            var actualJson = JToken.Parse(serialized);
            ContractJsonAssertions.AssertPreserved(
                expectedJson,
                actualJson,
                JObject.Parse(ReadSchema(schemaFilename)),
                typeof(T).Name);

            var roundTripped = JsonConvert.DeserializeObject<T>(
                serialized,
                SerializerSettings);
            Assert.That(
                roundTripped,
                Is.Not.Null,
                $"Could not deserialize round-tripped {typeof(T).Name}.");
            return roundTripped;
        }

        private static void AssertTypes<T>(
            IEnumerable<T> values,
            params Type[] expectedTypes)
        {
            var actualTypes = values.Select(value => value.GetType()).ToArray();
            Assert.That(actualTypes, Is.EqualTo(expectedTypes));
        }

        private static string ReadFixture(string filename)
        {
            return File.ReadAllText(Path.Combine(FixtureDirectory, filename));
        }

        private static string ReadSchema(string filename)
        {
            var schemaPath = Path.GetFullPath(
                Path.Combine(
                    FixtureDirectory,
                    "..",
                    "..",
                    "Schemas~",
                    "v0",
                    filename));
            return File.ReadAllText(schemaPath);
        }
    }
}
