#nullable enable

using System;
using EmbodiedLab.Contracts;
using Newtonsoft.Json;

namespace EmbodiedLab.Unity
{
    /// <summary>An immutable, point-in-time view of a job result.</summary>
    /// <remarks>
    /// Safe to retain across subsequent updates and job disposal. No mutable wire DTO is shared
    /// with the job or other consumers. Use <see cref="ToDocument"/> when a wire-format copy is
    /// needed, including access to artifact metadata before using the existing download APIs.
    /// Snapshot creation uses the transport's bounded, validated Result Document.
    /// </remarks>
    public sealed class ResultSnapshot
    {
        private readonly string documentJson;

        internal ResultSnapshot(ResultDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            documentJson = JsonConvert.SerializeObject(document);
            SubmissionId = document.SubmissionId;
            Status = document.Status;
            UpdatedAt = document.UpdatedAt;
            Error = document.Error;
            Progress = new ResultProgressSnapshot(document.Progress);
        }

        /// <summary>Gets the submission identity.</summary>
        public string SubmissionId { get; }

        /// <summary>Gets the lifecycle state at capture time.</summary>
        public ResultStatus Status { get; }

        /// <summary>Gets the server-provided update timestamp.</summary>
        public string UpdatedAt { get; }

        /// <summary>Gets the failure message, or null when absent.</summary>
        public string? Error { get; }

        /// <summary>Gets immutable progress values at capture time.</summary>
        public ResultProgressSnapshot Progress { get; }

        /// <summary>Creates an independently owned mutable wire document, including its nested values.</summary>
        /// <remarks>
        /// Each call allocates a deep copy. Mutating its progress, artifacts, lists or dictionaries
        /// cannot change this snapshot, the job, future downloads, or another consumer's copy.
        /// This is an explicit wire-data export, not live job state.
        /// </remarks>
        public ResultDocument ToDocument() => JsonConvert.DeserializeObject<ResultDocument>(documentJson)!;
    }

    /// <summary>Immutable progress values belonging to one <see cref="ResultSnapshot"/>.</summary>
    public sealed class ResultProgressSnapshot
    {
        internal ResultProgressSnapshot(Progress progress)
        {
            CurrentStep = progress.CurrentStep;
            TotalSteps = progress.TotalSteps;
            Phase = progress.Phase;
            Message = progress.Message;
        }

        /// <summary>Gets the number of completed training steps.</summary>
        public int CurrentStep { get; }

        /// <summary>Gets the total training steps; zero may mean the trainer has not started.</summary>
        public int TotalSteps { get; }

        /// <summary>Gets the progress phase.</summary>
        public ResultStatus Phase { get; }

        /// <summary>Gets the server-provided progress message.</summary>
        public string Message { get; }
    }
}
