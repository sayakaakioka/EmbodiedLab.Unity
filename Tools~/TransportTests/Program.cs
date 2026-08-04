using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity;
using EmbodiedLab.Unity.Internal;
using Newtonsoft.Json.Linq;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Endpoint encryption", TestEndpointEncryptionAsync),
    ("HTTP contracts", TestHttpContractsAsync),
    ("Submission response recovery", TestSubmissionResponseRecoveryAsync),
    ("Bounded submission recovery", TestBoundedSubmissionRecoveryAsync),
    ("Submission capability mismatch", TestSubmissionCapabilityMismatchAsync),
    ("Server-owned training dispatch", TestServerOwnedTrainingDispatchAsync),
    ("WebSocket primary", TestHealthyWebSocketUsesNoResultGetAsync),
    ("WebSocket message limit", TestWebSocketMessageLimitAsync),
    ("WebSocket message deadline", TestWebSocketMessageDeadlineAsync),
    ("Connect failure reconciliation", TestConnectFailureReconcilesAsync),
    ("Disconnect reconciliation", TestDisconnectReconcilesAsync),
    ("Silence reconciliation", TestSilenceReconcilesAsync),
    ("Bounded reconnect", TestReconnectDelayIsBoundedAsync),
    ("Local cancellation", TestLocalCancellationStopsMonitoringAsync),
    ("Artifact download", TestArtifactDownloadAsync),
    ("Artifact validation cancellation", TestArtifactValidationCancellationAsync),
    ("Concurrent artifact downloads", TestConcurrentArtifactDownloadsAsync),
    ("Bounded HTTP responses", TestBoundedHttpResponsesAsync),
    ("Artifact format limits", TestArtifactFormatLimits),
    ("Artifact content-length limit", TestOversizedArtifactContentLengthAsync),
    ("Artifact streaming limit", TestOversizedStreamingArtifactAsync),
    ("Incorrect artifact content length", TestIncorrectArtifactContentLengthAsync),
    ("Artifact digest mismatch", TestArtifactDigestMismatchAsync),
    ("Interrupted artifact cleanup", TestInterruptedArtifactDownloadAsync),
    ("Stateful facade", TestStatefulFacadeAsync),
    ("Facade scenario identity", TestFacadeScenarioIdentityAsync),
    ("Stale result rejection", TestStaleResultUpdatesAreRejectedAsync),
    ("Terminal result enrichment", TestTerminalResultEnrichmentAsync),
    ("Facade model selection", TestFacadeModelSelectionAsync),
    ("Facade model fallback rejection", TestFacadeRejectsModelFallbackAsync),
    ("Facade replay chunk", TestFacadeReplayChunkAsync),
    ("Single completion monitor", TestConcurrentCompletionMonitorIsRejectedAsync),
};

foreach ((string name, Func<Task> run) in tests)
{
    await run();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"Validated {tests.Length} transport behaviors.");
return 0;

static ScenarioBundle CreateScenario()
{
    string fixturePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Tests~/Fixtures/navigation_default_scenario_bundle.json"));
    return ScenarioBundleJson.Deserialize(File.ReadAllText(fixturePath));
}

static Task TestEndpointEncryptionAsync()
{
    var remote = new EmbodiedLabEndpoints(
        "https://api.example.test/root",
        "wss://stream.example.test/service");
    AssertEqual("https://api.example.test/root/", remote.ApiBaseUri.AbsoluteUri, "remote API");
    AssertEqual(
        "wss://stream.example.test/service/",
        remote.ResultWebSocketBaseUri.AbsoluteUri,
        "remote result stream");

    foreach ((string api, string stream) in new[]
             {
                 ("http://localhost:8000", "ws://localhost:8001"),
                 ("http://127.0.0.1:8000", "ws://127.0.0.1:8001"),
                 ("http://[::1]:8000", "ws://[::1]:8001"),
             })
    {
        var loopback = new EmbodiedLabEndpoints(api, stream);
        AssertEqual("http", loopback.ApiBaseUri.Scheme, "loopback API scheme");
        AssertEqual("ws", loopback.ResultWebSocketBaseUri.Scheme, "loopback stream scheme");
    }

    AssertArgumentException(
        () => new EmbodiedLabEndpoints(
            "http://api.example.test",
            "wss://stream.example.test"),
        "apiBaseUrl",
        "remote plaintext API");
    AssertArgumentException(
        () => new EmbodiedLabEndpoints(
            "https://api.example.test",
            "ws://stream.example.test"),
        "resultWebSocketBaseUrl",
        "remote plaintext result stream");
    AssertArgumentException(
        () => new EmbodiedLabEndpoints(
            "https://api.example.test?query=1",
            "wss://stream.example.test"),
        "apiBaseUrl",
        "API query");
    AssertArgumentException(
        () => new EmbodiedLabEndpoints(
            "https://api.example.test",
            "wss://stream.example.test#fragment"),
        "resultWebSocketBaseUrl",
        "result stream fragment");
    AssertArgumentException(
        () => new EmbodiedLabEndpoints(
            "/relative-api",
            "wss://stream.example.test"),
        "apiBaseUrl",
        "relative API");

    AssertInternalEndpointRejected(
        new Uri("http://api.example.test"),
        new Uri("wss://stream.example.test"),
        "apiBaseUri");
    AssertInternalEndpointRejected(
        new Uri("https://api.example.test"),
        new Uri("ws://stream.example.test"),
        "resultWebSocketBaseUri");
    return Task.CompletedTask;
}

static void AssertInternalEndpointRejected(
    Uri apiBaseUri,
    Uri resultWebSocketBaseUri,
    string parameterName)
{
    AssertArgumentException(
        () =>
        {
            using var httpClient = new HttpClient(new RecordingHttpHandler(
                request => throw new InvalidOperationException(
                    $"Unexpected request: {request.Uri}")));
            using var transport = new EmbodiedLabTransport(
                apiBaseUri,
                resultWebSocketBaseUri,
                httpClient,
                new QueueWebSocketFactory(),
                ResultMonitorTiming.Default,
                Task.Delay);
        },
        parameterName,
        "internal plaintext endpoint");
}

static async Task TestHttpContractsAsync()
{
    var handler = new RecordingHttpHandler(request => request.Uri.AbsolutePath switch
    {
        "/root/submissions" => JsonResponse(
            $$"""{"cancel_token":"{{request.ClientCancelToken}}","status":"accepted","submission_id":"submission-1"}"""),
        "/root/submissions/submission-1/cancel" => JsonResponse(
            ResultJson("cancelled"),
            HttpStatusCode.Accepted),
        "/root/results/submission-1" => JsonResponse(ResultJson("running")),
        _ => throw new InvalidOperationException($"Unexpected request: {request.Uri}"),
    });
    using var transport = CreateTransport(handler, new QueueWebSocketFactory());

    SubmissionResponse submitted = await transport.SubmitAsync(
        CreateScenario(),
        CancellationToken.None);
    ResultDocument result = await transport.GetResultAsync(
        submitted.SubmissionId,
        CancellationToken.None);
    ResultDocument cancelled = await transport.CancelAsync(
        submitted.SubmissionId,
        submitted.CancelToken,
        CancellationToken.None);

    RecordedRequest submissionRequest = handler.Requests.Single(
        request => request.Uri.AbsolutePath.EndsWith("/submissions", StringComparison.Ordinal));
    AssertEqual(
        submissionRequest.ClientCancelToken,
        submitted.CancelToken,
        "submission capability");
    AssertEqual(true, submissionRequest.IdempotencyKey?.Length >= 32, "idempotency key");
    AssertEqual(true, submitted.CancelToken.Length >= 32, "client cancellation capability");
    AssertEqual(ResultStatus.Running, result.Status, "result status");
    AssertEqual(ResultStatus.Cancelled, cancelled.Status, "cancel status");
    AssertEqual(3, handler.Requests.Count, "HTTP request count");
    RecordedRequest cancelRequest = handler.Requests.Single(
        request => request.Uri.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal));
    AssertEqual<string?>(null, cancelRequest.Body, "cancel request body");
    AssertEqual("Bearer", cancelRequest.Authorization?.Scheme, "cancel auth scheme");
    AssertEqual(submitted.CancelToken, cancelRequest.Authorization?.Parameter, "cancel auth token");
}

static async Task TestSubmissionResponseRecoveryAsync()
{
    int submissionAttempts = 0;
    var handler = new RecordingHttpHandler(request =>
    {
        if (!request.Uri.AbsolutePath.EndsWith("/submissions", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected request: {request.Uri}");
        }

        submissionAttempts++;
        if (submissionAttempts == 1)
        {
            return JsonResponse("{");
        }

        return JsonResponse(
            $$"""{"cancel_token":"{{request.ClientCancelToken}}","status":"accepted","submission_id":"submission-1"}""");
    });
    using var transport = CreateTransport(handler, new QueueWebSocketFactory());

    SubmissionResponse recovered = await transport.SubmitAsync(
        CreateScenario(),
        CancellationToken.None);

    AssertEqual(2, handler.Requests.Count, "submission recovery attempts");
    AssertEqual(
        handler.Requests[0].IdempotencyKey,
        handler.Requests[1].IdempotencyKey,
        "reused idempotency key");
    AssertEqual(
        handler.Requests[0].ClientCancelToken,
        handler.Requests[1].ClientCancelToken,
        "reused cancellation capability");
    AssertEqual(handler.Requests[0].Body, handler.Requests[1].Body, "reused scenario body");
    AssertEqual(
        false,
        string.Equals(
            handler.Requests[1].IdempotencyKey,
            handler.Requests[1].ClientCancelToken,
            StringComparison.Ordinal),
        "independent recovery values");
    AssertEqual(
        handler.Requests[1].ClientCancelToken,
        recovered.CancelToken,
        "recovered cancellation capability");
}

static async Task TestBoundedSubmissionRecoveryAsync()
{
    var handler = new RecordingHttpHandler(request =>
        throw new HttpRequestException($"Submission response was lost: {request.Uri}"));
    using var transport = CreateTransport(handler, new QueueWebSocketFactory());

    await AssertThrowsAsync<HttpRequestException>(
        () => transport.SubmitAsync(
            CreateScenario(),
            CancellationToken.None));

    AssertEqual(2, handler.Requests.Count, "bounded submission attempts");
    AssertEqual(
        handler.Requests[0].IdempotencyKey,
        handler.Requests[1].IdempotencyKey,
        "bounded retry idempotency key");
    AssertEqual(
        handler.Requests[0].ClientCancelToken,
        handler.Requests[1].ClientCancelToken,
        "bounded retry cancellation capability");
}

static async Task TestSubmissionCapabilityMismatchAsync()
{
    var handler = new RecordingHttpHandler(request =>
    {
        if (!request.Uri.AbsolutePath.EndsWith("/submissions", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected request: {request.Uri}");
        }

        return JsonResponse(
            """{"cancel_token":"different-server-capability-00000001","status":"accepted","submission_id":"submission-1"}""");
    });
    using var transport = CreateTransport(handler, new QueueWebSocketFactory());

    await AssertThrowsAsync<InvalidDataException>(
        () => transport.SubmitAsync(
            CreateScenario(),
            CancellationToken.None));

    AssertEqual(1, handler.Requests.Count, "capability mismatch request count");
}

static async Task TestServerOwnedTrainingDispatchAsync()
{
    var handler = new RecordingHttpHandler(request => request.Uri.AbsolutePath switch
    {
        "/root/submissions" => JsonResponse(
            $$"""{"cancel_token":"{{request.ClientCancelToken}}","status":"accepted","submission_id":"submission-1"}"""),
        _ => throw new InvalidOperationException($"Unexpected request: {request.Uri}"),
    });
    using EmbodiedLabJob job = await EmbodiedLabJob.SubmitAsync(
        CreateTransport(handler, new QueueWebSocketFactory()),
        CreateScenario(),
        synchronizationContext: null,
        CancellationToken.None);

    AssertEqual("submission-1", job.SubmissionId, "server-owned dispatch submission ID");
    AssertEqual(
        handler.Requests[0].ClientCancelToken,
        job.CancelToken,
        "server-owned dispatch capability");
    AssertEqual(1, handler.Requests.Count, "server-owned dispatch request count");
}

static async Task TestHealthyWebSocketUsesNoResultGetAsync()
{
    var handler = new RecordingHttpHandler(
        request => throw new InvalidOperationException($"Unexpected HTTP request: {request.Uri}"));
    var socket = new ScriptedWebSocket(
        TextFrame("""{"type":"connected","submission_id":"submission-1"}"""),
        TextFrame(ResultJson("running")),
        TextFrame(ResultJson("completed")));
    var factory = new QueueWebSocketFactory(socket);
    using var transport = CreateTransport(handler, factory);
    var statuses = new List<ResultStatus>();

    await transport.MonitorResultAsync(
        "submission-1",
        result => statuses.Add(result.Status),
        CancellationToken.None);

    AssertSequence(
        new[] { ResultStatus.Running, ResultStatus.Completed },
        statuses,
        "stream statuses");
    AssertEqual(0, handler.Requests.Count, "healthy stream HTTP result requests");
    AssertEqual(
        "wss://stream.example.test/service/ws/results/submission-1",
        socket.ConnectedUri?.ToString(),
        "result stream URI");
}

static async Task TestWebSocketMessageLimitAsync()
{
    ScriptedFrame[] frames = Enumerable.Range(0, 129)
        .Select(_ => TextFrame(new string('x', 8192), endOfMessage: false))
        .ToArray();
    var socket = new ScriptedWebSocket(frames);
    using var transport = CreateTransport(
        new RecordingHttpHandler(
            request => throw new InvalidOperationException(
                $"Unexpected HTTP request: {request.Uri}")),
        new QueueWebSocketFactory(socket));

    await AssertThrowsAsync<InvalidDataException>(
        () => transport.MonitorResultAsync(
            "submission-1",
            _ => { },
            CancellationToken.None));

    AssertEqual(true, socket.AbortCalled, "oversized stream abort");
}

static async Task TestWebSocketMessageDeadlineAsync()
{
    var socket = new ScriptedWebSocket(
        TextFrame("{", endOfMessage: false, delay: TimeSpan.FromMilliseconds(40)),
        TextFrame("}", delay: TimeSpan.FromMilliseconds(40)));
    var handler = ResultHandler("completed");
    var timing = new ResultMonitorTiming(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(60),
        TimeSpan.Zero,
        TimeSpan.Zero);
    using var transport = CreateTransport(
        handler,
        new QueueWebSocketFactory(socket),
        timing,
        (_, _) => Task.CompletedTask);

    await transport.MonitorResultAsync(
        "submission-1",
        _ => { },
        CancellationToken.None);

    AssertEqual(true, socket.AbortCalled, "message deadline stream abort");
    AssertSingleResultGet(handler);
}

static async Task TestConnectFailureReconcilesAsync()
{
    var handler = ResultHandler("completed");
    var factory = new QueueWebSocketFactory(
        new ScriptedWebSocket(new WebSocketException("connect failed")));
    using var transport = CreateTransport(handler, factory);
    ResultDocument? latest = null;

    await transport.MonitorResultAsync(
        "submission-1",
        result => latest = result,
        CancellationToken.None);

    AssertEqual(ResultStatus.Completed, latest?.Status, "reconciled status");
    AssertSingleResultGet(handler);
}

static async Task TestDisconnectReconcilesAsync()
{
    var handler = ResultHandler("completed");
    var factory = new QueueWebSocketFactory(
        new ScriptedWebSocket(
            TextFrame("""{"type":"connected","submission_id":"submission-1"}"""),
            TextFrame(ResultJson("running")),
            CloseFrame()));
    using var transport = CreateTransport(handler, factory);
    var statuses = new List<ResultStatus>();

    await transport.MonitorResultAsync(
        "submission-1",
        result => statuses.Add(result.Status),
        CancellationToken.None);

    AssertSequence(
        new[] { ResultStatus.Running, ResultStatus.Completed },
        statuses,
        "disconnect statuses");
    AssertSingleResultGet(handler);
}

static async Task TestSilenceReconcilesAsync()
{
    var handler = ResultHandler("completed");
    var factory = new QueueWebSocketFactory(
        new ScriptedWebSocket(
            new[] { TextFrame("""{"type":"connected","submission_id":"submission-1"}""") },
            blockAfterFrames: true));
    ResultMonitorTiming timing = new(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.Zero,
        TimeSpan.Zero);
    using var transport = CreateTransport(handler, factory, timing);

    await transport.MonitorResultAsync(
        "submission-1",
        _ => { },
        CancellationToken.None);

    AssertSingleResultGet(handler);
}

static async Task TestReconnectDelayIsBoundedAsync()
{
    int resultGetCount = 0;
    var handler = new RecordingHttpHandler(request =>
    {
        if (!request.Uri.AbsolutePath.EndsWith("/results/submission-1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected request: {request.Uri}");
        }

        resultGetCount++;
        return JsonResponse(ResultJson(resultGetCount == 4 ? "completed" : "running"));
    });
    var factory = new QueueWebSocketFactory(
        new ScriptedWebSocket(new WebSocketException("failure 1")),
        new ScriptedWebSocket(new WebSocketException("failure 2")),
        new ScriptedWebSocket(new WebSocketException("failure 3")),
        new ScriptedWebSocket(new WebSocketException("failure 4")));
    ResultMonitorTiming timing = new(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(4));
    var delays = new List<TimeSpan>();
    using var transport = CreateTransport(
        handler,
        factory,
        timing,
        (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });

    await transport.MonitorResultAsync(
        "submission-1",
        _ => { },
        CancellationToken.None);

    AssertSequence(
        new[]
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
        },
        delays,
        "reconnect delays");
}

static async Task TestArtifactDownloadAsync()
{
    byte[] expected = Encoding.UTF8.GetBytes("model bytes");
    var handler = new RecordingHttpHandler(request =>
    {
        AssertEqual(
            "https://storage.googleapis.com/bucket-name/folder/model%20one.onnx",
            request.Uri.AbsoluteUri,
            "artifact URI");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expected),
        };
    });
    using var transport = CreateTransport(handler, new QueueWebSocketFactory());
    string directory = Path.Combine(Path.GetTempPath(), $"embodiedlab-transport-{Guid.NewGuid():N}");
    string destination = Path.Combine(directory, "model.onnx");

    try
    {
        await transport.DownloadArtifactAsync(
            new ArtifactDownloadRequest(
                ArtifactStorage.Gcs,
                "bucket-name",
                "folder/model one.onnx",
                "onnx",
                expected.LongLength,
                ComputeSha256(expected)),
            destination,
            CancellationToken.None);
        AssertSequence(expected, await File.ReadAllBytesAsync(destination), "artifact bytes");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static async Task TestConcurrentArtifactDownloadsAsync()
{
    byte[] first = Encoding.UTF8.GetBytes("first verified model");
    byte[] second = Encoding.UTF8.GetBytes("second verified model");
    using var transport = CreateTransport(
        new CoordinatedArtifactHandler(first, second),
        new QueueWebSocketFactory());
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"embodiedlab-concurrent-download-{Guid.NewGuid():N}");
    string destination = Path.Combine(directory, "policy.onnx");

    try
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(destination, "existing destination");
        Task firstDownload = transport.DownloadArtifactAsync(
            new ArtifactDownloadRequest(
                ArtifactStorage.Gcs,
                "models",
                "first.onnx",
                "onnx",
                first.LongLength,
                ComputeSha256(first)),
            destination,
            CancellationToken.None);
        Task secondDownload = transport.DownloadArtifactAsync(
            new ArtifactDownloadRequest(
                ArtifactStorage.Gcs,
                "models",
                "second.onnx",
                "onnx",
                second.LongLength,
                ComputeSha256(second)),
            destination,
            CancellationToken.None);

        await Task.WhenAll(firstDownload, secondDownload);
        byte[] actual = await File.ReadAllBytesAsync(destination);
        AssertEqual(
            actual.SequenceEqual(first) || actual.SequenceEqual(second),
            true,
            "concurrent destination contains one fully verified artifact");
        AssertEqual(
            0,
            Directory.GetFiles(directory, "*.part").Length,
            "concurrent temporary files");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static async Task TestArtifactValidationCancellationAsync()
{
    byte[] expected = Encoding.UTF8.GetBytes("validated artifact");
    var handler = new RecordingHttpHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expected),
        });
    using var transport = CreateTransport(handler, new QueueWebSocketFactory());
    using var cancellation = new CancellationTokenSource();
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"embodiedlab-validation-cancel-{Guid.NewGuid():N}");
    string destination = Path.Combine(directory, "manifest.json");
    byte[] existing = Encoding.UTF8.GetBytes("existing destination");
    bool validationStarted = false;

    try
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(destination, existing);
        await AssertThrowsAsync<OperationCanceledException>(
            () => transport.DownloadArtifactAsync(
                new ArtifactDownloadRequest(
                    ArtifactStorage.Gcs,
                    "bucket-name",
                    "replay/manifest.json",
                    "json",
                    expected.LongLength,
                    ComputeSha256(expected)),
                destination,
                cancellation.Token,
                (_, validationCancellation) =>
                {
                    validationStarted = true;
                    cancellation.Cancel();
                    validationCancellation.ThrowIfCancellationRequested();
                }));
        AssertEqual(true, validationStarted, "artifact validation started");
        AssertSequence(
            existing,
            await File.ReadAllBytesAsync(destination),
            "validation cancellation destination bytes");
        AssertEqual(
            0,
            Directory.GetFiles(directory, "*.part").Length,
            "validation cancellation temporary files");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static async Task TestBoundedHttpResponsesAsync()
{
    byte[] oversizedJson = new byte[(1024 * 1024) + 1];
    var successHandler = new RecordingHttpHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(oversizedJson),
        });
    using (var transport = CreateTransport(
        successHandler,
        new QueueWebSocketFactory()))
    {
        await AssertThrowsAsync<InvalidDataException>(
            () => transport.GetResultAsync("submission-1", CancellationToken.None));
    }

    byte[] oversizedError = new byte[(64 * 1024) + 1];
    var errorHandler = new RecordingHttpHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new ByteArrayContent(oversizedError),
        });
    using var errorTransport = CreateTransport(
        errorHandler,
        new QueueWebSocketFactory());
    await AssertThrowsAsync<InvalidDataException>(
        () => errorTransport.GetResultAsync("submission-1", CancellationToken.None));
}

static Task TestArtifactFormatLimits()
{
    AssertEqual(
        1024L * 1024L,
        EmbodiedLabTransport.GetMaximumArtifactBytes("json"),
        "JSON artifact limit");
    AssertEqual(
        64L * 1024L * 1024L,
        EmbodiedLabTransport.GetMaximumArtifactBytes("jsonl"),
        "JSONL artifact limit");
    AssertEqual(
        64L * 1024L * 1024L,
        EmbodiedLabTransport.GetMaximumArtifactBytes("jsonl.gz"),
        "compressed JSONL artifact limit");
    AssertEqual(
        1024L * 1024L * 1024L,
        EmbodiedLabTransport.GetMaximumArtifactBytes("onnx"),
        "ONNX artifact limit");
    return Task.CompletedTask;
}

static Task TestOversizedArtifactContentLengthAsync()
{
    var content = new ByteArrayContent(Array.Empty<byte>());
    content.Headers.ContentLength = (1024L * 1024L) + 1L;
    return AssertRejectedDownloadPreservesDestinationAsync<InvalidDataException>(
        content,
        "oversized content length");
}

static Task TestOversizedStreamingArtifactAsync()
{
    var content = new StreamContent(new RepeatingReadStream((1024L * 1024L) + 1L));
    content.Headers.ContentLength = null;
    return AssertRejectedDownloadPreservesDestinationAsync<InvalidDataException>(
        content,
        "oversized streaming response");
}

static Task TestIncorrectArtifactContentLengthAsync()
{
    var content = new StreamContent(new RepeatingReadStream((1024L * 1024L) + 1L));
    content.Headers.ContentLength = 1;
    return AssertRejectedDownloadPreservesDestinationAsync<InvalidDataException>(
        content,
        "incorrect content length");
}

static Task TestInterruptedArtifactDownloadAsync()
{
    var content = new StreamContent(new InterruptedReadStream(4096));
    content.Headers.ContentLength = null;
    return AssertRejectedDownloadPreservesDestinationAsync<IOException>(
        content,
        "interrupted response",
        declaredSize: 4096);
}

static Task TestArtifactDigestMismatchAsync()
{
    var content = new ByteArrayContent(new byte[] { 1 });
    return AssertRejectedDownloadPreservesDestinationAsync<InvalidDataException>(
        content,
        "artifact digest mismatch");
}

static async Task AssertRejectedDownloadPreservesDestinationAsync<TException>(
    HttpContent content,
    string description,
    long declaredSize = 1)
    where TException : Exception
{
    byte[] existing = Encoding.UTF8.GetBytes("existing destination");
    var handler = new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = content,
    });
    using var transport = CreateTransport(handler, new QueueWebSocketFactory());
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"embodiedlab-download-limit-{Guid.NewGuid():N}");
    string destination = Path.Combine(directory, "manifest.json");

    try
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(destination, existing);
        bool rejected = false;
        try
        {
            await transport.DownloadArtifactAsync(
                new ArtifactDownloadRequest(
                    ArtifactStorage.Gcs,
                    "bucket-name",
                    "replay/manifest.json",
                    "json",
                    declaredSize,
                    new string('0', 64)),
                destination,
                CancellationToken.None);
        }
        catch (TException)
        {
            rejected = true;
        }

        AssertEqual(true, rejected, description);
        AssertSequence(
            existing,
            await File.ReadAllBytesAsync(destination),
            $"{description} destination bytes");
        AssertEqual(
            0,
            Directory.GetFiles(directory, "*.part").Length,
            $"{description} temporary files");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static string ComputeSha256(byte[] bytes)
{
    return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

static async Task TestLocalCancellationStopsMonitoringAsync()
{
    var handler = ResultHandler("running");
    var factory = new QueueWebSocketFactory(
        new ScriptedWebSocket(new WebSocketException("connect failed")));
    using var transport = CreateTransport(handler, factory);
    using var cancellation = new CancellationTokenSource();
    bool observedCancellation = false;

    try
    {
        await transport.MonitorResultAsync(
            "submission-1",
            _ => cancellation.Cancel(),
            cancellation.Token);
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        observedCancellation = true;
    }

    AssertEqual(true, observedCancellation, "local monitoring cancellation");
}

static async Task TestStatefulFacadeAsync()
{
    int resultReadCount = 0;
    var handler = new RecordingHttpHandler(request => request.Uri.AbsolutePath switch
    {
        "/root/results/submission-1" => JsonResponse(
            ResultJson(resultReadCount++ == 0 ? "running" : "queued")),
        "/root/submissions/submission-1/cancel" => JsonResponse(
            ResultJson("cancelled"),
            HttpStatusCode.Accepted),
        _ => throw new InvalidOperationException($"Unexpected request: {request.Uri}"),
    });
    using var job = new EmbodiedLabJob(
        CreateTransport(handler, new QueueWebSocketFactory()),
        "submission-1",
        "navigation_default",
        "capability-1",
        synchronizationContext: null);
    var statuses = new List<ResultStatus>();
    job.ResultUpdated += result => statuses.Add(result.Status);

    ResultDocument refreshed = await job.RefreshAsync();
    ResultDocument cancelled = await job.CancelAsync();
    ResultDocument rejectedRollback = await job.RefreshAsync();

    AssertEqual(ResultStatus.Running, refreshed.Status, "facade refreshed status");
    AssertEqual(ResultStatus.Cancelled, cancelled.Status, "facade cancelled status");
    AssertEqual(ResultStatus.Cancelled, rejectedRollback.Status, "rejected terminal rollback");
    AssertEqual(ResultStatus.Cancelled, job.LatestResult?.Status, "facade latest status");
    AssertEqual(true, job.IsTerminal, "facade terminal state");
    AssertEqual(true, job.CanCancel, "facade cancellation capability");
    AssertSequence(
        new[] { ResultStatus.Running, ResultStatus.Cancelled },
        statuses,
        "facade result events");
}

static async Task TestFacadeScenarioIdentityAsync()
{
    var handler = new RecordingHttpHandler(_ =>
        JsonResponse(ResultJson(
            "completed",
            ",\"result_bundle\":{\"scenario_id\":\"other-scenario\"}")));
    using var job = new EmbodiedLabJob(
        CreateTransport(handler, new QueueWebSocketFactory()),
        "submission-1",
        "navigation_default",
        "capability-1",
        synchronizationContext: null);

    await AssertThrowsAsync<InvalidOperationException>(() => job.RefreshAsync());
}

static async Task TestStaleResultUpdatesAreRejectedAsync()
{
    int resultReadCount = 0;
    var handler = new RecordingHttpHandler(request =>
    {
        if (!request.Uri.AbsolutePath.EndsWith("/results/submission-1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected request: {request.Uri}");
        }

        return JsonResponse(
            resultReadCount++ == 0
                ? ResultJson("running", ",\"updated_at\":\"2026-07-20T03:00:00Z\"")
                : ResultJson("queued", ",\"updated_at\":\"2026-07-20T02:00:00Z\""));
    });
    using var job = new EmbodiedLabJob(
        CreateTransport(handler, new QueueWebSocketFactory()),
        "submission-1",
        "navigation_default",
        "capability-1",
        synchronizationContext: null);
    var statuses = new List<ResultStatus>();
    job.ResultUpdated += result => statuses.Add(result.Status);

    ResultDocument current = await job.RefreshAsync();
    ResultDocument stale = await job.RefreshAsync();

    AssertEqual(ResultStatus.Running, current.Status, "current result status");
    AssertEqual(ResultStatus.Running, stale.Status, "returned stale result replacement");
    AssertEqual(ResultStatus.Running, job.LatestResult?.Status, "latest non-stale status");
    AssertSequence(new[] { ResultStatus.Running }, statuses, "accepted result events");
}

static async Task TestTerminalResultEnrichmentAsync()
{
    int resultReadCount = 0;
    var handler = new RecordingHttpHandler(request =>
    {
        if (!request.Uri.AbsolutePath.EndsWith("/results/submission-1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected request: {request.Uri}");
        }

        string updatedAt = resultReadCount++ == 0
            ? "2026-07-20T02:00:00Z"
            : "2026-07-20T03:00:00Z";
        return JsonResponse(
            ResultJson("completed", $",\"updated_at\":\"{updatedAt}\""));
    });
    using var job = new EmbodiedLabJob(
        CreateTransport(handler, new QueueWebSocketFactory()),
        "submission-1",
        "navigation_default",
        "capability-1",
        synchronizationContext: null);

    await job.RefreshAsync();
    ResultDocument enriched = await job.RefreshAsync();

    AssertEqual("2026-07-20T03:00:00Z", enriched.UpdatedAt, "terminal enrichment timestamp");
    AssertEqual("2026-07-20T03:00:00Z", job.LatestResult?.UpdatedAt, "latest enrichment");
}

static async Task TestFacadeModelSelectionAsync()
{
    byte[] expected = Encoding.UTF8.GetBytes("onnx model");
    var handler = new RecordingHttpHandler(request =>
    {
        if (request.Uri.AbsolutePath.EndsWith("/results/submission-1", StringComparison.Ordinal))
        {
            return JsonResponse(
                ResultJson(
                    "completed",
                    """
                    ,"result_bundle":{
                      "artifacts":{
                        "onnx_model":{
                          "storage":"gcs",
                          "bucket":"models",
                          "path":"policy.onnx",
                          "format":"onnx",
                          "size_bytes":10,
                          "sha256":"201b4b2329f9a88369562d3f2f178df6d5caa68a0c1089c426f7bc936ca15626"
                        }
                      }
                    }
                    """));
        }

        AssertEqual(
            "https://storage.googleapis.com/models/policy.onnx",
            request.Uri.AbsoluteUri,
            "facade selected model URI");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expected),
        };
    });
    using var job = new EmbodiedLabJob(
        CreateTransport(handler, new QueueWebSocketFactory()),
        "submission-1",
        "navigation_default",
        "capability-1",
        synchronizationContext: null);
    string directory = Path.Combine(Path.GetTempPath(), $"embodiedlab-job-{Guid.NewGuid():N}");
    string destination = Path.Combine(directory, "policy.onnx");

    try
    {
        await job.RefreshAsync();
        await job.DownloadModelAsync(destination);
        AssertSequence(expected, await File.ReadAllBytesAsync(destination), "facade model bytes");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static async Task TestFacadeRejectsModelFallbackAsync()
{
    var handler = new RecordingHttpHandler(request =>
    {
        if (request.Uri.AbsolutePath.EndsWith("/results/submission-1", StringComparison.Ordinal))
        {
            return JsonResponse(ResultJson(
                "completed",
                ",\"result_bundle\":{\"artifacts\":{\"onnx_model\":null}}"));
        }

        throw new InvalidOperationException($"Unexpected artifact request: {request.Uri}");
    });
    using var job = new EmbodiedLabJob(
        CreateTransport(handler, new QueueWebSocketFactory()),
        "submission-1",
        "navigation_default",
        "capability-1",
        synchronizationContext: null);
    await AssertThrowsAsync<InvalidDataException>(() => job.RefreshAsync());
    AssertEqual(1, handler.Requests.Count, "model rejection request count");
}

static async Task TestFacadeReplayChunkAsync()
{
    string fixturePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "../../../../../Tests~/Fixtures/navigation_default_replay_log.jsonl"));
    JObject replayStep = JObject.Parse(File.ReadLines(fixturePath).First());
    replayStep["scenario_id"] = "scenario-1";
    replayStep["checkpoint_step"] = 5000;
    byte[] expected = GzipUtf8(
        replayStep.ToString(Newtonsoft.Json.Formatting.None) + "\n");
    var handler = new RecordingHttpHandler(request =>
    {
        if (request.Uri.AbsolutePath.EndsWith("/results/submission-1", StringComparison.Ordinal))
        {
            return JsonResponse(
                ResultJson(
                    "completed",
                    """
                    ,"result_bundle":{
                      "scenario_id":"scenario-1",
                      "job_id":"submission-1",
                      "status":"completed",
                      "artifacts":{
                        "replay_bundle":{
                          "storage":"gcs",
                          "bucket":"replays",
                          "path":"results/submission-1/replay/manifest.json",
                          "format":"json"
                        }
                      }
                    }
                    """));
        }

        AssertEqual(
            "https://storage.googleapis.com/replays/results/submission-1/replay/eval/checkpoint_00005000.jsonl.gz",
            request.Uri.AbsoluteUri,
            "facade replay chunk URI");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expected),
        };
    });
    using var job = new EmbodiedLabJob(
        CreateTransport(handler, new QueueWebSocketFactory()),
        "submission-1",
        "scenario-1",
        "capability-1",
        synchronizationContext: null);
    string directory = Path.Combine(Path.GetTempPath(), $"embodiedlab-job-{Guid.NewGuid():N}");
    string destination = Path.Combine(directory, "checkpoint.jsonl.gz");

    try
    {
        await job.RefreshAsync();
        var chunk = new EvalReplayBundleChunk
        {
            CheckpointStep = 5000,
            Path = "eval/checkpoint_00005000.jsonl.gz",
            Format = EvalReplayBundleChunkFormat.JsonlGz,
            SizeBytes = expected.Length,
            Sha256 = ComputeSha256(expected),
            StepCount = 1,
            EpisodeCount = 1,
        };
        await job.DownloadReplayChunkAsync(chunk, destination);
        AssertSequence(
            expected,
            await File.ReadAllBytesAsync(destination),
            "facade replay chunk bytes");

        bool traversalRejected = false;
        try
        {
            chunk.Path = "../other-job/chunk.jsonl.gz";
            await job.DownloadReplayChunkAsync(chunk, destination);
        }
        catch (ArgumentException)
        {
            traversalRejected = true;
        }

        AssertEqual(true, traversalRejected, "replay chunk traversal rejection");

        bool pathLengthRejected = false;
        try
        {
            chunk.Path = new string('p', 1025);
            await job.DownloadReplayChunkAsync(chunk, destination);
        }
        catch (InvalidDataException)
        {
            pathLengthRejected = true;
        }

        AssertEqual(true, pathLengthRejected, "replay chunk path length rejection");

        bool stepCountRejected = false;
        try
        {
            chunk.Path = "eval/checkpoint_00005000.jsonl.gz";
            chunk.StepCount = 100001;
            await job.DownloadReplayChunkAsync(chunk, destination);
        }
        catch (InvalidDataException)
        {
            stepCountRejected = true;
        }

        AssertEqual(true, stepCountRejected, "replay chunk step count rejection");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static byte[] GzipUtf8(string value)
{
    using var destination = new MemoryStream();
    using (var gzip = new GZipStream(destination, CompressionMode.Compress, leaveOpen: true))
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        gzip.Write(bytes, 0, bytes.Length);
    }

    return destination.ToArray();
}

static async Task TestConcurrentCompletionMonitorIsRejectedAsync()
{
    var handler = new RecordingHttpHandler(
        request => throw new InvalidOperationException($"Unexpected request: {request.Uri}"));
    var socket = new ScriptedWebSocket(
        new[] { TextFrame("""{"type":"connected","submission_id":"submission-1"}""") },
        blockAfterFrames: true);
    using var job = new EmbodiedLabJob(
        CreateTransport(handler, new QueueWebSocketFactory(socket)),
        "submission-1",
        "navigation_default",
        "capability-1",
        synchronizationContext: null);
    using var cancellation = new CancellationTokenSource();
    var firstMonitor = job.WaitForCompletionAsync(cancellation.Token);
    bool concurrentMonitorRejected = false;

    try
    {
        await job.WaitForCompletionAsync();
    }
    catch (InvalidOperationException)
    {
        concurrentMonitorRejected = true;
    }

    cancellation.Cancel();
    bool firstMonitorCancelled = false;
    try
    {
        await firstMonitor;
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        firstMonitorCancelled = true;
    }

    AssertEqual(true, concurrentMonitorRejected, "concurrent monitor rejection");
    AssertEqual(true, firstMonitorCancelled, "first monitor cancellation");
}

static EmbodiedLabTransport CreateTransport(
    HttpMessageHandler handler,
    IResultWebSocketFactory factory,
    ResultMonitorTiming? timing = null,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
{
    return new EmbodiedLabTransport(
        new Uri("https://api.example.test/root/"),
        new Uri("wss://stream.example.test/service/"),
        new HttpClient(handler),
        factory,
        timing ?? ResultMonitorTiming.Default,
        delayAsync ?? Task.Delay);
}

static RecordingHttpHandler ResultHandler(string status)
{
    return new RecordingHttpHandler(request =>
    {
        if (!request.Uri.AbsolutePath.EndsWith("/results/submission-1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected request: {request.Uri}");
        }

        return JsonResponse(ResultJson(status));
    });
}

static void AssertSingleResultGet(RecordingHttpHandler handler)
{
    AssertEqual(1, handler.Requests.Count, "HTTP reconciliation count");
    AssertEqual(HttpMethod.Get, handler.Requests[0].Method, "HTTP reconciliation method");
    AssertEqual(
        "/root/results/submission-1",
        handler.Requests[0].Uri.AbsolutePath,
        "HTTP reconciliation path");
}

static HttpResponseMessage JsonResponse(
    string json,
    HttpStatusCode statusCode = HttpStatusCode.OK)
{
    return new HttpResponseMessage(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}

static string ResultJson(string status, string additionalProperties = "")
{
    JObject result;
    if (string.Equals(status, "completed", StringComparison.Ordinal))
    {
        string fixturePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../Tests~/Fixtures/navigation_completed_result_document.json"));
        result = JObject.Parse(File.ReadAllText(fixturePath));
    }
    else
    {
        result = new JObject
        {
            ["submission_id"] = "submission-1",
            ["status"] = status,
            ["progress"] = new JObject
            {
                ["phase"] = status,
                ["current_step"] = 0,
                ["total_steps"] = 5000,
                ["message"] = $"Training {status}",
            },
            ["error"] = string.Equals(status, "failed", StringComparison.Ordinal)
                ? "Training failed"
                : JValue.CreateNull(),
            ["result_bundle"] = JValue.CreateNull(),
            ["updated_at"] = "2026-07-20T01:00:00Z",
        };
    }

    if (!string.IsNullOrWhiteSpace(additionalProperties))
    {
        JObject overrides = JObject.Parse(
            "{" + additionalProperties.TrimStart().TrimStart(',') + "}");
        result.Merge(
            overrides,
            new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Replace,
                MergeNullValueHandling = MergeNullValueHandling.Merge,
            });
    }

    return result.ToString(Newtonsoft.Json.Formatting.None);
}

static ScriptedFrame TextFrame(
    string text,
    bool endOfMessage = true,
    TimeSpan delay = default) =>
    new(text, WebSocketMessageType.Text, endOfMessage, delay);

static ScriptedFrame CloseFrame() =>
    new(string.Empty, WebSocketMessageType.Close, true, TimeSpan.Zero);

static async Task AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Expected {typeof(TException).Name}, but no matching exception was thrown.");
}

static void AssertEqual<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"Expected {description} to be '{expected}', but received '{actual}'.");
    }
}

static void AssertArgumentException(
    Action action,
    string parameterName,
    string description)
{
    try
    {
        action();
    }
    catch (ArgumentException exception)
    {
        AssertEqual(parameterName, exception.ParamName, $"{description} parameter");
        return;
    }

    throw new InvalidOperationException($"Expected {description} to be rejected.");
}

static void AssertSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string description)
{
    T[] expectedValues = expected.ToArray();
    T[] actualValues = actual.ToArray();
    if (!expectedValues.SequenceEqual(actualValues))
    {
        throw new InvalidOperationException(
            $"Expected {description} [{string.Join(", ", expectedValues)}], " +
            $"but received [{string.Join(", ", actualValues)}].");
    }
}

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    AuthenticationHeaderValue? Authorization,
    string? IdempotencyKey,
    string? ClientCancelToken,
    string? Body);

internal sealed class RecordingHttpHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, HttpResponseMessage> responder;

    internal RecordingHttpHandler(Func<RecordedRequest, HttpResponseMessage> responder)
    {
        this.responder = responder;
    }

    internal List<RecordedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri ?? throw new InvalidOperationException("Request URI is missing."),
            request.Headers.Authorization,
            GetHeader(request, "Idempotency-Key"),
            GetHeader(request, "X-EmbodiedLab-Cancel-Token"),
            request.Content == null ? null : await request.Content.ReadAsStringAsync());
        Requests.Add(recorded);
        return responder(recorded);
    }

    private static string? GetHeader(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? values.Single()
            : null;
}

internal sealed class CoordinatedArtifactHandler : HttpMessageHandler
{
    private readonly byte[] first;
    private readonly byte[] second;
    private readonly TaskCompletionSource<bool> bothRequests = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int requestCount;

    internal CoordinatedArtifactHandler(byte[] first, byte[] second)
    {
        this.first = first;
        this.second = second;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref requestCount) == 2)
        {
            bothRequests.TrySetResult(true);
        }

        await bothRequests.Task.WaitAsync(cancellationToken);
        string path = request.RequestUri?.AbsolutePath ?? throw new InvalidOperationException(
            "Artifact request URI is missing.");
        byte[] content = path.EndsWith("/first.onnx", StringComparison.Ordinal)
            ? first
            : path.EndsWith("/second.onnx", StringComparison.Ordinal)
                ? second
                : throw new InvalidOperationException($"Unexpected artifact path: {path}");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        };
    }
}

internal sealed class RepeatingReadStream : Stream
{
    private long remaining;

    internal RepeatingReadStream(long length)
    {
        remaining = length;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = (int)Math.Min(remaining, count);
        if (read == 0)
        {
            return 0;
        }

        Array.Fill(buffer, (byte)'x', offset, read);
        remaining -= read;
        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read(buffer, offset, count));
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}

internal sealed class InterruptedReadStream : Stream
{
    private int bytesBeforeFailure;

    internal InterruptedReadStream(int bytesBeforeFailure)
    {
        this.bytesBeforeFailure = bytesBeforeFailure;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (bytesBeforeFailure == 0)
        {
            throw new IOException("Simulated interrupted artifact response.");
        }

        int read = Math.Min(bytesBeforeFailure, count);
        Array.Fill(buffer, (byte)'x', offset, read);
        bytesBeforeFailure -= read;
        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(Read(buffer, offset, count));
        }
        catch (Exception exception)
        {
            return Task.FromException<int>(exception);
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}

internal sealed record ScriptedFrame(
    string Text,
    WebSocketMessageType MessageType,
    bool EndOfMessage,
    TimeSpan Delay);

internal sealed class ScriptedWebSocket : IResultWebSocket
{
    private readonly Queue<ScriptedFrame> frames;
    private readonly Exception? connectException;
    private readonly bool blockAfterFrames;

    internal ScriptedWebSocket(params ScriptedFrame[] frames)
        : this(frames, blockAfterFrames: false)
    {
    }

    internal ScriptedWebSocket(IEnumerable<ScriptedFrame> frames, bool blockAfterFrames)
    {
        this.frames = new Queue<ScriptedFrame>(frames);
        this.blockAfterFrames = blockAfterFrames;
    }

    internal ScriptedWebSocket(Exception connectException)
    {
        this.connectException = connectException;
        frames = new Queue<ScriptedFrame>();
    }

    public WebSocketState State { get; private set; } = WebSocketState.None;

    internal Uri? ConnectedUri { get; private set; }

    internal bool AbortCalled { get; private set; }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ConnectedUri = uri;
        if (connectException != null)
        {
            return Task.FromException(connectException);
        }

        State = WebSocketState.Open;
        return Task.CompletedTask;
    }

    public async Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken)
    {
        if (frames.Count == 0)
        {
            if (blockAfterFrames)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            State = WebSocketState.Closed;
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        }

        ScriptedFrame frame = frames.Dequeue();
        if (frame.Delay > TimeSpan.Zero)
        {
            await Task.Delay(frame.Delay, cancellationToken);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(frame.Text);
        if (bytes.Length > buffer.Count)
        {
            throw new InvalidOperationException("Scripted frame exceeds the receive buffer.");
        }

        Array.Copy(bytes, 0, buffer.Array!, buffer.Offset, bytes.Length);
        if (frame.MessageType == WebSocketMessageType.Close)
        {
            State = WebSocketState.CloseReceived;
        }

        return new WebSocketReceiveResult(
            bytes.Length,
            frame.MessageType,
            frame.EndOfMessage);
    }

    public void Abort()
    {
        AbortCalled = true;
        State = WebSocketState.Aborted;
    }

    public void Dispose()
    {
        State = WebSocketState.Closed;
    }
}

internal sealed class QueueWebSocketFactory : IResultWebSocketFactory
{
    private readonly Queue<IResultWebSocket> sockets;

    internal QueueWebSocketFactory(params IResultWebSocket[] sockets)
    {
        this.sockets = new Queue<IResultWebSocket>(sockets);
    }

    public IResultWebSocket Create()
    {
        if (sockets.Count == 0)
        {
            throw new InvalidOperationException("No scripted WebSocket remains.");
        }

        return sockets.Dequeue();
    }
}
