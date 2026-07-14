#nullable enable

using System;

namespace EmbodiedLab.Unity.Internal
{
    internal sealed class ResultMonitorTiming
    {
        internal static ResultMonitorTiming Default { get; } = new(
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30));

        internal ResultMonitorTiming(
            TimeSpan connectTimeout,
            TimeSpan silenceTimeout,
            TimeSpan initialReconnectDelay,
            TimeSpan maximumReconnectDelay)
        {
            if (connectTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(connectTimeout));
            }

            if (silenceTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(silenceTimeout));
            }

            if (initialReconnectDelay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(initialReconnectDelay));
            }

            if (maximumReconnectDelay < initialReconnectDelay)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumReconnectDelay));
            }

            ConnectTimeout = connectTimeout;
            SilenceTimeout = silenceTimeout;
            InitialReconnectDelay = initialReconnectDelay;
            MaximumReconnectDelay = maximumReconnectDelay;
        }

        internal TimeSpan ConnectTimeout { get; }

        internal TimeSpan SilenceTimeout { get; }

        internal TimeSpan InitialReconnectDelay { get; }

        internal TimeSpan MaximumReconnectDelay { get; }

        internal TimeSpan GetReconnectDelay(int failureCount)
        {
            if (failureCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(failureCount));
            }

            long delayTicks = InitialReconnectDelay.Ticks;
            long maximumTicks = MaximumReconnectDelay.Ticks;
            for (int index = 1; index < failureCount && delayTicks < maximumTicks; index++)
            {
                delayTicks = Math.Min(maximumTicks, delayTicks > maximumTicks / 2
                    ? maximumTicks
                    : delayTicks * 2);
            }

            return TimeSpan.FromTicks(delayTicks);
        }
    }
}
