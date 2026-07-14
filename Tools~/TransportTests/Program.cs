using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity.Internal;

var tests = new (string Name, Func<Task> Run)[]
{
    ("HTTP contracts", TestHttpContractsAsync),
    ("WebSocket primary", TestHealthyWebSocketUsesNoResultGetAsync),
    ("Connect failure reconciliation", TestConnectFailureReconcilesAsync),
    ("Disconnect reconciliation", TestDisconnectReconcilesAsync),
    ("Silence reconciliation", TestSilenceReconcilesAsync),
    ("Bounded reconnect", TestReconnectDelayIsBoundedAsync),
    ("Local cancellation", TestLocalCancellationStopsMonitoringAsync),
    ("Artifact download", TestArtifactDownloadAsync),
};

foreach ((string name, Func<Task> run) in tests)
{
    await run();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"Validated {tests.Length} transport behaviors.");
return 0;

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

static string ResultJson(string status)
{
    return $$"""{"submission_id":"submission-1","status":"{{status}}"}""";
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
