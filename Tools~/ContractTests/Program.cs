using System.IO.Compression;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity;
using EmbodiedLab.Unity.Tests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

var fixtureDirectory = args.Length == 1
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Tests~/Fixtures"));
var schemaDirectory = Path.GetFullPath(
    Path.Combine(fixtureDirectory, "../../Schemas~/v0"));

var scenario = RoundTrip<ScenarioBundle>(
    "navigation_default_scenario_bundle.json",
    "scenario-bundle.schema.json");
RoundTrip<ResultDocument>(
    "navigation_completed_result_document.json",
    "result-document.schema.json");
RoundTrip<ReplayBundleManifest>(
    "navigation_replay_bundle_manifest.json",
    "replay-bundle-manifest.schema.json");

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
_ = nameof(WorldSpec.StaticObstacles);
_ = nameof(ScenarioBundle.SchemaVersion);
_ = ArtifactFormat.JsonlGz;
AssertSchemaDefaultValidation();

var resultDocument = JObject.Parse(ReadFixture("navigation_completed_result_document.json"));
if (typeof(ResultDocument).GetProperty("Artifacts") is not null)
{
    throw new InvalidOperationException(
        "ResultDocument must not expose the legacy top-level Artifacts property.");
}

if (resultDocument.Property("artifacts") is not null)
{
    throw new InvalidOperationException(
        "The canonical result document must not contain top-level artifacts.");
}

if (resultDocument["result_bundle"]?["artifacts"] is null)
{
    throw new InvalidOperationException(
        "The canonical result document must contain result_bundle.artifacts.");
}

RoundTripJson<ResultBundle>(
    resultDocument["result_bundle"]!.ToString(),
    "result-bundle.schema.json");
RoundTripJson<SubmissionResponse>(
    """{"cancel_token":"cancel-token-1","submission_id":"submission-1","status":"accepted"}""",
    "submission-response.schema.json");
RoundTripJson<TrainingResponse>(
    """{"submission_id":"submission-1","status":"accepted"}""",
    "training-response.schema.json");
_ = ResultStatus.Cancelling;
_ = ResultStatus.Cancelled;

var replayLines = File.ReadLines(Path.Combine(fixtureDirectory, "navigation_default_replay_log.jsonl"));
var replayCount = 0;
foreach (var line in replayLines)
{
    RoundTripJson<ReplayLogStep>(line, "replay-log-step.schema.json");
    replayCount++;
}

if (replayCount != 2)
{
    throw new InvalidOperationException(
        $"The replay fixture must contain exactly two steps, but contained {replayCount}.");
}

ValidatePublicScenarioJson();
ValidatePublicReplayReaders();

Console.WriteLine(
    $"Validated canonical contracts, public persistence APIs, and {replayCount} replay steps.");
return 0;

T RoundTrip<T>(string filename, string schemaFilename)
{
    return RoundTripJson<T>(ReadFixture(filename), schemaFilename);
}

T RoundTripJson<T>(string json, string schemaFilename)
{
    var expectedJson = JToken.Parse(json);
    var settings = new JsonSerializerSettings
    {
        MissingMemberHandling = MissingMemberHandling.Error,
    };
    var value = JsonConvert.DeserializeObject<T>(json, settings)
        ?? throw new InvalidOperationException($"Could not deserialize {typeof(T).Name}.");
    var serialized = JsonConvert.SerializeObject(value);
    var actualJson = JToken.Parse(serialized);
    ContractJsonAssertions.AssertPreserved(
        expectedJson,
        actualJson,
        JObject.Parse(ReadSchema(schemaFilename)),
        typeof(T).Name);

    return JsonConvert.DeserializeObject<T>(serialized, settings)
        ?? throw new InvalidOperationException($"Could not deserialize round-tripped {typeof(T).Name}.");
}

void AssertTypes<T>(IEnumerable<T> values, params Type[] expectedTypes)
{
    var actualTypes = values.Select(value => value!.GetType()).ToArray();
    if (!actualTypes.SequenceEqual(expectedTypes))
    {
        throw new InvalidOperationException(
            $"Expected [{string.Join(", ", expectedTypes.Select(type => type.Name))}] " +
            $"but received [{string.Join(", ", actualTypes.Select(type => type.Name))}].");
    }
}

string ReadFixture(string filename)
{
    return File.ReadAllText(Path.Combine(fixtureDirectory, filename));
}

string ReadSchema(string filename)
{
    return File.ReadAllText(Path.Combine(schemaDirectory, filename));
}

void AssertSchemaDefaultValidation()
{
    var expected = JObject.Parse("{}");
    var schema = JObject.Parse(
        """{"type":"object","additionalProperties":false,"properties":{"count":{"type":"integer","default":1}}}""");
    ContractJsonAssertions.AssertPreserved(
        expected,
        JObject.Parse("""{"count":1}"""),
        schema,
        "SchemaDefaultProbe");
    AssertRejected(
        () => ContractJsonAssertions.AssertPreserved(
            expected,
            JObject.Parse("""{"count":2}"""),
            schema,
            "SchemaDefaultProbe"),
        "a value that differs from the canonical schema default");
    AssertRejected(
        () => ContractJsonAssertions.AssertPreserved(
            expected,
            JObject.Parse("""{"unknown":1}"""),
            schema,
            "SchemaDefaultProbe"),
        "an undeclared property");
}

void ValidatePublicScenarioJson()
{
    string json = ReadFixture("navigation_default_scenario_bundle.json");
    ScenarioBundle parsed = ScenarioBundleJson.Deserialize(json);
    if (parsed.ScenarioId != "navigation_default")
    {
        throw new InvalidOperationException("ScenarioBundleJson changed the scenario ID.");
    }

    ScenarioBundle reparsed = ScenarioBundleJson.Deserialize(
        ScenarioBundleJson.Serialize(parsed, indented: true));
    AssertTypes(
        reparsed.Sensors,
        typeof(ForwardCameraSensor),
        typeof(DistanceSensor));
}

void ValidatePublicReplayReaders()
{
    ReplayBundleManifest manifest = EmbodiedLabReplay.ReadManifest(
        Path.Combine(fixtureDirectory, "navigation_replay_bundle_manifest.json"));
    if (manifest.Chunks.Count != 2)
    {
        throw new InvalidOperationException("Replay manifest must contain two chunks.");
    }

    string replayPath = Path.Combine(
        fixtureDirectory,
        "navigation_default_replay_log.jsonl");
    IReadOnlyList<ReplayLogStep> plainSteps = EmbodiedLabReplay.ReadSteps(replayPath);
    IReadOnlyList<ReplayLogStep> parsedSteps = EmbodiedLabReplay.ParseSteps(
        File.ReadAllText(replayPath));
    if (plainSteps.Count != 2 || parsedSteps.Count != 2)
    {
        throw new InvalidOperationException("Replay readers must return two steps.");
    }

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

        IReadOnlyList<ReplayLogStep> compressedSteps =
            EmbodiedLabReplay.ReadSteps(gzipPath);
        if (compressedSteps.Count != 2)
        {
            throw new InvalidOperationException(
                "Compressed replay reader must return two steps.");
        }
    }
    finally
    {
        File.Delete(gzipPath);
    }
}

void AssertRejected(Action action, string description)
{
    try
    {
        action();
    }
    catch (InvalidOperationException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Canonical JSON validation accepted {description}.");
}
