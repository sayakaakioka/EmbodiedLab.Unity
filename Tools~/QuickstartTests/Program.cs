using EmbodiedLab.Contracts;
using EmbodiedLab.Unity;
using EmbodiedLab.Unity.Samples.Quickstart;

var tests = new (string Name, Action Run)[]
{
    ("History round trip", TestRoundTrip),
    ("Newest-first ordering", TestNewestFirst),
    ("Existing record update", TestUpdate),
    ("Selection data", TestSelectionData),
    ("Terminal token clearing", TestTerminalTokenClearing),
    ("Record-only removal", TestRecordOnlyRemoval),
    ("Failed persistence keeps store unchanged", TestFailedPersistence),
    ("Interrupted save recovery", TestInterruptedSaveRecovery),
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
};

foreach ((string name, Action run) in tests)
{
    run();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"Validated {tests.Length} Quickstart behaviors.");
return 0;

static void TestRoundTrip()
{
    WithStore((directory, store) =>
    {
        QuickstartHistoryRecord expected = CreateRecord(
            "submission-1",
            "2026-07-15T01:02:03.0000000+00:00");
        expected.CancelToken = "secret-capability";
        expected.Progress = new Progress
        {
            Phase = ResultStatus.Running,
            CurrentStep = 12,
            TotalSteps = 100,
            Message = "Training",
        };
        expected.LocalReplayManifestPath = Path.Combine(directory, "manifest.json");
        expected.LocalReplayChunkPath = Path.Combine(directory, "chunk.jsonl.gz");
        expected.LocalOnnxPath = Path.Combine(directory, "policy.onnx");
        store.Upsert(expected);

        var reloaded = new QuickstartHistoryStore(
            Path.Combine(directory, "job-history.json"));
        reloaded.Load();
        QuickstartHistoryRecord actual = reloaded.Records.Single();

        AssertEqual(expected.SubmissionId, actual.SubmissionId, "submission ID");
        AssertEqual(expected.SubmittedAtUtc, actual.SubmittedAtUtc, "submission time");
        AssertEqual(expected.ApiBaseUrl, actual.ApiBaseUrl, "API endpoint");
        AssertEqual(
            expected.ResultWebSocketBaseUrl,
            actual.ResultWebSocketBaseUrl,
            "WebSocket endpoint");
        AssertEqual(expected.ScenarioJson, actual.ScenarioJson, "scenario JSON");
        AssertEqual(expected.CancelToken, actual.CancelToken, "cancel token");
        AssertEqual(12, actual.Progress?.CurrentStep, "progress step");
        AssertEqual(expected.LocalOnnxPath, actual.LocalOnnxPath, "ONNX path");
        AssertEqual(
            expected.LocalReplayManifestPath,
            actual.LocalReplayManifestPath,
            "manifest path");
        AssertEqual(
            expected.LocalReplayChunkPath,
            actual.LocalReplayChunkPath,
            "chunk path");
        AssertEqual(false, File.Exists(Path.Combine(directory, "job-history.json.tmp")), "temp file");
    });
}

static void TestNewestFirst()
{
    WithStore((_, store) =>
    {
        store.Upsert(CreateRecord("older", "2026-07-15T01:00:00.0000000+00:00"));
        store.Upsert(CreateRecord("newer", "2026-07-15T02:00:00.0000000+00:00"));

        AssertEqual("newer", store.Records[0].SubmissionId, "newest record");
        AssertEqual("older", store.Records[1].SubmissionId, "oldest record");
    });
}

static void TestUpdate()
{
    WithStore((directory, store) =>
    {
        QuickstartHistoryRecord original = CreateRecord(
            "submission-1",
            "2026-07-15T01:00:00.0000000+00:00");
        store.Upsert(original);
        QuickstartHistoryRecord replacement = CreateRecord(
            "submission-1",
            "2026-07-15T01:00:00.0000000+00:00");
        replacement.Status = ResultStatus.Running;
        store.Upsert(replacement);

        var reloaded = new QuickstartHistoryStore(
            Path.Combine(directory, "job-history.json"));
        reloaded.Load();

        AssertEqual(1, reloaded.Records.Count, "record count");
        AssertEqual(ResultStatus.Running, reloaded.Records[0].Status, "updated status");
        AssertEqual(replacement, store.Records[0], "replacement record instance");
    });
}

static void TestSelectionData()
{
    WithStore((_, store) =>
    {
        QuickstartHistoryRecord expected = CreateRecord(
            "submission-1",
            "2026-07-15T01:00:00.0000000+00:00");
        expected.CancelToken = "selection-capability";
        store.Upsert(expected);

        QuickstartHistoryRecord? selected = store.Find("submission-1");

        AssertEqual(expected, selected, "selected record");
        AssertEqual("selection-capability", selected?.CancelToken, "selected capability");
        AssertEqual("{\"scenario_id\":\"scenario-1\"}", selected?.ScenarioJson, "selected scenario");
    });
}

static void TestTerminalTokenClearing()
{
    foreach (ResultStatus terminalStatus in new[]
             {
                 ResultStatus.Completed,
                 ResultStatus.Failed,
                 ResultStatus.Cancelled,
             })
    {
        WithStore((directory, store) =>
        {
            QuickstartHistoryRecord record = CreateRecord(
                $"submission-{terminalStatus}",
                "2026-07-15T01:00:00.0000000+00:00");
            record.CancelToken = "terminal-capability";
            record.ApplyResult(new ResultDocument
            {
                SubmissionId = record.SubmissionId,
                Status = terminalStatus,
            });
            store.Upsert(record);

            string historyPath = Path.Combine(directory, "job-history.json");
            string historyJson = File.ReadAllText(historyPath);
            var reloaded = new QuickstartHistoryStore(historyPath);
            reloaded.Load();

            AssertEqual(null, reloaded.Records[0].CancelToken, "terminal cancel token");
            AssertEqual(terminalStatus, reloaded.Records[0].Status, "terminal status");
            AssertEqual(false, historyJson.Contains("cancel_token"), "serialized token field");
        });
    }
}

static void TestRecordOnlyRemoval()
{
    WithStore((directory, store) =>
    {
        string modelPath = Path.Combine(directory, "policy.onnx");
        string replayPath = Path.Combine(directory, "chunk.jsonl.gz");
        File.WriteAllText(modelPath, "model");
        File.WriteAllText(replayPath, "replay");
        QuickstartHistoryRecord record = CreateRecord(
            "submission-1",
            "2026-07-15T01:00:00.0000000+00:00");
        record.LocalOnnxPath = modelPath;
        record.LocalReplayChunkPath = replayPath;
        store.Upsert(record);

        bool removed = store.Remove(record.SubmissionId);

        AssertEqual(true, removed, "record removed");
        AssertEqual(0, store.Records.Count, "remaining record count");
        AssertEqual(true, File.Exists(modelPath), "model preserved");
        AssertEqual(true, File.Exists(replayPath), "replay preserved");
    });
}

static void TestFailedPersistence()
{
    WithStore((directory, store) =>
    {
        QuickstartHistoryRecord original = CreateRecord(
            "submission-1",
            "2026-07-15T01:00:00.0000000+00:00");
        store.Upsert(original);

        string blockingParent = Path.Combine(directory, "blocking-file");
        File.WriteAllText(blockingParent, "not a directory");
        var failingStore = new QuickstartHistoryStore(
            Path.Combine(blockingParent, "job-history.json"));
        failingStore.Load();

        AssertThrows<IOException>(() => failingStore.Upsert(original));
        AssertEqual(0, failingStore.Records.Count, "records after failed upsert");

        Directory.Delete(directory, recursive: true);
        File.WriteAllText(directory, "not a directory");
        AssertThrows<IOException>(() => store.Remove(original.SubmissionId));
        AssertEqual(1, store.Records.Count, "records after failed removal");
        File.Delete(directory);
        Directory.CreateDirectory(directory);
    });
}

static void TestInterruptedSaveRecovery()
{
    WithStore((directory, store) =>
    {
        QuickstartHistoryRecord older = CreateRecord(
            "submission-1",
            "2026-07-15T01:00:00.0000000+00:00");
        store.Upsert(older);

        string candidatePath = Path.Combine(directory, "candidate-history.json");
        var candidateStore = new QuickstartHistoryStore(candidatePath);
        candidateStore.Load();
        candidateStore.Upsert(older);
        candidateStore.Upsert(
            CreateRecord(
                "submission-2",
                "2026-07-15T02:00:00.0000000+00:00"));

        string historyPath = Path.Combine(directory, "job-history.json");
        string temporaryPath = historyPath + ".tmp";
        File.Copy(candidatePath, temporaryPath);
        var recoveredStore = new QuickstartHistoryStore(historyPath);
        recoveredStore.Load();

        AssertEqual(2, recoveredStore.Records.Count, "recovered record count");
        AssertEqual("submission-2", recoveredStore.Records[0].SubmissionId, "recovered job");
        AssertEqual(false, File.Exists(temporaryPath), "promoted temp file");

        File.WriteAllText(temporaryPath, "{not-json");
        recoveredStore.Load();

        AssertEqual(2, recoveredStore.Records.Count, "records after corrupt temp");
        AssertEqual(false, File.Exists(temporaryPath), "corrupt temp file");
    });
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

    ReplayBundleChunk canonical =
        QuickstartReplayTimeline.SelectLatestDeterministicEvaluationChunk(manifest);

    AssertEqual(5000, canonical.CheckpointStep, "canonical replay checkpoint");
    AssertEqual(
        "eval/checkpoint_00005000.jsonl.gz",
        canonical.Path,
        "canonical replay chunk");

    var latest = new ReplayBundleChunk
    {
        Phase = ReplayBundleChunkPhase.Eval,
        PolicyMode = ReplayBundleChunkPolicyMode.Deterministic,
        CheckpointStep = 6000,
        Path = "eval/checkpoint_00006000.jsonl.gz",
        Format = ReplayBundleChunkFormat.JsonlGz,
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
            new()
            {
                Phase = ReplayBundleChunkPhase.Train,
                PolicyMode = ReplayBundleChunkPolicyMode.Stochastic,
                CheckpointStep = 1,
                Path = "train/chunk.jsonl.gz",
                Format = ReplayBundleChunkFormat.JsonlGz,
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
    ReplayBundleChunk selectedChunk =
        QuickstartReplayTimeline.SelectLatestDeterministicEvaluationChunk(manifest);
    IReadOnlyList<ReplayLogStep> steps = EmbodiedLabReplay.ReadSteps(
        FixturePath("navigation_default_replay_log.jsonl"));
    SetCheckpoint(steps, selectedChunk.CheckpointStep);

    QuickstartReplayTimeline.ValidateSelectedChunkSteps(
        manifest.JobId,
        manifest.ScenarioId,
        selectedChunk,
        steps);

    AssertInvalid((chunk, rows) => rows[0].JobId = "other-submission");
    AssertInvalid((chunk, rows) => rows[0].ScenarioId = "other-scenario");
    AssertInvalid((chunk, rows) => rows[0].Phase = "train");
    AssertInvalid((chunk, rows) => rows[0].PolicyMode = "stochastic");
    AssertInvalid(
        (chunk, rows) => rows[0].CheckpointStep = chunk.CheckpointStep - 1);
    AssertInvalid((chunk, rows) => chunk.StepCount++);

    static void AssertInvalid(
        Action<ReplayBundleChunk, IReadOnlyList<ReplayLogStep>> mutate)
    {
        ReplayBundleManifest candidateManifest = EmbodiedLabReplay.ReadManifest(
            FixturePath("navigation_replay_bundle_manifest.json"));
        ReplayBundleChunk candidateChunk =
            QuickstartReplayTimeline.SelectLatestDeterministicEvaluationChunk(
                candidateManifest);
        IReadOnlyList<ReplayLogStep> candidateSteps = EmbodiedLabReplay.ReadSteps(
            FixturePath("navigation_default_replay_log.jsonl"));
        SetCheckpoint(candidateSteps, candidateChunk.CheckpointStep);
        mutate(candidateChunk, candidateSteps);

        AssertThrows<InvalidDataException>(
            () => QuickstartReplayTimeline.ValidateSelectedChunkSteps(
                candidateManifest.JobId,
                candidateManifest.ScenarioId,
                candidateChunk,
                candidateSteps));
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
        Phase = "eval",
        PolicyMode = "deterministic",
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

static QuickstartHistoryRecord CreateRecord(string submissionId, string submittedAtUtc)
{
    return new QuickstartHistoryRecord
    {
        SubmissionId = submissionId,
        SubmittedAtUtc = submittedAtUtc,
        ApiBaseUrl = "https://api.example.test/",
        ResultWebSocketBaseUrl = "wss://results.example.test/",
        ScenarioJson = "{\"scenario_id\":\"scenario-1\"}",
        Status = ResultStatus.Queued,
    };
}

static void WithStore(Action<string, QuickstartHistoryStore> action)
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"embodiedlab-quickstart-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var store = new QuickstartHistoryStore(
            Path.Combine(directory, "job-history.json"));
        store.Load();
        action(directory, store);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
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
