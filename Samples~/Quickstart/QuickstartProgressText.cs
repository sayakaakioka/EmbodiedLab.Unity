#nullable enable

using EmbodiedLab.Contracts;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal static class QuickstartProgressText
    {
        internal static string Format(ResultStatus status, Progress? progress)
        {
            if (progress == null)
            {
                return "-";
            }

            if (status == ResultStatus.Queued && progress.TotalSteps <= 0)
            {
                return "Waiting for the trainer to start.";
            }

            if (progress.TotalSteps <= 0)
            {
                return string.IsNullOrWhiteSpace(progress.Message)
                    ? progress.Phase.ToString()
                    : $"{progress.Phase}: {progress.Message}";
            }

            string message = string.IsNullOrWhiteSpace(progress.Message)
                ? string.Empty
                : $" {progress.Message}";
            return $"{progress.Phase}: {progress.CurrentStep}/{progress.TotalSteps}{message}";
        }
    }
}
