using EmbodiedLab.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

var fixtureDirectory = args.Length == 1
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Tests~/Fixtures"));

var scenario = RoundTrip<ScenarioBundle>(
    "navigation_default_scenario_bundle.json",
    "scenario-bundle.v0");
RoundTrip<ResultDocument>("navigation_completed_result_document.json", "result-bundle.v0");
RoundTrip<ReplayBundleManifest>("navigation_replay_bundle_manifest.json", "jsonl.gz");

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

var resultDocument = JObject.Parse(ReadFixture("navigation_completed_result_document.json"));
RoundTripJson<ResultBundle>(resultDocument["result_bundle"]!.ToString(), "result-bundle.v0");
RoundTripJson<SubmissionResponse>(
    """{"submission_id":"submission-1","status":"accepted"}""",
    "accepted");

var replayLines = File.ReadLines(Path.Combine(fixtureDirectory, "navigation_default_replay_log.jsonl"));
var replayCount = 0;
foreach (var line in replayLines)
{
    RoundTripJson<ReplayLogStep>(line, "replay-log.v0");
    replayCount++;
}

if (replayCount == 0)
{
    throw new InvalidOperationException("The replay fixture must contain at least one step.");
}

Console.WriteLine($"Validated canonical contracts and {replayCount} replay steps.");
return 0;

T RoundTrip<T>(string filename, string expectedWireValue)
{
    return RoundTripJson<T>(ReadFixture(filename), expectedWireValue);
}

T RoundTripJson<T>(string json, string expectedWireValue)
{
    var settings = new JsonSerializerSettings
    {
        MissingMemberHandling = MissingMemberHandling.Error,
    };
    var value = JsonConvert.DeserializeObject<T>(json, settings)
        ?? throw new InvalidOperationException($"Could not deserialize {typeof(T).Name}.");
    var serialized = JsonConvert.SerializeObject(value);
    if (!serialized.Contains(expectedWireValue, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"{typeof(T).Name} did not preserve wire value '{expectedWireValue}'.");
    }

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
