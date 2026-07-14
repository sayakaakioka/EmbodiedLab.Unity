#nullable enable

using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace EmbodiedLab.Unity.Internal
{
    internal interface IResultWebSocket : IDisposable
    {
        WebSocketState State { get; }

        Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

        Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken);

        void Abort();
    }

    internal interface IResultWebSocketFactory
    {
        IResultWebSocket Create();
    }

    internal sealed class ClientResultWebSocketFactory : IResultWebSocketFactory
    {
        public IResultWebSocket Create()
        {
            return new ClientResultWebSocket();
        }
    }

    internal sealed class ClientResultWebSocket : IResultWebSocket
    {
        private readonly ClientWebSocket socket = new();

        public WebSocketState State => socket.State;

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            return socket.ConnectAsync(uri, cancellationToken);
        }

        public Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            return socket.ReceiveAsync(buffer, cancellationToken);
        }

        public void Abort()
        {
            socket.Abort();
        }

        public void Dispose()
        {
            socket.Dispose();
        }
    }
}
