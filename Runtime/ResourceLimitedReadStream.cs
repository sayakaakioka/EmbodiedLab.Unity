#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EmbodiedLab.Unity.Internal
{
    internal sealed class ResourceLimitedReadStream : Stream
    {
        private readonly Stream source;
        private readonly long maximumBytes;
        private readonly string resourceName;
        private readonly bool leaveOpen;
        private long bytesRead;

        internal ResourceLimitedReadStream(
            Stream source,
            long maximumBytes,
            string resourceName,
            bool leaveOpen = false)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            if (maximumBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                throw new ArgumentException("Resource name cannot be empty.", nameof(resourceName));
            }

            this.maximumBytes = maximumBytes;
            this.resourceName = resourceName;
            this.leaveOpen = leaveOpen;
        }

        public override bool CanRead => source.CanRead;

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
            int read = source.Read(buffer, offset, count);
            RecordRead(read);
            return read;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            int read = await source.ReadAsync(buffer, offset, count, cancellationToken)
                .ConfigureAwait(false);
            RecordRead(read);
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen)
            {
                source.Dispose();
            }

            base.Dispose(disposing);
        }

        private void RecordRead(int count)
        {
            if (count > maximumBytes - bytesRead)
            {
                throw new InvalidDataException(
                    $"{resourceName} exceeds the maximum size of {maximumBytes} bytes.");
            }

            bytesRead += count;
        }
    }
}
