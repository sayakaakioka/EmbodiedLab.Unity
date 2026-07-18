using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity;
using EmbodiedLab.Unity.Internal;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Endpoint encryption", TestEndpointEncryptionAsync),
    ("HTTP contracts", TestHttpContractsAsync),
    ("WebSocket primary", TestHealthyWebSocketUsesNoResultGetAsync),
    ("Connect failure reconciliation", TestConnectFailureReconcilesAsync),
    ("Disconnect reconciliation", TestDisconnectReconcilesAsync),
    ("Silence reconciliation", TestSilenceReconcilesAsync),
    ("Bounded reconnect", TestReconnectDelayIsBoundedAsync),
    ("Local cancellation", TestLocalCancellationStopsMonitoringAsync),
    ("Artifact download", TestArtifactDownloadAsync),
    ("Artifact format limits", TestArtifactFormatLimits),
    ("Artifact content-length limit", TestOversizedArtifactContentLengthAsync),
    ("Artifact streaming limit", TestOversizedStreamingArtifactAsync),
    ("Incorrect artifact content length", TestIncorrectArtifactContentLengthAsync),
    ("Interrupted artifact cleanup", TestInterruptedArtifactDownloadAsync),
    ("Stateful facade", TestStatefulFacadeAsync),
    ("Facade model selection", TestFacadeModelSelectionAsync),
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
            """{"cancel_token":"capability-1","status":"accepted","submission_id":"submission-1"}"""),
        "/root/submissions/submission-1/train" => JsonResponse(
            """{"status":"accepted","submission_id":"submission-1"}"""),
        "/root/submissions/submission-1/cancel" => JsonResponse(
            ResultJson("cancelled"),
            HttpStatusCode.Accepted),
        "/root/results/submission-1" => JsonResponse(ResultJson("running")),
        _ => throw new InvalidOperationException($"Unexpected request: {request.Uri}"),
    });
    using var transport = CreateTransport(handler, new QueueWebSocketFactory());

    SubmissionResponse submitted = await transport.SubmitAsync(
        new ScenarioBundle { ScenarioId = "scenario-1" },
        CancellationToken.None);
    TrainingResponse training = await transport.StartTrainingAsync(
        submitted.SubmissionId,
        CancellationToken.None);
    ResultDocument result = await transport.GetResultAsync(
        submitted.SubmissionId,
        CancellationToken.None);
    ResultDocument cancelled = await transport.CancelAsync(
        submitted.SubmissionId,
        submitted.CancelToken,
        CancellationToken.None);

    AssertEqual("capability-1", submitted.CancelToken, "submission capability");
    AssertEqual("submission-1", training.SubmissionId, "training submission");
    AssertEqual(ResultStatus.Running, result.Status, "result status");
    AssertEqual(ResultStatus.Cancelled, cancelled.Status, "cancel status");
    AssertEqual(4, handler.Requests.Count, "HTTP request count");
    RecordedRequest cancelRequest = handler.Requests.Single(
        request => request.Uri.AbsolutePath.EndsWith("/cancel", StringComparison.Ordinal));
    AssertEqual("Bearer", cancelRequest.Authorization?.Scheme, "cancel auth scheme");
    AssertEqual("capability-1", cancelRequest.Authorization?.Parameter, "cancel auth token");
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
            new ArtifactLocation
            {
                Bucket = "bucket-name",
                Path = "folder/model one.onnx",
                Format = ArtifactFormat.Onnx,
                Storage = ArtifactStorage.Gcs,
            },
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

static Task TestArtifactFormatLimits()
{
    AssertEqual(
        1024L * 1024L,
        EmbodiedLabTransport.GetMaximumArtifactBytes(ArtifactFormat.Json),
        "JSON artifact limit");
    AssertEqual(
        64L * 1024L * 1024L,
        EmbodiedLabTransport.GetMaximumArtifactBytes(ArtifactFormat.Jsonl),
        "JSONL artifact limit");
    AssertEqual(
        64L * 1024L * 1024L,
        EmbodiedLabTransport.GetMaximumArtifactBytes(ArtifactFormat.JsonlGz),
        "compressed JSONL artifact limit");
    AssertEqual(
        1024L * 1024L * 1024L,
        EmbodiedLabTransport.GetMaximumArtifactBytes(ArtifactFormat.Onnx),
        "ONNX artifact limit");
    AssertEqual(
        1024L * 1024L * 1024L,
        EmbodiedLabTransport.GetMaximumArtifactBytes(ArtifactFormat.Zip),
        "ZIP artifact limit");
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
        "interrupted response");
}

static async Task AssertRejectedDownloadPreservesDestinationAsync<TException>(
    HttpContent content,
    string description)
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
                new ArtifactLocation
                {
                    Bucket = "bucket-name",
                    Path = "replay/manifest.json",
                    Format = ArtifactFormat.Json,
                    Storage = ArtifactStorage.Gcs,
                },
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
        AssertEqual(false, File.Exists(destination + ".part"), $"{description} temporary file");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
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
    var handler = new RecordingHttpHandler(request => request.Uri.AbsolutePath switch
    {
        "/root/results/submission-1" => JsonResponse(ResultJson("running")),
        "/root/submissions/submission-1/cancel" => JsonResponse(
            ResultJson("cancelled"),
            HttpStatusCode.Accepted),
        _ => throw new InvalidOperationException($"Unexpected request: {request.Uri}"),
    });
    using var job = new EmbodiedLabJob(
        CreateTransport(handler, new QueueWebSocketFactory()),
        "submission-1",
        "capability-1",
        synchronizationContext: null);
    var statuses = new List<ResultStatus>();
    job.ResultUpdated += result => statuses.Add(result.Status);

    ResultDocument refreshed = await job.RefreshAsync();
    ResultDocument cancelled = await job.CancelAsync();

    AssertEqual(ResultStatus.Running, refreshed.Status, "facade refreshed status");
    AssertEqual(ResultStatus.Cancelled, cancelled.Status, "facade cancelled status");
    AssertEqual(ResultStatus.Cancelled, job.LatestResult?.Status, "facade latest status");
    AssertEqual(true, job.IsTerminal, "facade terminal state");
    AssertEqual(true, job.CanCancel, "facade cancellation capability");
    AssertSequence(
        new[] { ResultStatus.Running, ResultStatus.Cancelled },
        statuses,
        "facade result events");
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
                      "scenario_id":"scenario-1",
                      "job_id":"submission-1",
                      "status":"completed",
                      "artifacts":{
                        "model":{"storage":"gcs","bucket":"models","path":"policy.zip","format":"zip"},
                        "onnx_model":{"storage":"gcs","bucket":"models","path":"policy.onnx","format":"onnx"},
                        "sentis_model":{"storage":"gcs","bucket":"models","path":"policy.sentis.onnx","format":"onnx","target":"unity-sentis"}
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

static async Task TestFacadeReplayChunkAsync()
{
    byte[] expected = Encoding.UTF8.GetBytes("compressed replay");
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
        "capability-1",
        synchronizationContext: null);
    string directory = Path.Combine(Path.GetTempPath(), $"embodiedlab-job-{Guid.NewGuid():N}");
    string destination = Path.Combine(directory, "checkpoint.jsonl.gz");

    try
    {
        await job.RefreshAsync();
        var chunk = new ReplayBundleChunk
        {
            Path = "eval/checkpoint_00005000.jsonl.gz",
            Format = ReplayBundleChunkFormat.JsonlGz,
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
    RecordingHttpHandler handler,
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
    return $$"""{"submission_id":"submission-1","status":"{{status}}"{{additionalProperties}}}""";
}

static ScriptedFrame TextFrame(string text) => new(text, WebSocketMessageType.Text);

static ScriptedFrame CloseFrame() => new(string.Empty, WebSocketMessageType.Close);

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
            request.Content == null ? null : await request.Content.ReadAsStringAsync());
        Requests.Add(recorded);
        return responder(recorded);
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

internal sealed record ScriptedFrame(string Text, WebSocketMessageType MessageType);

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

        return new WebSocketReceiveResult(bytes.Length, frame.MessageType, true);
    }

    public void Abort()
    {
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
