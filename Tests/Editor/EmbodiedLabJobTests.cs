using System;
using System.Threading;
using EmbodiedLab.Contracts;
using NUnit.Framework;

namespace EmbodiedLab.Unity.Tests
{
    public sealed class EmbodiedLabJobTests
    {
        private static readonly EmbodiedLabEndpoints Endpoints = new(
            "https://api.example.test/root",
            "wss://stream.example.test/service");

        [Test]
        public void RestorePreservesCancellationCapability()
        {
            using EmbodiedLabJob job = EmbodiedLabJob.Restore(
                Endpoints,
                "submission-1",
                "navigation_default",
                "capability-1");

            Assert.That(job.SubmissionId, Is.EqualTo("submission-1"));
            Assert.That(job.ScenarioId, Is.EqualTo("navigation_default"));
            Assert.That(job.CancelToken, Is.EqualTo("capability-1"));
            Assert.That(job.CanCancel, Is.True);
            Assert.That(job.LatestResult, Is.Null);
            Assert.That(job.IsTerminal, Is.False);
        }

        [Test]
        public void RestoreWithoutCapabilityCreatesReadOnlyJob()
        {
            using EmbodiedLabJob job = EmbodiedLabJob.Restore(
                Endpoints,
                "submission-1",
                "navigation_default");

            Assert.That(job.CancelToken, Is.Null);
            Assert.That(job.CanCancel, Is.False);
        }

        [Test]
        public void SnapshotOwnsProgressAndWireCopies()
        {
            var wire = new ResultDocument
            {
                SubmissionId = "submission-1",
                Status = ResultStatus.Running,
                UpdatedAt = "2026-09-30T00:00:00Z",
                Progress = new Progress
                {
                    Phase = ResultStatus.Running,
                    CurrentStep = 1,
                    TotalSteps = 10,
                    Message = "original",
                },
            };
            var snapshot = new ResultSnapshot(wire);
            wire.Progress.Message = "producer changed";
            ResultDocument copy = snapshot.ToDocument();
            copy.Progress.Message = "consumer changed";
            copy.Status = ResultStatus.Failed;
            Assert.That(snapshot.Progress.Message, Is.EqualTo("original"));
            Assert.That(snapshot.ToDocument().Progress.Message, Is.EqualTo("original"));
            Assert.That(snapshot.Status, Is.EqualTo(ResultStatus.Running));
        }

        [Test]
        public void PreCancelledWaitAndLocalStopNeedNoNetwork()
        {
            using var job = EmbodiedLabJob.Restore(Endpoints, "submission-1", "navigation_default");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                job.WaitForCompletionAsync(cancellation.Token).GetAwaiter().GetResult();
                Assert.Fail("A pre-cancelled wait must not start monitoring.");
            }
            catch (OperationCanceledException)
            {
                Assert.That(job.LatestResult, Is.Null);
            }

            job.StopMonitoringAsync().GetAwaiter().GetResult();
            job.Dispose();
            Assert.Throws<ObjectDisposedException>(() => job.StopMonitoringAsync());
        }
    }
}
