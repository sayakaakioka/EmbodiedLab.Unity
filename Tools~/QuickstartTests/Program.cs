using EmbodiedLab.Contracts;
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
};

foreach ((string name, Action run) in tests)
{
    run();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"Validated {tests.Length} Quickstart history behaviors.");
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
