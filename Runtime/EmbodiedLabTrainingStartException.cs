#nullable enable

using System;

namespace EmbodiedLab.Unity
{
    /// <summary>
    /// Reports that a submission was created but its training start was not confirmed.
    /// </summary>
    public sealed class EmbodiedLabTrainingStartException : Exception
    {
        internal EmbodiedLabTrainingStartException(
            EmbodiedLabJob job,
            Exception innerException)
            : base(
                "EmbodiedLab created the submission, but training start was not confirmed. " +
                "Use Job to monitor or cancel the recoverable submission.",
                innerException)
        {
            Job = job ?? throw new ArgumentNullException(nameof(job));
        }

        /// <summary>
        /// Gets the recoverable job handle. The caller owns and must dispose this handle.
        /// </summary>
        public EmbodiedLabJob Job { get; }
    }
}
