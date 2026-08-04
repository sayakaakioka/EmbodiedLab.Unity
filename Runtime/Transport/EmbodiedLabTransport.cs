#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EmbodiedLab.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EmbodiedLab.Unity.Internal
{
    internal sealed class EmbodiedLabTransport : IDisposable
    {
        private const string JsonMediaType = "application/json";
        private const string IdempotencyKeyHeader = "Idempotency-Key";
        private const string ClientCancelTokenHeader = "X-EmbodiedLab-Cancel-Token";
        private const string PublicGcsBaseUrl = "https://storage.googleapis.com";
        private const long MaximumJsonArtifactBytes = 1024L * 1024L;
        private const long MaximumReplayArtifactBytes = 64L * 1024L * 1024L;
        private const long MaximumModelArtifactBytes = 1024L * 1024L * 1024L;
        private const int MaximumResultMessageBytes = 1024 * 1024;
        private const int MaximumJsonResponseBytes = 1024 * 1024;
        private const int MaximumErrorResponseBytes = 64 * 1024;

        private static readonly JsonSerializerSettings SerializerSettings = new()
        {
            MissingMemberHandling = MissingMemberHandling.Error,
        };

        private readonly Uri apiBaseUri;
        private readonly Uri resultWebSocketBaseUri;
        private readonly HttpClient httpClient;
        private readonly IResultWebSocketFactory webSocketFactory;
        private readonly ResultMonitorTiming monitorTiming;
        private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;
        private readonly SemaphoreSlim artifactCommitGate = new(1, 1);

        internal EmbodiedLabTransport(Uri apiBaseUri, Uri resultWebSocketBaseUri)
            : this(
                apiBaseUri,
                resultWebSocketBaseUri,
                new HttpClient(),
                new ClientResultWebSocketFactory(),
                ResultMonitorTiming.Default,
                Task.Delay)
        {
        }

        internal EmbodiedLabTransport(
            Uri apiBaseUri,
            Uri resultWebSocketBaseUri,
            HttpClient httpClient,
            IResultWebSocketFactory webSocketFactory,
            ResultMonitorTiming monitorTiming,
            Func<TimeSpan, CancellationToken, Task> delayAsync)
        {
            this.apiBaseUri = NormalizeBaseUri(
                apiBaseUri,
                nameof(apiBaseUri),
                "http",
                "https");
            this.resultWebSocketBaseUri = NormalizeBaseUri(
                resultWebSocketBaseUri,
                nameof(resultWebSocketBaseUri),
                "ws",
                "wss");
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.webSocketFactory = webSocketFactory ??
                throw new ArgumentNullException(nameof(webSocketFactory));
            this.monitorTiming = monitorTiming ??
                throw new ArgumentNullException(nameof(monitorTiming));
            this.delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
        }

        internal async Task<SubmissionResponse> SubmitAsync(
            ScenarioBundle scenario,
            CancellationToken cancellationToken)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            string idempotencyKey = CreateRecoveryValue();
            string cancelToken = CreateRecoveryValue();
            var recoveryHeaders = new Dictionary<string, string>
            {
                [IdempotencyKeyHeader] = idempotencyKey,
                [ClientCancelTokenHeader] = cancelToken,
            };
            int attempt = 0;
            while (true)
            {
                try
                {
                    SubmissionResponse response = await SendJsonAsync<SubmissionResponse>(
                            HttpMethod.Post,
                            BuildApiUri("submissions"),
                            JsonConvert.SerializeObject(scenario, SerializerSettings),
                            authorization: null,
                            cancellationToken,
                            recoveryHeaders)
                        .ConfigureAwait(false);
                    if (!string.Equals(
                            response.CancelToken,
                            cancelToken,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "EmbodiedLab returned a different cancellation capability.");
                    }

                    return response;
                }
                catch (Exception exception) when (
                    attempt == 0 &&
                    IsAmbiguousSubmissionFailure(exception, cancellationToken))
                {
                    attempt++;
                }
            }
        }

        internal Task<ResultDocument> GetResultAsync(
            string submissionId,
            CancellationToken cancellationToken)
        {
            return SendJsonAsync<ResultDocument>(
                HttpMethod.Get,
                BuildApiUri("results", RequireValue(submissionId, nameof(submissionId))),
                requestJson: null,
                authorization: null,
                cancellationToken);
        }

        internal Task<ResultDocument> CancelAsync(
            string submissionId,
            string cancelToken,
            CancellationToken cancellationToken)
        {
            AuthenticationHeaderValue authorization = new(
                "Bearer",
                RequireValue(cancelToken, nameof(cancelToken)));
            return SendJsonAsync<ResultDocument>(
                HttpMethod.Post,
                BuildApiUri("submissions", RequireValue(submissionId, nameof(submissionId)), "cancel"),
                requestJson: null,
                authorization,
                cancellationToken);
        }

        internal async Task MonitorResultAsync(
            string submissionId,
            Action<ResultDocument> onResult,
            CancellationToken cancellationToken)
        {
            string requiredSubmissionId = RequireValue(submissionId, nameof(submissionId));
            if (onResult == null)
            {
                throw new ArgumentNullException(nameof(onResult));
            }

            Uri streamUri = BuildResultWebSocketUri(requiredSubmissionId);
            int consecutiveFailures = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using IResultWebSocket socket = webSocketFactory.Create();
                try
                {
                    await ConnectAsync(socket, streamUri, cancellationToken).ConfigureAwait(false);
                    while (socket.State == WebSocketState.Open)
                    {
                        string payload = await ReceiveMessageAsync(socket, cancellationToken)
                            .ConfigureAwait(false);
                        ResultDocument? result = ParseResultMessage(payload, requiredSubmissionId);
                        if (result == null)
                        {
                            continue;
                        }

                        consecutiveFailures = 0;
                        onResult(result);
                        if (IsTerminal(result.Status))
                        {
                            return;
                        }
                    }

                    throw new ResultStreamDisconnectedException();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsRecoverableStreamFailure(exception))
                {
                    socket.Abort();
                }

                ResultDocument? reconciled = await TryGetResultAsync(
                        requiredSubmissionId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (reconciled != null)
                {
                    onResult(reconciled);
                    if (IsTerminal(reconciled.Status))
                    {
                        return;
                    }
                }

                consecutiveFailures++;
                TimeSpan reconnectDelay = monitorTiming.GetReconnectDelay(consecutiveFailures);
                await delayAsync(reconnectDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        internal async Task DownloadArtifactAsync(
            ArtifactDownloadRequest artifact,
            string destinationPath,
            CancellationToken cancellationToken,
            Action<string, CancellationToken>? validateTemporaryFile = null)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            string requiredDestinationPath = RequireValue(
                destinationPath,
                nameof(destinationPath));
            long maximumBytes = GetMaximumArtifactBytes(artifact.Format);
            ValidateArtifactMetadata(artifact, maximumBytes);
            string fullDestinationPath = Path.GetFullPath(requiredDestinationPath);
            string? directory = Path.GetDirectoryName(fullDestinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = fullDestinationPath + "." +
                Guid.NewGuid().ToString("N") + ".part";

            try
            {
                using HttpRequestMessage request = new(
                    HttpMethod.Get,
                    BuildPublicArtifactUri(artifact));
                using HttpResponseMessage response = await httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    string responseBody = await ReadBoundedUtf8Async(
                            response.Content,
                            MaximumErrorResponseBytes,
                            "Artifact error response",
                            cancellationToken)
                        .ConfigureAwait(false);
                    throw new EmbodiedLabTransportException(
                        response.StatusCode,
                        responseBody,
                        request.RequestUri ?? throw new InvalidOperationException(
                            "Artifact request URI is missing."));
                }

                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value != artifact.SizeBytes)
                {
                    throw new InvalidDataException(
                        "Artifact content length does not match the declared size.");
                }

                using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var limitedSource = new ResourceLimitedReadStream(
                    source,
                    maximumBytes,
                    $"{artifact.Format} artifact",
                    leaveOpen: true);
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long writtenBytes = 0;
                using (var destination = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = await limitedSource.ReadAsync(
                        buffer,
                        0,
                        buffer.Length,
                        cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        writtenBytes += read;
                        if (writtenBytes > artifact.SizeBytes)
                        {
                            throw new InvalidDataException(
                                "Artifact exceeds its declared size.");
                        }

                        digest.AppendData(buffer, 0, read);
                        await destination.WriteAsync(
                            buffer,
                            0,
                            read,
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                if (writtenBytes != artifact.SizeBytes)
                {
                    throw new InvalidDataException(
                        "Artifact byte count does not match the declared size.");
                }

                string actualSha256 = BitConverter.ToString(digest.GetHashAndReset())
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
                if (!string.Equals(
                    actualSha256,
                    artifact.Sha256,
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Artifact SHA-256 does not match the declared digest.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                validateTemporaryFile?.Invoke(temporaryPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                await artifactCommitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (File.Exists(fullDestinationPath))
                    {
                        File.Replace(temporaryPath, fullDestinationPath, null);
                    }
                    else
                    {
                        File.Move(temporaryPath, fullDestinationPath);
                    }
                }
                finally
                {
                    artifactCommitGate.Release();
                }
            }
            catch
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                throw;
            }
        }

        internal static Uri BuildPublicArtifactUri(ArtifactDownloadRequest artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            if (artifact.Storage != ArtifactStorage.Gcs)
            {
                throw new ArgumentException("Only public GCS artifacts are supported.", nameof(artifact));
            }

            string bucket = RequireValue(artifact.Bucket, nameof(artifact.Bucket));
            string path = RequireValue(artifact.Path, nameof(artifact.Path));
            string escapedPath = string.Join(
                "/",
                path.Split('/').Select(Uri.EscapeDataString));
            return new Uri(
                $"{PublicGcsBaseUrl}/{Uri.EscapeDataString(bucket)}/{escapedPath}",
                UriKind.Absolute);
        }

        internal static long GetMaximumArtifactBytes(string format)
        {
            return format switch
            {
                "json" => MaximumJsonArtifactBytes,
                "jsonl" => MaximumReplayArtifactBytes,
                "jsonl.gz" => MaximumReplayArtifactBytes,
                "onnx" => MaximumModelArtifactBytes,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(format),
                    format,
                    "Unsupported artifact format."),
            };
        }

        private static void ValidateArtifactMetadata(
            ArtifactDownloadRequest artifact,
            long maximumBytes)
        {
            if (artifact.SizeBytes < 0 || artifact.SizeBytes > maximumBytes)
            {
                throw new InvalidDataException(
                    "Artifact declared size is outside the supported range.");
            }

            if (artifact.Sha256.Length != 64 ||
                artifact.Sha256.Any(character =>
                    !(character >= '0' && character <= '9') &&
                    !(character >= 'a' && character <= 'f')))
            {
                throw new InvalidDataException(
                    "Artifact SHA-256 must be 64 lowercase hexadecimal characters.");
            }
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }

        internal static Uri NormalizeBaseUri(
            Uri uri,
            string parameterName,
            string plaintextScheme,
            string encryptedScheme)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (!uri.IsAbsoluteUri ||
                (!uri.Scheme.Equals(plaintextScheme, StringComparison.OrdinalIgnoreCase) &&
                    !uri.Scheme.Equals(encryptedScheme, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    $"URI must be absolute and use {plaintextScheme} or {encryptedScheme}.",
                    parameterName);
            }

            if (uri.Scheme.Equals(plaintextScheme, StringComparison.OrdinalIgnoreCase) &&
                !uri.IsLoopback)
            {
                throw new ArgumentException(
                    $"Non-loopback endpoints must use {encryptedScheme}.",
                    parameterName);
            }

            if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new ArgumentException(
                    "Base URI cannot contain a query or fragment.",
                    parameterName);
            }

            string value = uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? uri.AbsoluteUri
                : uri.AbsoluteUri + "/";
            return new Uri(value, UriKind.Absolute);
        }

        private static string RequireValue(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value cannot be empty.", parameterName);
            }

            return value;
        }

        private static string CreateRecoveryValue()
        {
            byte[] bytes = new byte[32];
            using RandomNumberGenerator generator = RandomNumberGenerator.Create();
            generator.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static bool IsAmbiguousSubmissionFailure(
            Exception exception,
            CancellationToken cancellationToken)
        {
            return exception is HttpRequestException ||
                exception is JsonException ||
                (exception is TaskCanceledException &&
                    !cancellationToken.IsCancellationRequested);
        }

        private Uri BuildApiUri(params string[] segments)
        {
            return BuildRelativeUri(apiBaseUri, segments);
        }

        private Uri BuildResultWebSocketUri(string submissionId)
        {
            return BuildRelativeUri(resultWebSocketBaseUri, "ws", "results", submissionId);
        }

        private static Uri BuildRelativeUri(Uri baseUri, params string[] segments)
        {
            string relativePath = string.Join("/", segments.Select(Uri.EscapeDataString));
            return new Uri(baseUri, relativePath);
        }

        private async Task<T> SendJsonAsync<T>(
            HttpMethod method,
            Uri requestUri,
            string? requestJson,
            AuthenticationHeaderValue? authorization,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? requestHeaders = null)
        {
            using HttpRequestMessage request = new(method, requestUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMediaType));
            request.Headers.Authorization = authorization;
            if (requestHeaders != null)
            {
                foreach (KeyValuePair<string, string> header in requestHeaders)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            if (requestJson != null)
            {
                request.Content = new StringContent(requestJson, Encoding.UTF8, JsonMediaType);
            }

            using HttpResponseMessage response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            int maximumResponseBytes = response.IsSuccessStatusCode
                ? MaximumJsonResponseBytes
                : MaximumErrorResponseBytes;
            string responseBody = await ReadBoundedUtf8Async(
                    response.Content,
                    maximumResponseBytes,
                    response.IsSuccessStatusCode
                        ? "EmbodiedLab JSON response"
                        : "EmbodiedLab error response",
                    cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new EmbodiedLabTransportException(
                    response.StatusCode,
                    responseBody,
                    requestUri);
            }

            T result = JsonConvert.DeserializeObject<T>(responseBody, SerializerSettings)
                ?? throw new JsonSerializationException(
                    $"EmbodiedLab returned an empty {typeof(T).Name} response.");
            if (result is ResultDocument resultDocument)
            {
                ContractSemanticValidator.ValidateResultDocument(resultDocument);
            }

            return result;
        }

        private static async Task<string> ReadBoundedUtf8Async(
            HttpContent content,
            int maximumBytes,
            string resourceName,
            CancellationToken cancellationToken)
        {
            long? contentLength = content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > maximumBytes)
            {
                throw new InvalidDataException(
                    $"{resourceName} exceeds the maximum size of {maximumBytes} bytes.");
            }

            using Stream source = await content.ReadAsStreamAsync().ConfigureAwait(false);
            using var limited = new ResourceLimitedReadStream(
                source,
                maximumBytes,
                resourceName,
                leaveOpen: true);
            using var payload = new MemoryStream();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = await limited.ReadAsync(
                buffer,
                0,
                buffer.Length,
                cancellationToken).ConfigureAwait(false)) > 0)
            {
                payload.Write(buffer, 0, read);
            }

            return new UTF8Encoding(false, true).GetString(payload.ToArray());
        }

        private async Task ConnectAsync(
            IResultWebSocket socket,
            Uri streamUri,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(monitorTiming.ConnectTimeout);
            try
            {
                await socket.ConnectAsync(streamUri, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                socket.Abort();
                throw new ResultStreamTimeoutException("WebSocket connection timed out.");
            }
        }

        private async Task<string> ReceiveMessageAsync(
            IResultWebSocket socket,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            using var payload = new MemoryStream();
            using CancellationTokenSource messageTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            messageTimeout.CancelAfter(monitorTiming.SilenceTimeout);
            try
            {
                while (true)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            messageTimeout.Token)
                        .ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        throw new ResultStreamDisconnectedException();
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        throw new JsonSerializationException(
                            "EmbodiedLab result stream returned a non-text message.");
                    }

                    if (payload.Length > MaximumResultMessageBytes - result.Count)
                    {
                        socket.Abort();
                        throw new InvalidDataException(
                            $"EmbodiedLab result message exceeds {MaximumResultMessageBytes} bytes.");
                    }

                    payload.Write(buffer, 0, result.Count);
                    if (result.EndOfMessage)
                    {
                        return Encoding.UTF8.GetString(payload.ToArray());
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                socket.Abort();
                throw new ResultStreamTimeoutException(
                    "WebSocket result message timed out.");
            }
        }

        private static ResultDocument? ParseResultMessage(string payload, string submissionId)
        {
            JObject message = JObject.Parse(payload);
            if (string.Equals(
                message.Value<string>("type"),
                "connected",
                StringComparison.Ordinal))
            {
                if (!string.Equals(
                    message.Value<string>("submission_id"),
                    submissionId,
                    StringComparison.Ordinal))
                {
                    throw new JsonSerializationException(
                        "Result stream connected to a different submission.");
                }

                return null;
            }

            ResultDocument result = message.ToObject<ResultDocument>(
                    JsonSerializer.Create(SerializerSettings))
                ?? throw new JsonSerializationException("Result stream message was empty.");
            if (!string.Equals(result.SubmissionId, submissionId, StringComparison.Ordinal))
            {
                throw new JsonSerializationException(
                    "Result stream message belongs to a different submission.");
            }

            ContractSemanticValidator.ValidateResultDocument(result);

            return result;
        }

        private async Task<ResultDocument?> TryGetResultAsync(
            string submissionId,
            CancellationToken cancellationToken)
        {
            try
            {
                return await GetResultAsync(submissionId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (EmbodiedLabTransportException)
            {
                return null;
            }
        }

        private static bool IsTerminal(ResultStatus status)
        {
            return status == ResultStatus.Completed ||
                status == ResultStatus.Failed ||
                status == ResultStatus.Cancelled;
        }

        private static bool IsRecoverableStreamFailure(Exception exception)
        {
            return exception is WebSocketException ||
                exception is HttpRequestException ||
                exception is ObjectDisposedException ||
                exception is ResultStreamDisconnectedException ||
                exception is ResultStreamTimeoutException;
        }

        private sealed class ResultStreamDisconnectedException : Exception
        {
        }

        private sealed class ResultStreamTimeoutException : TimeoutException
        {
            internal ResultStreamTimeoutException(string message)
                : base(message)
            {
            }
        }
    }

    internal sealed class ArtifactDownloadRequest
    {
        internal ArtifactDownloadRequest(
            ArtifactStorage storage,
            string bucket,
            string path,
            string format,
            long sizeBytes,
            string sha256)
        {
            Storage = storage;
            Bucket = bucket ?? throw new ArgumentNullException(nameof(bucket));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Format = format ?? throw new ArgumentNullException(nameof(format));
            SizeBytes = sizeBytes;
            Sha256 = sha256 ?? throw new ArgumentNullException(nameof(sha256));
        }

        internal ArtifactStorage Storage { get; }

        internal string Bucket { get; }

        internal string Path { get; }

        internal string Format { get; }

        internal long SizeBytes { get; }

        internal string Sha256 { get; }
    }
}
