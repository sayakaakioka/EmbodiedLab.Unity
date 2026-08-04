using EmbodiedLab.Contracts;
using EmbodiedLab.Unity;
using EmbodiedLab.Unity.Samples.Quickstart;
using Newtonsoft.Json;

var tests = new (string Name, Action Run)[]
{
    ("Queued progress text", TestProgressText),
    ("Safe submission directory", TestSafeSubmissionDirectory),
    ("Unsafe submission directory rejection", TestUnsafeSubmissionDirectory),
    ("Latest deterministic evaluation chunk", TestReplayChunkSelection),
    ("Missing deterministic evaluation chunk", TestMissingReplayChunk),
    ("Selected replay chunk rows", TestSelectedReplayChunkRows),
    ("Replay episode boundary", TestReplayEpisodeBoundary),
    ("Replay non-consecutive step clock", TestReplayNonConsecutiveStepClock),
    ("Replay playback clock", TestReplayPlaybackClock),
    ("Replay stop reset", TestReplayStopReset),
    ("Replay player stop reset", TestReplayPlayerStopReset),
    ("Invalid replay values", TestInvalidReplayValues),
    ("Replay local paths", TestReplayLocalPaths),
    ("ONNX contract accepted shapes", TestOnnxContractAcceptedShapes),
    ("ONNX contract rejected metadata", TestOnnxContractRejectedMetadata),
    ("Goal observation", TestGoalObservation),
    ("RGB vertical flip and CHW conversion", TestImageConversion),
    ("Action contract", TestActionContract),
};

foreach ((string name, Action run) in tests)
{
    run();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"Validated {tests.Length} Quickstart behaviors.");
return 0;

static void TestProgressText()
{
    AssertEqual(
        "-",
        QuickstartProgressText.Format(ResultStatus.Queued, null),
        "missing progress");

    var queued = new Progress
    {
        Phase = ResultStatus.Queued,
        CurrentStep = 0,
        TotalSteps = 0,
        Message = "Queued",
    };
    AssertEqual(
        "Waiting for the trainer to start.",
        QuickstartProgressText.Format(ResultStatus.Queued, queued),
        "queued progress");

    var running = new Progress
    {
        Phase = ResultStatus.Running,
        CurrentStep = 12,
        TotalSteps = 100,
        Message = "Training",
    };
    AssertEqual(
        "Running: 12/100 Training",
        QuickstartProgressText.Format(ResultStatus.Running, running),
        "running progress");
}

static void TestSafeSubmissionDirectory()
{
    string root = Path.Combine(Path.GetTempPath(), "embodiedlab-path-test");
    string actual = QuickstartLocalPaths.GetSubmissionDirectory(root, "submission-123");
    string expected = Path.GetFullPath(
        Path.Combine(root, "EmbodiedLabQuickstart", "submission-123"));

    AssertEqual(expected, actual, "safe submission directory");
}

static void TestUnsafeSubmissionDirectory()
{
    string root = Path.Combine(Path.GetTempPath(), "embodiedlab-path-test");
    foreach (string submissionId in new[]
             {
                 "../escape",
                 "..\\escape",
                 ".",
                 "..",
                 Path.GetPathRoot(root) ?? "C:\\",
             })
    {
        AssertThrows<InvalidDataException>(
            () => QuickstartLocalPaths.GetSubmissionDirectory(root, submissionId));
    }
}

static void TestReplayChunkSelection()
{
    ReplayBundleManifest manifest = EmbodiedLabReplay.ReadManifest(
        FixturePath("navigation_replay_bundle_manifest.json"));

    EvalReplayBundleChunk canonical =
        QuickstartReplayTimeline.SelectLatestDeterministicEvaluationChunk(manifest);

    AssertEqual(5000, canonical.CheckpointStep, "canonical replay checkpoint");
    AssertEqual(
        "eval/checkpoint_00005000.jsonl.gz",
        canonical.Path,
        "canonical replay chunk");

    var latest = new EvalReplayBundleChunk
    {
        PolicyMode = EvalReplayBundleChunkPolicyMode.Deterministic,
        CheckpointStep = 6000,
        Path = "eval/checkpoint_00006000.jsonl.gz",
        Format = EvalReplayBundleChunkFormat.JsonlGz,
        StepCount = 1,
    };
    manifest.Chunks.Add(latest);

    AssertEqual(
        latest,
        QuickstartReplayTimeline.SelectLatestDeterministicEvaluationChunk(manifest),
        "latest deterministic evaluation chunk");
}

static void TestMissingReplayChunk()
{
    var manifest = new ReplayBundleManifest
    {
        JobId = "submission-1",
        ScenarioId = "scenario-1",
        Chunks = new List<ReplayBundleChunk>
        {
            new TrainReplayBundleChunk
            {
                PolicyMode = TrainReplayBundleChunkPolicyMode.Stochastic,
                CheckpointStep = 1,
                Path = "train/chunk.jsonl.gz",
                Format = TrainReplayBundleChunkFormat.JsonlGz,
                StepCount = 1,
            },
        },
    };

    AssertThrows<InvalidOperationException>(
        () => QuickstartReplayTimeline.SelectLatestDeterministicEvaluationChunk(manifest));
}

static void TestSelectedReplayChunkRows()
{
    ReplayBundleManifest manifest = EmbodiedLabReplay.ReadManifest(
        FixturePath("navigation_replay_bundle_manifest.json"));
    EvalReplayBundleChunk selectedChunk =
        QuickstartReplayTimeline.SelectLatestDeterministicEvaluationChunk(manifest);
    IReadOnlyList<ReplayLogStep> steps = EmbodiedLabReplay.ReadSteps(
        FixturePath("navigation_default_replay_log.jsonl"));
    SetCheckpoint(steps, selectedChunk.CheckpointStep);

    EmbodiedLabReplay.ValidateChunkSteps(
        selectedChunk,
        steps,
        manifest.JobId,
        manifest.ScenarioId);
    QuickstartReplayTimeline.ValidateSelectedChunkSteps(
        manifest.JobId,
        manifest.ScenarioId,
        selectedChunk,
        steps);

    AssertInvalid((chunk, rows) => rows[0].JobId = "other-submission");
    AssertInvalid((chunk, rows) => rows[0].ScenarioId = "other-scenario");
    AssertInvalid((chunk, rows) => rows[0].Phase = ReplayLogStepPhase.Train);
    AssertInvalid(
        (chunk, rows) => rows[0].PolicyMode = ReplayLogStepPolicyMode.Stochastic);
    AssertInvalid(
        (chunk, rows) => rows[0].CheckpointStep = chunk.CheckpointStep - 1);
    AssertInvalid((chunk, rows) => chunk.StepCount++);
    AssertInvalid((chunk, rows) => rows[1].EpisodeId = "unexpected-episode");

    static void AssertInvalid(
        Action<EvalReplayBundleChunk, IReadOnlyList<ReplayLogStep>> mutate)
    {
        ReplayBundleManifest candidateManifest = EmbodiedLabReplay.ReadManifest(
            FixturePath("navigation_replay_bundle_manifest.json"));
        EvalReplayBundleChunk candidateChunk =
            QuickstartReplayTimeline.SelectLatestDeterministicEvaluationChunk(
                candidateManifest);
        IReadOnlyList<ReplayLogStep> candidateSteps = EmbodiedLabReplay.ReadSteps(
            FixturePath("navigation_default_replay_log.jsonl"));
        SetCheckpoint(candidateSteps, candidateChunk.CheckpointStep);
        mutate(candidateChunk, candidateSteps);

        AssertThrows<InvalidDataException>(
            () => EmbodiedLabReplay.ValidateChunkSteps(
                candidateChunk,
                candidateSteps,
                candidateManifest.JobId,
                candidateManifest.ScenarioId));
    }

    static void SetCheckpoint(IReadOnlyList<ReplayLogStep> rows, int checkpointStep)
    {
        foreach (ReplayLogStep row in rows)
        {
            row.CheckpointStep = checkpointStep;
        }
    }
}

static void TestReplayEpisodeBoundary()
{
    ReplayLogStep first = CreateReplayStep("episode-1", 0, 0D, 0D, 0D, 0D);
    ReplayLogStep sameEpisode = CreateReplayStep("episode-1", 1, 0.1D, 1D, 0D, 10D);
    ReplayLogStep nextEpisode = CreateReplayStep("episode-2", 0, 0D, 2D, 0D, 20D);

    AssertEqual(
        false,
        QuickstartReplayTimeline.IsEpisodeBoundary(first, sameEpisode),
        "same episode boundary");
    AssertEqual(
        true,
        QuickstartReplayTimeline.IsEpisodeBoundary(sameEpisode, nextEpisode),
        "next episode boundary");
    ReplayLogStep skippedStep = CreateReplayStep("episode-1", 3, 0.3D, 3D, 0D, 30D);
    AssertEqual(
        false,
        QuickstartReplayTimeline.CanInterpolate(sameEpisode, skippedStep),
        "non-consecutive steps");
    AssertEqual(
        true,
        QuickstartReplayTimeline.CanInterpolate(first, sameEpisode),
        "consecutive same-episode steps");

    var timeline = new QuickstartReplayTimeline(
        new[] { first, sameEpisode, nextEpisode });
    timeline.Play();
    QuickstartReplayFrame pauseFrame = timeline.Advance(0.1D);
    AssertEqual(true, timeline.IsEpisodePause, "episode pause active");
    AssertEqual(1, pauseFrame.FromStep.StepIndex, "episode pause step");

    QuickstartReplayFrame nextEpisodeFrame = timeline.Advance(
        QuickstartReplayTimeline.EpisodePauseSeconds);
    AssertEqual("episode-2", nextEpisodeFrame.FromStep.EpisodeId, "next episode");
    AssertEqual(0, nextEpisodeFrame.FromStep.StepIndex, "next episode first step");
}

static void TestReplayPlaybackClock()
{
    IReadOnlyList<ReplayLogStep> canonicalSteps = EmbodiedLabReplay.ReadSteps(
        FixturePath("navigation_default_replay_log.jsonl"));
    var timeline = new QuickstartReplayTimeline(canonicalSteps);

    timeline.Play();
    QuickstartReplayFrame frame = timeline.Advance(0.025D);

    AssertEqual(0, frame.FromStep.StepIndex, "clock from step");
    AssertEqual(1, frame.ToStep.StepIndex, "clock to step");
    AssertNear(0.25D, frame.Interpolation, "clock interpolation");
    AssertNear(-5.995D, frame.Interpolate(-6D, -5.98D), "interpolated x");
}

static void TestReplayNonConsecutiveStepClock()
{
    var timeline = new QuickstartReplayTimeline(
        new[]
        {
            CreateReplayStep("episode-1", 0, 0D, 0D, 0D, 0D),
            CreateReplayStep("episode-1", 2, 1D, 2D, 0D, 20D),
        });

    timeline.Play();
    QuickstartReplayFrame held = timeline.Advance(0.25D);
    AssertEqual(0, held.FromStep.StepIndex, "gap holds previous step");
    AssertEqual(0, held.ToStep.StepIndex, "gap does not interpolate");

    QuickstartReplayFrame snapped = timeline.Advance(0.75D);
    AssertEqual(2, snapped.FromStep.StepIndex, "gap snaps at next timestamp");
}

static void TestReplayStopReset()
{
    var timeline = new QuickstartReplayTimeline(
        new[]
        {
            CreateReplayStep("episode-1", 0, 0D, 0D, 0D, 350D),
            CreateReplayStep("episode-1", 1, 1D, 1D, 0D, 10D),
        });

    timeline.Play();
    QuickstartReplayFrame playing = timeline.Advance(0.5D);
    AssertNear(360D, playing.InterpolateAngleDegrees(350D, 10D), "short yaw path");
    AssertNear(
        0D,
        new QuickstartReplayFrame(
            CreateReplayStep("episode-1", 0, 0D, 0D, 0D, 720D),
            CreateReplayStep("episode-1", 1, 1D, 0D, 0D, 0D),
            0.5D).InterpolateAngleDegrees(720D, 0D),
        "unnormalized yaw path");

    QuickstartReplayFrame stopped = timeline.Stop();
    AssertEqual(false, timeline.IsPlaying, "stopped replay");
    AssertEqual(0, stopped.FromStep.StepIndex, "reset step");
    AssertNear(0D, stopped.Interpolation, "reset interpolation");
}

static void TestReplayPlayerStopReset()
{
    var robotObject = new UnityEngine.GameObject("Robot");
    robotObject.transform.position = new UnityEngine.Vector3(0F, 0.5F, 0F);
    var player = new QuickstartReplayPlayer();
    player.Load(
        robotObject.transform,
        new[]
        {
            CreateReplayStep("episode-1", 0, 0D, 1D, 2D, 0D),
            CreateReplayStep("episode-1", 1, 1D, 3D, 4D, 90D),
        },
        "eval/chunk.jsonl.gz");

    player.Play();
    player.Tick(0.5D);
    AssertNear(2D, robotObject.transform.position.x, "player moved robot x");
    AssertNear(3D, robotObject.transform.position.z, "player moved robot z");

    player.Stop();
    AssertNear(1D, robotObject.transform.position.x, "player reset robot x");
    AssertNear(2D, robotObject.transform.position.z, "player reset robot z");
    AssertNear(0.5D, robotObject.transform.position.y, "player retained robot height");
}

static void TestInvalidReplayValues()
{
    ReplayLogStep invalid = CreateReplayStep(
        "episode-1",
        0,
        0D,
        double.NaN,
        0D,
        0D);
    AssertThrows<ArgumentException>(
        () => new QuickstartReplayTimeline(new[] { invalid }));
    ReplayLogStep tooLarge = CreateReplayStep(
        "episode-1",
        0,
        0D,
        double.MaxValue,
        0D,
        0D);
    AssertThrows<ArgumentException>(
        () => new QuickstartReplayTimeline(new[] { tooLarge }));
}

static void TestReplayLocalPaths()
{
    string root = Path.Combine(Path.GetTempPath(), "embodiedlab-replay-path-test");
    string manifestPath = QuickstartLocalPaths.GetReplayManifestPath(root, "submission-1");
    string modelPath = QuickstartLocalPaths.GetModelPath(root, "submission-1");
    string chunkPath = QuickstartLocalPaths.GetReplayChunkPath(
        root,
        "submission-1",
        "eval/checkpoint_00005000.jsonl.gz");

    AssertEqual(
        Path.GetFullPath(
            Path.Combine(
                root,
                "EmbodiedLabQuickstart",
                "submission-1",
                "replay",
                "manifest.json")),
        manifestPath,
        "replay manifest path");
    AssertEqual(
        Path.GetFullPath(
            Path.Combine(
                root,
                "EmbodiedLabQuickstart",
                "submission-1",
                "policy.onnx")),
        modelPath,
        "model path");
    AssertEqual(
        Path.GetFullPath(
            Path.Combine(
                root,
                "EmbodiedLabQuickstart",
                "submission-1",
                "replay",
                "eval",
                "checkpoint_00005000.jsonl.gz")),
        chunkPath,
        "replay chunk path");

    foreach (string unsafePath in new[]
             {
                 "../escape.jsonl.gz",
                 "eval/../escape.jsonl.gz",
                 "eval\\escape.jsonl.gz",
                 "/absolute.jsonl.gz",
                 "eval/file.jsonl.gz?query=1",
                 "eval/C:drive.jsonl.gz",
             })
    {
        AssertThrows<InvalidDataException>(
            () => QuickstartLocalPaths.GetReplayChunkPath(
                root,
                "submission-1",
                unsafePath));
    }
}

static void TestOnnxContractAcceptedShapes()
{
    ScenarioBundle scenario = CanonicalScenario();
    OnnxModelArtifactLocation model = CanonicalOnnxModel();
    ModelInput image = model.Inputs.Single(input => input.Name == "obs_0");
    ModelInput numeric = model.Inputs.Single(input => input.Name == "obs_1");
    image.Shape = new List<int> { 3, 84, 112 };
    numeric.Shape = new List<int> { 2 };
    QuickstartOnnxContract unbatched = QuickstartOnnxContract.Validate(
        scenario,
        model,
        new[]
        {
            Tensor("obs_0", true, 3, 84, 112),
            Tensor("obs_1", true, 2),
        },
        new[] { Tensor("action", true, 2) });
    AssertEqual(3, unbatched.ImageDimensions.Length, "unbatched image rank");
    AssertEqual(1, unbatched.NumericDimensions.Length, "unbatched numeric rank");
    AssertEqual("action", unbatched.OutputName, "output name");

    scenario = CanonicalScenario();
    model = CanonicalOnnxModel();
    QuickstartOnnxContract batched = QuickstartOnnxContract.Validate(
        scenario,
        model,
        new[]
        {
            Tensor("obs_1", true, -1, 2),
            Tensor("obs_0", true, -1, 3, 84, 112),
        },
        new[] { Tensor("action", true, -1, 2) });
    AssertEqual(1, batched.ImageDimensions[0], "resolved image batch");
    AssertEqual(1, batched.NumericDimensions[0], "resolved numeric batch");

    ForwardCameraSensor camera = scenario.Sensors.OfType<ForwardCameraSensor>().Single();
    camera.Width = 64;
    model.Inputs.Single(input => input.Name == "obs_0").Shape =
        new List<int> { -1, 3, 84, 64 };
    QuickstartOnnxContract resized = QuickstartOnnxContract.Validate(
        scenario,
        model,
        new[]
        {
            Tensor("obs_1", true, -1, 2),
            Tensor("obs_0", true, -1, 3, 84, 64),
        },
        new[] { Tensor("action", true, -1, 2) });
    AssertEqual(64, resized.ImageWidth, "Scenario-driven image width");
    AssertEqual(3 * 84 * 64, resized.ImageValueCount, "Scenario-driven image size");
}

static void TestOnnxContractRejectedMetadata()
{
    ScenarioBundle scenario = CanonicalScenario();
    OnnxModelArtifactLocation model = CanonicalOnnxModel();
    AssertThrows<InvalidDataException>(
        () => QuickstartOnnxContract.Validate(
            scenario,
            model,
            new[]
            {
                Tensor("obs_0", true, 3, 84, 112),
                Tensor("obs_1", true, 2),
                Tensor("extra", true, 1),
            },
            new[] { Tensor("action", true, 2) }));
    AssertThrows<InvalidDataException>(
        () => QuickstartOnnxContract.Validate(
            scenario,
            model,
            new[]
            {
                Tensor("obs_0", true, -1, 3, 84, 111),
                Tensor("obs_1", true, -1, 2),
            },
            new[] { Tensor("action", true, 2) }));
    AssertThrows<InvalidDataException>(
        () => QuickstartOnnxContract.Validate(
            scenario,
            model,
            new[]
            {
                Tensor("obs_0", false, -1, 3, 84, 112),
                Tensor("obs_1", true, -1, 2),
            },
            new[] { Tensor("action", true, 2) }));
    AssertThrows<InvalidDataException>(
        () => QuickstartOnnxContract.Validate(
            scenario,
            model,
            new[]
            {
                Tensor("obs_0", true, -1, 3, 84, 112),
                Tensor("obs_1", true, -1, 2),
            },
            new[] { Tensor("action", false, 2) }));
    AssertThrows<InvalidDataException>(
        () => QuickstartOnnxContract.Validate(
            scenario,
            model,
            new[]
            {
                Tensor("obs_0", true, -1, 3, 84, 112),
                Tensor("obs_1", true, -1, 2),
            },
            new[] { Tensor("action", true, 1) }));
    AssertThrows<InvalidDataException>(
        () => QuickstartOnnxContract.Validate(
            scenario,
            model,
            new[]
            {
                Tensor("obs_0", true, -1, 3, 84, 112),
                Tensor("obs_1", true, -1, 2),
            },
            new[] { Tensor("action", true, -1, 3) }));
    AssertThrows<InvalidDataException>(
        () => QuickstartOnnxContract.Validate(
            scenario,
            model,
            new[]
            {
                Tensor("obs_0", true, -1, 3, 84, 112),
                Tensor("obs_1", true, -1, 2),
            },
            new[]
            {
                Tensor("action", true, -1, 2),
                Tensor("extra", true, 1),
            }));

    model = CanonicalOnnxModel();
    model.Output.ActionMapping["forward"] = "policy_forward";
    AssertThrows<InvalidDataException>(
        () => QuickstartOnnxContract.Validate(
            scenario,
            model,
            new[]
            {
                Tensor("obs_0", true, -1, 3, 84, 112),
                Tensor("obs_1", true, -1, 2),
            },
            new[] { Tensor("action", true, -1, 2) }));

    model = CanonicalOnnxModel();
    model.OpsetVersion = (OnnxModelArtifactLocationOpsetVersion)18;
    AssertThrows<InvalidDataException>(
        () => QuickstartOnnxContract.Validate(
            scenario,
            model,
            new[]
            {
                Tensor("obs_0", true, -1, 3, 84, 112),
                Tensor("obs_1", true, -1, 2),
            },
            new[] { Tensor("action", true, 2) }));
}

static void TestGoalObservation()
{
    var values = new float[2];
    QuickstartInferenceMath.WriteNumericObservation(
        new UnityEngine.Vector3(0f, 0f, 0f),
        170f,
        new UnityEngine.Vector3(-1f, 0f, -1f),
        new[] { Values.GoalAngleDegrees, Values.GoalDistanceMeters },
        values);
    AssertNear(55D, values[0], "signed relative goal angle");
    AssertNear(Math.Sqrt(2D), values[1], "goal distance");

    QuickstartInferenceMath.WriteNumericObservation(
        new UnityEngine.Vector3(0f, 0f, 0f),
        -170f,
        new UnityEngine.Vector3(1f, 0f, -1f),
        new[] { Values.GoalAngleDegrees, Values.GoalDistanceMeters },
        values);
    AssertNear(-55D, values[0], "wrapped negative goal angle");
}

static void TestImageConversion()
{
    var source = new[]
    {
        new UnityEngine.Color32(255, 0, 0, 255),
        new UnityEngine.Color32(0, 255, 0, 255),
        new UnityEngine.Color32(0, 0, 255, 255),
        new UnityEngine.Color32(255, 255, 255, 255),
    };
    var destination = new float[12];
    QuickstartInferenceMath.ConvertRgbToVerticallyFlippedChw(
        source,
        2,
        2,
        new[]
        {
            "channel_0_unused",
            "channel_1_traversable",
            "channel_2_blocked_or_background",
        },
        destination);

    float[] expected =
    {
        0f, 1f, 1f, 0f,
        0f, 1f, 0f, 1f,
        1f, 1f, 0f, 0f,
    };
    for (int index = 0; index < expected.Length; index++)
    {
        AssertNear(expected[index], destination[index], $"CHW value {index}");
    }
}

static void TestActionContract()
{
    QuickstartAppliedAction valid = QuickstartInferenceMath.ApplyActionContract(
        new QuickstartRawAction(0.25f, -0.5f));
    AssertEqual(false, valid.ContractViolation, "valid action contract");
    AssertNear(0.25D, valid.Forward, "valid forward action");
    AssertNear(-0.5D, valid.Turn, "valid turn action");

    QuickstartAppliedAction clamped = QuickstartInferenceMath.ApplyActionContract(
        new QuickstartRawAction(-2f, 3f));
    AssertEqual(true, clamped.ContractViolation, "invalid action contract");
    AssertNear(0D, clamped.Forward, "clamped forward action");
    AssertNear(1D, clamped.Turn, "clamped turn action");
    if (!clamped.FormatSummary().Contains("CONTRACT VIOLATION", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Action violation summary is not visible.");
    }

    AssertThrows<InvalidDataException>(
        () => QuickstartInferenceMath.ApplyActionContract(
            new QuickstartRawAction(float.NaN, 0f)));
}

static ScenarioBundle CanonicalScenario()
{
    return JsonConvert.DeserializeObject<ScenarioBundle>(
        File.ReadAllText(FixturePath("navigation_default_scenario_bundle.json"))) ??
        throw new InvalidDataException("Canonical Scenario fixture is empty.");
}

static OnnxModelArtifactLocation CanonicalOnnxModel()
{
    ResultDocument result = JsonConvert.DeserializeObject<ResultDocument>(
        File.ReadAllText(FixturePath("navigation_completed_result_document.json"))) ??
        throw new InvalidDataException("Canonical Result fixture is empty.");
    return result.ResultBundle?.Artifacts?.OnnxModel ??
        throw new InvalidDataException("Canonical Result fixture has no ONNX model.");
}

static QuickstartTensorMetadata Tensor(
    string name,
    bool isFloat,
    params int[] dimensions)
{
    return new QuickstartTensorMetadata(name, isFloat, dimensions);
}

static ReplayLogStep CreateReplayStep(
    string episodeId,
    int stepIndex,
    double timeSeconds,
    double x,
    double z,
    double yaw)
{
    return new ReplayLogStep
    {
        JobId = "submission-1",
        ScenarioId = "scenario-1",
        EpisodeId = episodeId,
        StepIndex = stepIndex,
        TimeSeconds = timeSeconds,
        Phase = ReplayLogStepPhase.Eval,
        PolicyMode = ReplayLogStepPolicyMode.Deterministic,
        Robot = new ReplayRobotState
        {
            Position = new ReplayPosition { X = x, Z = z },
            RotationYDegrees = yaw,
        },
    };
}

static string FixturePath(string filename)
{
    return Path.Combine(AppContext.BaseDirectory, "Fixtures", filename);
}

static void AssertEqual<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"Expected {description} to be '{expected}', but received '{actual}'.");
    }
}
static void AssertThrows<TException>(Action action)
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

    throw new InvalidOperationException(
        $"Expected {typeof(TException).Name}, but no matching exception was thrown.");
}

static void AssertNear(double expected, double actual, string description)
{
    if (Math.Abs(expected - actual) > 0.000001D)
    {
        throw new InvalidOperationException(
            $"Expected {description} to be '{expected}', but received '{actual}'.");
    }
}
