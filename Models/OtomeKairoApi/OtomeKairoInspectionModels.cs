using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.OtomeKairoApi
{
    public class OtomeKairoCycleSummariesResponse
    {
        [JsonPropertyName("cycle_summaries")]
        public List<OtomeKairoCycleSummary> CycleSummaries { get; set; } = new List<OtomeKairoCycleSummary>();
    }

    public class OtomeKairoCycleSummary
    {
        [JsonPropertyName("cycle_id")]
        public string CycleId { get; set; } = string.Empty;

        [JsonPropertyName("server_id")]
        public string ServerId { get; set; } = string.Empty;

        [JsonPropertyName("trigger_kind")]
        public string TriggerKind { get; set; } = string.Empty;

        [JsonPropertyName("started_at")]
        public string StartedAt { get; set; } = string.Empty;

        [JsonPropertyName("finished_at")]
        public string FinishedAt { get; set; } = string.Empty;

        [JsonPropertyName("result_kind")]
        public string ResultKind { get; set; } = string.Empty;

        [JsonPropertyName("failed")]
        public bool Failed { get; set; }
    }

    public class OtomeKairoCycleTrace
    {
        [JsonPropertyName("cycle_id")]
        public string CycleId { get; set; } = string.Empty;

        [JsonPropertyName("cycle_summary")]
        public JsonElement CycleSummary { get; set; }

        [JsonPropertyName("input_trace")]
        public JsonElement InputTrace { get; set; }

        [JsonPropertyName("recall_trace")]
        public JsonElement RecallTrace { get; set; }

        [JsonPropertyName("decision_trace")]
        public JsonElement DecisionTrace { get; set; }

        [JsonPropertyName("world_state_trace")]
        public JsonElement WorldStateTrace { get; set; }

        [JsonPropertyName("result_trace")]
        public JsonElement ResultTrace { get; set; }

        [JsonPropertyName("memory_trace")]
        public JsonElement MemoryTrace { get; set; }
    }

    public class OtomeKairoCurrentStateSnapshot
    {
        [JsonPropertyName("generated_at")]
        public string GeneratedAt { get; set; } = string.Empty;

        [JsonPropertyName("settings_snapshot")]
        public JsonElement SettingsSnapshot { get; set; }

        [JsonPropertyName("runtime_summary")]
        public JsonElement RuntimeSummary { get; set; }

        [JsonPropertyName("runtime_detail")]
        public JsonElement RuntimeDetail { get; set; }

        [JsonPropertyName("current_state")]
        public JsonElement CurrentState { get; set; }

        [JsonPropertyName("capability_inspection")]
        public JsonElement CapabilityInspection { get; set; }
    }
}
