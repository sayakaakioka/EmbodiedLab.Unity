#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using EmbodiedLab.Contracts;
using EmbodiedLab.Unity;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    internal sealed class QuickstartCloudJob : IDisposable
    {
        private enum Operation
        {
            None,
            Submitting,
            Cancelling,
        }

        private CancellationTokenSource? monitorCancellation;
        private EmbodiedLabJob? job;
        private Operation operation;
        private bool disposed;
        private int monitorGeneration;

        internal event Action<string>? ActivityChanged;

        internal event Action<string, Exception>? Failed;

        internal event Action<ResultDocument>? ResultUpdated;

        internal event Action<string>? SubmissionStarted;

        internal EmbodiedLabJob? Job => job;

        internal bool IsBusy => operation != Operation.None;

        internal bool IsMonitoring { get; private set; }

        internal bool CanCancel =>
            !disposed &&
            operation == Operation.None &&
            job != null &&
            job.CanCancel &&
            !job.IsTerminal;

        internal async Task SubmitAndMonitorAsync(
            EmbodiedLabEndpoints endpoints,
            ScenarioBundle scenario,
            CancellationToken lifetimeToken)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(QuickstartCloudJob));
            }

            if (IsBusy || IsMonitoring)
            {
                throw new InvalidOperationException(
                    "Another cloud job operation is already active.");
            }

            operation = Operation.Submitting;
            StopCurrentJob();
            EmbodiedLabJob? submittedJob = null;
            int generation = -1;
            try
            {
                ReportActivity("Submitting the scenario for server-owned training...");
                submittedJob = await EmbodiedLabJob.SubmitAsync(
                    endpoints,
                    scenario,
                    lifetimeToken);

                if (disposed)
                {
                    return;
                }

                AttachJob(submittedJob);
                submittedJob = null;
                EmbodiedLabJob activeJob = job!;
                SubmissionStarted?.Invoke(activeJob.SubmissionId);
                (CancellationToken token, int operationGeneration) =
                    StartMonitor(lifetimeToken);
                generation = operationGeneration;
                operation = Operation.None;
                ReportActivity("Monitoring result updates...");

                ResultDocument result = await activeJob.WaitForCompletionAsync(token);
                ResultUpdated?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
                if (!disposed && generation == monitorGeneration)
                {
                    ReportActivity(
                        "Local monitoring stopped. The cloud job may still be running.");
                }
            }
            catch (Exception exception)
            {
                Failed?.Invoke(
                    generation >= 0
                        ? "Result monitoring failed"
                        : "Submission failed",
                    exception);
            }
            finally
            {
                if (operation == Operation.Submitting)
                {
                    operation = Operation.None;
                }

                submittedJob?.Dispose();
                FinishMonitor(generation);
            }
        }

        internal async Task CancelAsync(CancellationToken cancellationToken)
        {
            if (!CanCancel)
            {
                return;
            }

            EmbodiedLabJob activeJob = job!;
            operation = Operation.Cancelling;
            try
            {
                ReportActivity("Requesting cloud cancellation...");
                ResultDocument result = await activeJob.CancelAsync(cancellationToken);
                if (ReferenceEquals(job, activeJob))
                {
                    ResultUpdated?.Invoke(result);
                }
            }
            catch (OperationCanceledException)
            {
                if (!disposed)
                {
                    ReportActivity("The local cancellation request stopped.");
                }
            }
            catch (Exception exception)
            {
                Failed?.Invoke("Cloud cancellation failed", exception);
            }
            finally
            {
                operation = Operation.None;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            StopCurrentJob();
        }

        private void AttachJob(EmbodiedLabJob nextJob)
        {
            StopCurrentJob();
            job = nextJob;
            job.ResultUpdated += HandleResultUpdated;
        }

        private void HandleResultUpdated(ResultDocument result)
        {
            ResultUpdated?.Invoke(result);
        }

        private (CancellationToken Token, int Generation) StartMonitor(
            CancellationToken lifetimeToken)
        {
            monitorCancellation?.Cancel();
            monitorCancellation?.Dispose();
            monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeToken);
            IsMonitoring = true;
            int generation = ++monitorGeneration;
            return (monitorCancellation.Token, generation);
        }

        private void FinishMonitor(int generation)
        {
            if (generation >= 0 && generation == monitorGeneration)
            {
                IsMonitoring = false;
            }
        }

        private void StopCurrentJob()
        {
            monitorGeneration++;
            IsMonitoring = false;
            monitorCancellation?.Cancel();
            monitorCancellation?.Dispose();
            monitorCancellation = null;
            if (job == null)
            {
                return;
            }

            job.ResultUpdated -= HandleResultUpdated;
            job.Dispose();
            job = null;
        }

        private void ReportActivity(string message)
        {
            ActivityChanged?.Invoke(message);
        }
    }
}
