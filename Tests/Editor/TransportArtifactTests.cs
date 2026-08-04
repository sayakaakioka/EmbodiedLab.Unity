#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity.Internal;
using NUnit.Framework;

namespace EmbodiedLab.Unity.Tests
{
    public sealed class TransportArtifactTests
    {
        [Test]
        public void SuccessfulDownloadReplacesExistingDestination()
        {
            byte[] expected = System.Text.Encoding.UTF8.GetBytes("verified artifact");
            using var transport = CreateTransport(new StaticHandler(expected));
            WithDestination((directory, destination) =>
            {
                File.WriteAllText(destination, "existing destination");
                DownloadAsync(transport, "verified.json", expected, destination)
                    .GetAwaiter()
                    .GetResult();

                Assert.That(File.ReadAllBytes(destination), Is.EqualTo(expected));
                Assert.That(Directory.GetFiles(directory, "*.part"), Is.Empty);
            });
        }

        [Test]
        public void CancelledDownloadPreservesExistingDestination()
        {
            byte[] expected = System.Text.Encoding.UTF8.GetBytes("never committed");
            using var transport = CreateTransport(new StaticHandler(expected));
            WithDestination((directory, destination) =>
            {
                byte[] existing = System.Text.Encoding.UTF8.GetBytes("existing destination");
                File.WriteAllBytes(destination, existing);
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();

                bool cancelled = false;
                try
                {
                    DownloadAsync(
                        transport,
                        "cancelled.json",
                        expected,
                        destination,
                        cancellation.Token)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }

                Assert.That(cancelled, Is.True);
                Assert.That(File.ReadAllBytes(destination), Is.EqualTo(existing));
                Assert.That(Directory.GetFiles(directory, "*.part"), Is.Empty);
            });
        }

        [Test]
        public void ConcurrentDownloadsCommitOnlyVerifiedArtifacts()
        {
            byte[] first = System.Text.Encoding.UTF8.GetBytes("first verified artifact");
            byte[] second = System.Text.Encoding.UTF8.GetBytes("second verified artifact");
            using var transport = CreateTransport(
                new CoordinatedHandler(first, second));
            WithDestination((directory, destination) =>
            {
                File.WriteAllText(destination, "existing destination");
                Task firstDownload = DownloadAsync(
                    transport,
                    "first.json",
                    first,
                    destination);
                Task secondDownload = DownloadAsync(
                    transport,
                    "second.json",
                    second,
                    destination);

                Task.WhenAll(firstDownload, secondDownload).GetAwaiter().GetResult();
                byte[] actual = File.ReadAllBytes(destination);
                Assert.That(
                    actual.SequenceEqual(first) || actual.SequenceEqual(second),
                    Is.True);
                Assert.That(Directory.GetFiles(directory, "*.part"), Is.Empty);
            });
        }

        private static EmbodiedLabTransport CreateTransport(HttpMessageHandler handler)
        {
            return new EmbodiedLabTransport(
                new Uri("https://api.example.test/"),
                new Uri("wss://stream.example.test/"),
                new HttpClient(handler),
                new UnusedWebSocketFactory(),
                ResultMonitorTiming.Default,
                Task.Delay);
        }

        private static Task DownloadAsync(
            EmbodiedLabTransport transport,
            string path,
            byte[] expected,
            string destination,
            CancellationToken cancellationToken = default)
        {
            return transport.DownloadArtifactAsync(
                new ArtifactDownloadRequest(
                    ArtifactStorage.Gcs,
                    "artifacts",
                    path,
                    "json",
                    expected.LongLength,
                    ComputeSha256(expected)),
                destination,
                cancellationToken);
        }

        private static void WithDestination(Action<string, string> test)
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                $"embodiedlab-unity-transport-{Guid.NewGuid():N}");
            string destination = Path.Combine(directory, "artifact.json");
            try
            {
                Directory.CreateDirectory(directory);
                test(directory, destination);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static string ComputeSha256(byte[] value)
        {
            using SHA256 digest = SHA256.Create();
            return BitConverter.ToString(digest.ComputeHash(value))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private sealed class StaticHandler : HttpMessageHandler
        {
            private readonly byte[] content;

            internal StaticHandler(byte[] content)
            {
                this.content = content;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content),
                });
            }
        }

        private sealed class CoordinatedHandler : HttpMessageHandler
        {
            private readonly byte[] first;
            private readonly byte[] second;
            private readonly TaskCompletionSource<bool> bothRequests = new();
            private int requestCount;

            internal CoordinatedHandler(byte[] first, byte[] second)
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

                await bothRequests.Task;
                string path = request.RequestUri?.AbsolutePath ??
                    throw new InvalidOperationException("Artifact URI is missing.");
                byte[] content = path.EndsWith("/first.json", StringComparison.Ordinal)
                    ? first
                    : second;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content),
                };
            }
        }

        private sealed class UnusedWebSocketFactory : IResultWebSocketFactory
        {
            public IResultWebSocket Create()
            {
                throw new InvalidOperationException(
                    "Artifact tests must not create a WebSocket.");
            }
        }
    }
}
