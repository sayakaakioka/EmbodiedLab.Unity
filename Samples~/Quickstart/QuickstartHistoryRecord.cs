#nullable enable

using EmbodiedLab.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace EmbodiedLab.Unity.Samples.Quickstart
{
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class QuickstartHistoryRecord
    {
        [JsonProperty("submission_id", Required = Required.Always)]
        internal string SubmissionId { get; set; } = string.Empty;

        [JsonProperty("submitted_at_utc", Required = Required.Always)]
        internal string SubmittedAtUtc { get; set; } = string.Empty;

        [JsonProperty("api_base_url", Required = Required.Always)]
        internal string ApiBaseUrl { get; set; } = string.Empty;

        [JsonProperty("result_websocket_base_url", Required = Required.Always)]
        internal string ResultWebSocketBaseUrl { get; set; } = string.Empty;

        [JsonProperty("scenario_json", Required = Required.Always)]
        internal string ScenarioJson { get; set; } = string.Empty;

        [JsonProperty("status", Required = Required.Always)]
        [JsonConverter(typeof(StringEnumConverter))]
        internal ResultStatus Status { get; set; } = ResultStatus.Queued;

        [JsonProperty("progress", NullValueHandling = NullValueHandling.Ignore)]
        internal Progress? Progress { get; set; }

        // This capability is local-only and must never be logged or displayed.
        [JsonProperty("cancel_token", NullValueHandling = NullValueHandling.Ignore)]
        internal string? CancelToken { get; set; }

        [JsonProperty(
            "local_replay_manifest_path",
            NullValueHandling = NullValueHandling.Ignore)]
        internal string? LocalReplayManifestPath { get; set; }

        [JsonProperty(
            "local_replay_chunk_path",
            NullValueHandling = NullValueHandling.Ignore)]
        internal string? LocalReplayChunkPath { get; set; }

        [JsonProperty("local_onnx_path", NullValueHandling = NullValueHandling.Ignore)]
        internal string? LocalOnnxPath { get; set; }

        internal bool IsTerminal => IsTerminalStatus(Status);

        internal bool ApplyResult(ResultDocument result)
        {
            bool changed = Status != result.Status || !ProgressEquals(Progress, result.Progress);
            Status = result.Status;
            Progress = result.Progress;
            if (IsTerminal)
            {
                // A terminal job no longer needs its cloud cancellation capability.
                changed |= CancelToken != null;
                CancelToken = null;
            }

            return changed;
        }

        private static bool ProgressEquals(Progress? left, Progress? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            return left != null &&
                right != null &&
                left.Phase == right.Phase &&
                left.CurrentStep == right.CurrentStep &&
                left.TotalSteps == right.TotalSteps &&
                left.Message == right.Message;
        }

        private static bool IsTerminalStatus(ResultStatus status)
        {
            return status == ResultStatus.Completed ||
                status == ResultStatus.Failed ||
                status == ResultStatus.Cancelled;
        }
    }
}
