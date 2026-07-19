#nullable enable

using System;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal sealed class QuickstartModeCoordinator
    {
        private readonly Action stopReplay;
        private readonly Action stopInference;

        internal QuickstartModeCoordinator(Action stopReplay, Action stopInference)
        {
            this.stopReplay = stopReplay ?? throw new ArgumentNullException(nameof(stopReplay));
            this.stopInference = stopInference ??
                throw new ArgumentNullException(nameof(stopInference));
        }

        internal void EnterReplay()
        {
            stopInference();
        }

        internal void EnterInference()
        {
            stopReplay();
        }

        internal void Clear()
        {
            stopReplay();
            stopInference();
        }
    }
}
