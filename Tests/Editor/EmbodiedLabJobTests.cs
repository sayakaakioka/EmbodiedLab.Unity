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
                "capability-1");

            Assert.That(job.SubmissionId, Is.EqualTo("submission-1"));
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
                "submission-1");

            Assert.That(job.CancelToken, Is.Null);
            Assert.That(job.CanCancel, Is.False);
        }
    }
}
