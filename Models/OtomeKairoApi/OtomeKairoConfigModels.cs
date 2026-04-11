using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.OtomeKairoApi
{
    /// <summary>
    /// OtomeKairo の設定 bundle を表します。
    /// </summary>
    public class OtomeKairoEditorState
    {
        // Current
        [JsonPropertyName("current")]
        public OtomeKairoCurrentSettings Current { get; set; } = new OtomeKairoCurrentSettings();

        // Resources
        [JsonPropertyName("personas")]
        public List<OtomeKairoPersonaDefinition> Personas { get; set; } = new List<OtomeKairoPersonaDefinition>();

        [JsonPropertyName("memory_sets")]
        public List<OtomeKairoMemorySetDefinition> MemorySets { get; set; } = new List<OtomeKairoMemorySetDefinition>();

        [JsonPropertyName("model_presets")]
        public List<OtomeKairoModelPresetDefinition> ModelPresets { get; set; } = new List<OtomeKairoModelPresetDefinition>();
    }

    /// <summary>
    /// OtomeKairo の現在設定を表します。
    /// </summary>
    public class OtomeKairoCurrentSettings
    {
        // Identity
        [JsonPropertyName("selected_persona_id")]
        public string SelectedPersonaId { get; set; } = string.Empty;

        [JsonPropertyName("selected_memory_set_id")]
        public string SelectedMemorySetId { get; set; } = string.Empty;

        [JsonPropertyName("selected_model_preset_id")]
        public string SelectedModelPresetId { get; set; } = string.Empty;

        [JsonPropertyName("desktop_watch")]
        public OtomeKairoDesktopWatchSettings DesktopWatch { get; set; } = new OtomeKairoDesktopWatchSettings();

        [JsonPropertyName("wake_policy")]
        public Dictionary<string, object?> WakePolicy { get; set; } = new Dictionary<string, object?>();
    }

    /// <summary>
    /// デスクトップウォッチ設定を表します。
    /// </summary>
    public class OtomeKairoDesktopWatchSettings
    {
        // Fields
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("interval_seconds")]
        public int IntervalSeconds { get; set; }

        [JsonPropertyName("target_client_id")]
        public string? TargetClientId { get; set; }
    }

    /// <summary>
    /// 人格設定を表します。
    /// </summary>
    public class OtomeKairoPersonaDefinition
    {
        // Identity
        [JsonPropertyName("persona_id")]
        public string PersonaId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("persona_prompt")]
        public string PersonaPrompt { get; set; } = string.Empty;

        [JsonPropertyName("expression_addon")]
        public string ExpressionAddon { get; set; } = string.Empty;
    }

    /// <summary>
    /// 記憶セット設定を表します。
    /// </summary>
    public class OtomeKairoMemorySetDefinition
    {
        // Fields
        [JsonPropertyName("memory_set_id")]
        public string MemorySetId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("embedding")]
        public Dictionary<string, object?> Embedding { get; set; } = new Dictionary<string, object?>();
    }

    /// <summary>
    /// モデルプリセット設定を表します。
    /// </summary>
    public class OtomeKairoModelPresetDefinition
    {
        // Fields
        [JsonPropertyName("model_preset_id")]
        public string ModelPresetId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("prompt_window")]
        public OtomeKairoPromptWindowDefinition PromptWindow { get; set; } = new OtomeKairoPromptWindowDefinition();

        [JsonPropertyName("roles")]
        public Dictionary<string, Dictionary<string, object?>> Roles { get; set; } = new Dictionary<string, Dictionary<string, object?>>();
    }

    public class OtomeKairoPromptWindowDefinition
    {
        [JsonPropertyName("recent_turn_limit")]
        public int RecentTurnLimit { get; set; }

        [JsonPropertyName("recent_turn_minutes")]
        public int RecentTurnMinutes { get; set; }
    }
}
