#nullable enable

using EmbodiedLab.Contracts;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal static class QuickstartProgressText
    {
        internal static string Format(ResultStatus status, Progress? progress)
        {
            return progress == null
                ? "-"
                : Format(status, progress.Phase, progress.CurrentStep, progress.TotalSteps, progress.Message);
        }

        internal static string Format(
            ResultStatus status, ResultStatus phase, int currentStep, int totalSteps, string message)
        {
            if (status == ResultStatus.Queued && totalSteps <= 0)
            {
                return "Waiting for the trainer to start.";
            }

            if (totalSteps <= 0)
            {
                return string.IsNullOrWhiteSpace(message)
                    ? phase.ToString()
                    : $"{phase}: {message}";
            }

            string suffix = string.IsNullOrWhiteSpace(message)
                ? string.Empty
                : $" {message}";
            return $"{phase}: {currentStep}/{totalSteps}{suffix}";
        }
    }
}
