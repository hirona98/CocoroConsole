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

        [JsonPropertyName("activity_trace")]
        public JsonElement ActivityTrace { get; set; }

        [JsonPropertyName("result_trace")]
        public JsonElement ResultTrace { get; set; }

        [JsonPropertyName("memory_trace")]
        public JsonElement MemoryTrace { get; set; }
    }

    public class OtomeKairoCycleCognitiveContext
    {
        [JsonPropertyName("cycle_id")]
        public string CycleId { get; set; } = string.Empty;

        [JsonPropertyName("cycle_summary")]
        public JsonElement CycleSummary { get; set; }

        [JsonPropertyName("foreground_selection")]
        public JsonElement ForegroundSelection { get; set; }

        [JsonPropertyName("workspace_context_summary")]
        public JsonElement WorkspaceContextSummary { get; set; }

        [JsonPropertyName("self_state_context")]
        public JsonElement SelfStateContext { get; set; }

        [JsonPropertyName("relationship_context")]
        public JsonElement RelationshipContext { get; set; }

        [JsonPropertyName("prediction_error_context")]
        public JsonElement PredictionErrorContext { get; set; }

        [JsonPropertyName("default_mode_context")]
        public JsonElement DefaultModeContext { get; set; }
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

    public class OtomeKairoAutonomousRunsResponse
    {
        [JsonPropertyName("generated_at")]
        public string GeneratedAt { get; set; } = string.Empty;

        [JsonPropertyName("autonomous_runs")]
        public List<OtomeKairoAutonomousRunSummary> AutonomousRuns { get; set; } = new List<OtomeKairoAutonomousRunSummary>();
    }

    public class OtomeKairoAutonomousRunSummary
    {
        [JsonPropertyName("run_id")]
        public string RunId { get; set; } = string.Empty;

        [JsonPropertyName("memory_set_id")]
        public string MemorySetId { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("objective_summary")]
        public string? ObjectiveSummary { get; set; }

        [JsonPropertyName("origin_kind")]
        public string? OriginKind { get; set; }

        [JsonPropertyName("current_step_summary")]
        public string? CurrentStepSummary { get; set; }

        [JsonPropertyName("history_summary")]
        public string? HistorySummary { get; set; }

        [JsonPropertyName("next_run_at")]
        public string? NextRunAt { get; set; }

        [JsonPropertyName("waiting_request_id")]
        public string? WaitingRequestId { get; set; }

        [JsonPropertyName("pause_reason")]
        public string? PauseReason { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public string? UpdatedAt { get; set; }

        [JsonPropertyName("completed_at")]
        public string? CompletedAt { get; set; }
    }
}
