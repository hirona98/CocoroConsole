using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.OtomeKairoApi
{
    /// <summary>
    /// OtomeKairo の設定 bundle を表します。
    /// </summary>
    public class OtomeKairoEditorState
    {
        // Block: Current
        [JsonPropertyName("current")]
        public OtomeKairoCurrentSettings Current { get; set; } = new OtomeKairoCurrentSettings();

        // Block: Resources
        [JsonPropertyName("personas")]
        public List<OtomeKairoPersonaDefinition> Personas { get; set; } = new List<OtomeKairoPersonaDefinition>();

        [JsonPropertyName("memory_sets")]
        public List<OtomeKairoMemorySetDefinition> MemorySets { get; set; } = new List<OtomeKairoMemorySetDefinition>();

        [JsonPropertyName("model_presets")]
        public List<OtomeKairoModelPresetDefinition> ModelPresets { get; set; } = new List<OtomeKairoModelPresetDefinition>();

        [JsonPropertyName("model_profiles")]
        public List<OtomeKairoModelProfileDefinition> ModelProfiles { get; set; } = new List<OtomeKairoModelProfileDefinition>();
    }

    /// <summary>
    /// OtomeKairo の現在設定を表します。
    /// </summary>
    public class OtomeKairoCurrentSettings
    {
        // Block: Identity
        [JsonPropertyName("selected_persona_id")]
        public string SelectedPersonaId { get; set; } = string.Empty;

        [JsonPropertyName("selected_memory_set_id")]
        public string SelectedMemorySetId { get; set; } = string.Empty;

        [JsonPropertyName("selected_model_preset_id")]
        public string SelectedModelPresetId { get; set; } = string.Empty;

        // Block: RuntimeToggles
        [JsonPropertyName("memory_enabled")]
        public bool MemoryEnabled { get; set; }

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
        // Block: Fields
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
        // Block: Identity
        [JsonPropertyName("persona_id")]
        public string PersonaId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        // Block: EditableText
        [JsonPropertyName("persona_text")]
        public string PersonaText { get; set; } = string.Empty;

        [JsonPropertyName("second_person_label")]
        public string SecondPersonLabel { get; set; } = string.Empty;

        [JsonPropertyName("addon_text")]
        public string AddonText { get; set; } = string.Empty;

        // Block: PersonaCore
        [JsonPropertyName("core_persona")]
        public Dictionary<string, object?> CorePersona { get; set; } = new Dictionary<string, object?>();

        [JsonPropertyName("expression_style")]
        public Dictionary<string, object?> ExpressionStyle { get; set; } = new Dictionary<string, object?>();
    }

    /// <summary>
    /// 記憶セット設定を表します。
    /// </summary>
    public class OtomeKairoMemorySetDefinition
    {
        // Block: Fields
        [JsonPropertyName("memory_set_id")]
        public string MemorySetId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    /// <summary>
    /// モデルプリセット設定を表します。
    /// </summary>
    public class OtomeKairoModelPresetDefinition
    {
        // Block: Fields
        [JsonPropertyName("model_preset_id")]
        public string ModelPresetId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("roles")]
        public Dictionary<string, Dictionary<string, object?>> Roles { get; set; } = new Dictionary<string, Dictionary<string, object?>>();
    }

    /// <summary>
    /// モデルプロファイル設定を表します。
    /// </summary>
    public class OtomeKairoModelProfileDefinition
    {
        // Block: Identity
        [JsonPropertyName("model_profile_id")]
        public string ModelProfileId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        // Block: BaseConnection
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("base_url")]
        public string? BaseUrl { get; set; }

        [JsonPropertyName("auth")]
        public Dictionary<string, object?>? Auth { get; set; }

        // Block: VisionSettings
        [JsonPropertyName("vision_model_name")]
        public string? VisionModelName { get; set; }

        [JsonPropertyName("vision_base_url")]
        public string? VisionBaseUrl { get; set; }

        [JsonPropertyName("vision_auth")]
        public Dictionary<string, object?>? VisionAuth { get; set; }

        [JsonPropertyName("vision_max_tokens")]
        public int? VisionMaxTokens { get; set; }

        [JsonPropertyName("vision_timeout_seconds")]
        public int? VisionTimeoutSeconds { get; set; }
    }
}
