using System.IO.Compression;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity;
using EmbodiedLab.Unity.Internal;
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
    typeof(GoalVectorSensor));
AssertTypes(
    scenario.Reward.Components,
    typeof(TerminalRewardComponent),
    typeof(DistanceDeltaRewardComponent),
    typeof(CollisionRewardComponent),
    typeof(PerStepRewardComponent),
    typeof(MinimumAbsoluteAngleRewardComponent),
    typeof(MinimumAbsoluteAngleRewardComponent),
    typeof(MaximumAbsoluteForwardRewardComponent));
_ = nameof(WorldSpec.StaticObstacles);
_ = nameof(ScenarioBundle.SchemaVersion);
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
ValidateReplayManifestLimits();
ValidateReplayLineLimit();
ValidateReplayDecompressionAndStepLimits();
ValidateReplayCancellation();
ValidateSemanticConstraints();

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
        typeof(GoalVectorSensor));
}

void ValidatePublicReplayReaders()
{
    ReplayBundleManifest manifest = EmbodiedLabReplay.ReadManifest(
        Path.Combine(fixtureDirectory, "navigation_replay_bundle_manifest.json"),
        "submission-1",
        "navigation_default");
    if (manifest.Chunks.Count != 2)
    {
        throw new InvalidOperationException("Replay manifest must contain two chunks.");
    }
    AssertThrows<InvalidDataException>(
        () => EmbodiedLabReplay.ReadManifest(
            Path.Combine(fixtureDirectory, "navigation_replay_bundle_manifest.json"),
            "other-job",
            "navigation_default"),
        "a Replay manifest for a different job");

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
    ReplayLogStep initialStep = plainSteps[0];
    if (initialStep.StepIndex != 0
        || initialStep.TimeSeconds != 0D
        || initialStep.Action.Values.Any(value => value.Value != 0D)
        || initialStep.Reward.Total != 0D
        || initialStep.Reward.Components.Count != 0
        || initialStep.Events.Count != 0
        || initialStep.Terminated)
    {
        throw new InvalidOperationException(
            "The first Replay step must represent the reset state before any action, reward, or event.");
    }

    string gzipPath = Path.Combine(
        Path.GetTempPath(),
        $"embodiedlab-replay-{Guid.NewGuid():N}.jsonl.gz");
    string trainGzipPath = Path.Combine(
        Path.GetTempPath(),
        $"embodiedlab-train-replay-{Guid.NewGuid():N}.jsonl.gz");
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

        var chunk = new EvalReplayBundleChunk
        {
            CheckpointStep = 0,
            Path = "eval/checkpoint.jsonl.gz",
            Format = EvalReplayBundleChunkFormat.JsonlGz,
            StepCount = 2,
            EpisodeCount = 1,
        };
        IReadOnlyList<ReplayLogStep> validatedSteps = EmbodiedLabReplay.ReadChunk(
            gzipPath,
            chunk,
            "submission-1",
            "navigation_default");
        if (validatedSteps.Count != 2)
        {
            throw new InvalidOperationException(
                "Identity-aware Replay reader must return two steps.");
        }

        AssertThrows<InvalidDataException>(
            () => EmbodiedLabReplay.ReadChunk(
                gzipPath,
                chunk,
                "submission-1",
                "other-scenario"),
            "a Replay chunk for a different Scenario");

        string[] trainLines = File.ReadAllLines(replayPath)
            .Select((line, index) =>
            {
                JObject step = JObject.Parse(line);
                step["phase"] = "train";
                step["policy_mode"] = "stochastic";
                step["checkpoint_step"] = index + 1;
                return step.ToString(Formatting.None);
            })
            .ToArray();
        using (var file = File.Create(trainGzipPath))
        using (var gzip = new GZipStream(file, CompressionMode.Compress))
        using (var writer = new StreamWriter(gzip))
        {
            writer.WriteLine(string.Join(Environment.NewLine, trainLines));
        }

        var trainChunk = new TrainReplayBundleChunk
        {
            CheckpointStep = 2,
            StartStep = 1,
            EndStep = 2,
            Path = "train/chunk.jsonl.gz",
            Format = TrainReplayBundleChunkFormat.JsonlGz,
            StepCount = 2,
        };
        IReadOnlyList<ReplayLogStep> trainSteps = EmbodiedLabReplay.ReadChunk(
            trainGzipPath,
            trainChunk,
            "submission-1",
            "navigation_default");
        if (trainSteps.Select(step => step.CheckpointStep).SequenceEqual(new[] { 1, 2 }) ==
            false)
        {
            throw new InvalidOperationException(
                "Training Replay reader must preserve its checkpoint range.");
        }

        trainChunk.StartStep = 0;
        AssertThrows<InvalidDataException>(
            () => EmbodiedLabReplay.ReadChunk(
                trainGzipPath,
                trainChunk,
                "submission-1",
                "navigation_default"),
            "a training Replay chunk that omits its declared start checkpoint");
    }
    finally
    {
        File.Delete(gzipPath);
        File.Delete(trainGzipPath);
    }
}

void ValidateReplayCancellation()
{
    string replay = File.ReadAllText(
        Path.Combine(fixtureDirectory, "navigation_default_replay_log.jsonl"));
    using var cancellation = new CancellationTokenSource();
    using var reader = new CancellingTextReader(replay, cancellation);
    AssertThrows<OperationCanceledException>(
        () => EmbodiedLabReplay.ReadSteps(
            reader,
            ReplayResourceLimits.Default,
            cancellation.Token),
        "Replay validation that ignores cancellation while reading");
}

void ValidateReplayManifestLimits()
{
    var oversizedChunks = new JArray();
    for (int index = 0; index < 4097; index++)
    {
        oversizedChunks.Add(JValue.CreateNull());
    }

    AssertManifestRejected(
        CreateReplayManifest(oversizedChunks),
        "more than 4,096 replay chunks");

    AssertManifestRejected(
        CreateReplayManifest(
            new JArray(CreateReplayChunk(new string('p', 1025), stepCount: 1))),
        "a replay chunk path longer than 1,024 characters");

    AssertManifestRejected(
        CreateReplayManifest(
            new JArray(CreateReplayChunk("eval/chunk.jsonl.gz", stepCount: 100001))),
        "a replay chunk declaring more than 100,000 steps");

    JObject oversizedManifest = CreateReplayManifest(new JArray());
    oversizedManifest["scenario_id"] = new string('s', (1024 * 1024) + 1);
    AssertManifestRejected(oversizedManifest, "a replay manifest larger than 1 MiB");
}

void ValidateReplayLineLimit()
{
    AssertThrows<InvalidDataException>(
        () => EmbodiedLabReplay.ParseSteps(new string(' ', (1024 * 1024) + 1)),
        "a JSONL line longer than 1 MiB");
}

void ValidateReplayDecompressionAndStepLimits()
{
    string gzipPath = Path.Combine(
        Path.GetTempPath(),
        $"embodiedlab-replay-limit-{Guid.NewGuid():N}.jsonl.gz");
    try
    {
        using (var file = File.Create(gzipPath))
        using (var gzip = new GZipStream(file, CompressionMode.Compress))
        using (var writer = new StreamWriter(gzip))
        {
            writer.WriteLine(new string(' ', 600));
            writer.WriteLine(new string(' ', 600));
        }

        AssertThrows<InvalidDataException>(
            () => EmbodiedLabReplay.ReadSteps(
                gzipPath,
                CreateReplayLimits(
                    maximumDecompressedBytes: 1024,
                    maximumLineBytes: 700,
                    maximumSteps: 10)),
            "a gzip replay expanding beyond its byte budget");
    }
    finally
    {
        File.Delete(gzipPath);
    }

    string replayPath = Path.Combine(
        fixtureDirectory,
        "navigation_default_replay_log.jsonl");
    AssertThrows<InvalidDataException>(
        () => EmbodiedLabReplay.ReadSteps(
            replayPath,
            CreateReplayLimits(
                maximumDecompressedBytes: 1024 * 1024,
                maximumLineBytes: 1024 * 1024,
                maximumSteps: 1)),
        "more replay steps than the configured total limit");
}

void ValidateSemanticConstraints()
{
    ResultDocument mismatchedResult = JsonConvert.DeserializeObject<ResultDocument>(
        ReadFixture("navigation_completed_result_document.json")) ??
        throw new InvalidOperationException("Could not read the completed result fixture.");
    mismatchedResult.Progress.Phase = ResultStatus.Running;
    AssertThrows<InvalidDataException>(
        () => ContractSemanticValidator.ValidateResultDocument(mismatchedResult),
        "a result whose status differs from progress.phase");

    ResultDocument wrongOpset = JsonConvert.DeserializeObject<ResultDocument>(
        ReadFixture("navigation_completed_result_document.json")) ??
        throw new InvalidOperationException("Could not read the completed result fixture.");
    wrongOpset.ResultBundle!.Artifacts!.OnnxModel!.OpsetVersion =
        (OnnxModelArtifactLocationOpsetVersion)18;
    AssertThrows<InvalidDataException>(
        () => ContractSemanticValidator.ValidateResultDocument(wrongOpset),
        "an ONNX model with a noncanonical opset");

    ResultDocument wrongActionMapping = JsonConvert.DeserializeObject<ResultDocument>(
        ReadFixture("navigation_completed_result_document.json")) ??
        throw new InvalidOperationException("Could not read the completed result fixture.");
    wrongActionMapping.ResultBundle!.Artifacts!.OnnxModel!.Output
        .ActionMapping["forward"] = "policy_forward";
    AssertThrows<InvalidDataException>(
        () => ContractSemanticValidator.ValidateResultDocument(wrongActionMapping),
        "an ONNX model with an unsupported action mapping");

    ResultDocument invalidMetrics = JsonConvert.DeserializeObject<ResultDocument>(
        ReadFixture("navigation_completed_result_document.json")) ??
        throw new InvalidOperationException("Could not read the completed result fixture.");
    invalidMetrics.ResultBundle!.Summary!.SuccessRate = double.NaN;
    AssertThrows<InvalidDataException>(
        () => ContractSemanticValidator.ValidateResultDocument(invalidMetrics),
        "a completed result with non-finite metrics");

    JObject replayStep = JObject.Parse(
        File.ReadLines(
            Path.Combine(fixtureDirectory, "navigation_default_replay_log.jsonl"))
            .First());
    replayStep["policy_mode"] = "stochastic";
    AssertThrows<InvalidDataException>(
        () => EmbodiedLabReplay.ParseSteps(replayStep.ToString(Formatting.None)),
        "an evaluation replay step with stochastic policy mode");

    JObject manifest = JObject.Parse(
        ReadFixture("navigation_replay_bundle_manifest.json"));
    JArray chunks = (JArray)manifest["chunks"]!;
    chunks[1]!["path"] = chunks[0]!["path"]!.Value<string>();
    AssertManifestRejected(manifest, "duplicate replay chunk paths");
}

ReplayResourceLimits CreateReplayLimits(
    long maximumDecompressedBytes,
    int maximumLineBytes,
    int maximumSteps)
{
    return new ReplayResourceLimits(
        maximumManifestBytes: 1024 * 1024,
        maximumManifestChunks: 4096,
        maximumChunkPathCharacters: 1024,
        maximumDeclaredChunkSteps: 100000,
        maximumDecompressedBytes,
        maximumLineBytes,
        maximumSteps);
}

JObject CreateReplayManifest(JArray chunks)
{
    return new JObject
    {
        ["schema_version"] = "replay-bundle.v0",
        ["job_id"] = "submission-1",
        ["scenario_id"] = "navigation_default",
        ["total_timesteps"] = 5000,
        ["chunks"] = chunks,
    };
}

JObject CreateReplayChunk(string path, int stepCount)
{
    return new JObject
    {
        ["phase"] = "eval",
        ["policy_mode"] = "deterministic",
        ["checkpoint_step"] = 5000,
        ["start_step"] = JValue.CreateNull(),
        ["end_step"] = JValue.CreateNull(),
        ["path"] = path,
        ["format"] = "jsonl.gz",
        ["size_bytes"] = 1,
        ["sha256"] = new string('0', 64),
        ["step_count"] = stepCount,
        ["episode_count"] = 1,
        ["success_rate"] = 1.0,
        ["avg_reward"] = 1.0,
        ["avg_steps"] = 1.0,
    };
}

void AssertManifestRejected(JObject manifest, string description)
{
    string path = Path.Combine(
        Path.GetTempPath(),
        $"embodiedlab-replay-manifest-{Guid.NewGuid():N}.json");
    try
    {
        File.WriteAllText(path, manifest.ToString(Formatting.None));
        AssertThrows<InvalidDataException>(
            () => EmbodiedLabReplay.ReadManifest(path),
            description);
    }
    finally
    {
        File.Delete(path);
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

void AssertThrows<TException>(Action action, string description)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Accepted {description}.");
}

sealed class CancellingTextReader : StringReader
{
    private readonly CancellationTokenSource cancellation;

    internal CancellingTextReader(
        string value,
        CancellationTokenSource cancellation)
        : base(value)
    {
        this.cancellation = cancellation;
    }

    public override int Read(char[] buffer, int index, int count)
    {
        int read = base.Read(buffer, index, count);
        cancellation.Cancel();
        return read;
    }
}
