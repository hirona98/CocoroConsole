using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.OtomeKairoApi
{
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

        [JsonPropertyName("thinking_speech_level")]
        public int ThinkingSpeechLevel { get; set; } = 5;

        [JsonPropertyName("selected_conversation_display_name_id")]
        public string? SelectedConversationDisplayNameId { get; set; }

        [JsonPropertyName("wake_policy")]
        public Dictionary<string, object?> WakePolicy { get; set; } = new Dictionary<string, object?>();
    }

    /// <summary>
    /// 会話入力と音声話者が共有する呼ばれ方を表します。
    /// </summary>
    public class OtomeKairoConversationDisplayNameDefinition
    {
        [JsonPropertyName("conversation_display_name_id")]
        public string ConversationDisplayNameId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public string? UpdatedAt { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public class OtomeKairoConversationDisplayNamesResponse
    {
        [JsonPropertyName("conversation_display_names")]
        public List<OtomeKairoConversationDisplayNameDefinition> ConversationDisplayNames { get; set; }
            = new List<OtomeKairoConversationDisplayNameDefinition>();
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

        [JsonPropertyName("initiative_baseline")]
        public string InitiativeBaseline { get; set; } = "medium";

        [JsonPropertyName("persona_prompt")]
        public string PersonaPrompt { get; set; } = string.Empty;

        [JsonPropertyName("expression_addon")]
        public string ExpressionAddon { get; set; } = string.Empty;
    }

    /// <summary>
    /// 記憶集合設定を表します。
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

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("api_base")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ApiBase { get; set; }

        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonPropertyName("reasoning_effort")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ReasoningEffort { get; set; }

        [JsonPropertyName("max_output_tokens")]
        public int MaxOutputTokens { get; set; } = 4000;

        [JsonPropertyName("timeout_seconds")]
        public int TimeoutSeconds { get; set; } = 90;

        [JsonPropertyName("web_search_enabled")]
        public bool WebSearchEnabled { get; set; }
    }

    public class OtomeKairoPromptWindowDefinition
    {
        [JsonPropertyName("recent_turn_limit")]
        public int RecentTurnLimit { get; set; }

        [JsonPropertyName("recent_turn_minutes")]
        public int RecentTurnMinutes { get; set; }
    }

    /// <summary>
    /// アバター音声設定の編集用bundleを表します。
    /// </summary>
    public class OtomeKairoAvatarSpeechEditorState
    {
        [JsonPropertyName("selected_avatar_id")]
        public string SelectedAvatarId { get; set; } = string.Empty;

        [JsonPropertyName("microphone_settings")]
        public OtomeKairoMicrophoneSettings MicrophoneSettings { get; set; } = new OtomeKairoMicrophoneSettings();

        [JsonPropertyName("audio_output_settings")]
        public OtomeKairoAudioOutputSettings AudioOutputSettings { get; set; } = new OtomeKairoAudioOutputSettings();

        [JsonPropertyName("avatars")]
        public List<OtomeKairoAvatarSpeechDefinition> Avatars { get; set; } = new List<OtomeKairoAvatarSpeechDefinition>();
    }

    public class OtomeKairoAudioOutputSettings
    {
        [JsonPropertyName("destination")]
        public string Destination { get; set; } = "otomekairo";

        [JsonPropertyName("local_output_device")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public OtomeKairoSelectedAudioInputDevice? LocalOutputDevice { get; set; }
    }

    public class OtomeKairoMicrophoneSettings
    {
        [JsonPropertyName("input_source")]
        public string InputSource { get; set; } = "local_microphone";

        /// <summary>
        /// 未選択時もAPIの必須フィールドとしてnullを送信します。
        /// </summary>
        [JsonPropertyName("local_input_device")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public OtomeKairoSelectedAudioInputDevice? LocalInputDevice { get; set; }

        [JsonPropertyName("console")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public OtomeKairoConsoleMicrophoneSettings? Console { get; set; }

        [JsonPropertyName("vad_probability_threshold")]
        public float VadProbabilityThreshold { get; set; }

        [JsonPropertyName("speaker_recognition_threshold")]
        public float SpeakerRecognitionThreshold { get; set; }
    }

    public class OtomeKairoSelectedAudioInputDevice
    {
        [JsonPropertyName("host_api")]
        public string HostApi { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class OtomeKairoConsoleMicrophoneSettings
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("input_device")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public OtomeKairoConsoleMicrophoneInputDevice? InputDevice { get; set; }
    }

    public class OtomeKairoConsoleMicrophoneInputDevice
    {
        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class OtomeKairoAvatarSpeechDefinition
    {
        [JsonPropertyName("avatar_id")]
        public string AvatarId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("stt")]
        public OtomeKairoSttSettings Stt { get; set; } = new OtomeKairoSttSettings();

        [JsonPropertyName("tts")]
        public OtomeKairoTtsSettings Tts { get; set; } = new OtomeKairoTtsSettings();
    }

    public class OtomeKairoSttSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("engine")]
        public string Engine { get; set; } = string.Empty;

        [JsonPropertyName("profile_id")]
        public string ProfileId { get; set; } = string.Empty;

        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = string.Empty;

    }

    public class OtomeKairoTtsSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("engine")]
        public string Engine { get; set; } = string.Empty;

        [JsonPropertyName("voicevox_config")]
        public OtomeKairoVoicevoxSettings VoicevoxConfig { get; set; } = new OtomeKairoVoicevoxSettings();

        [JsonPropertyName("style_bert_vits2_config")]
        public OtomeKairoStyleBertVits2Settings StyleBertVits2Config { get; set; } = new OtomeKairoStyleBertVits2Settings();

        [JsonPropertyName("aivis_cloud_config")]
        public OtomeKairoAivisCloudSettings AivisCloudConfig { get; set; } = new OtomeKairoAivisCloudSettings();
    }

    public class OtomeKairoVoicevoxSettings
    {
        [JsonPropertyName("endpoint_url")]
        public string EndpointUrl { get; set; } = string.Empty;

        [JsonPropertyName("secondary_endpoint_url")]
        public string SecondaryEndpointUrl { get; set; } = string.Empty;

        [JsonPropertyName("speaker_id")]
        public int SpeakerId { get; set; }

        [JsonPropertyName("speed_scale")]
        public float SpeedScale { get; set; }

        [JsonPropertyName("pitch_scale")]
        public float PitchScale { get; set; }

        [JsonPropertyName("intonation_scale")]
        public float IntonationScale { get; set; }

        [JsonPropertyName("volume_scale")]
        public float VolumeScale { get; set; }

        [JsonPropertyName("pre_phoneme_length")]
        public float PrePhonemeLength { get; set; }

        [JsonPropertyName("post_phoneme_length")]
        public float PostPhonemeLength { get; set; }

        [JsonPropertyName("output_sampling_rate")]
        public int OutputSamplingRate { get; set; }

        [JsonPropertyName("output_stereo")]
        public bool OutputStereo { get; set; }
    }

    public class OtomeKairoStyleBertVits2Settings
    {
        [JsonPropertyName("endpoint_url")]
        public string EndpointUrl { get; set; } = string.Empty;

        [JsonPropertyName("model_name")]
        public string ModelName { get; set; } = string.Empty;

        [JsonPropertyName("model_id")]
        public int ModelId { get; set; }

        [JsonPropertyName("speaker_name")]
        public string SpeakerName { get; set; } = string.Empty;

        [JsonPropertyName("speaker_id")]
        public int SpeakerId { get; set; }

        [JsonPropertyName("style")]
        public string Style { get; set; } = string.Empty;

        [JsonPropertyName("style_weight")]
        public float StyleWeight { get; set; }

        [JsonPropertyName("sdp_ratio")]
        public float SdpRatio { get; set; }

        [JsonPropertyName("noise")]
        public float Noise { get; set; }

        [JsonPropertyName("noise_w")]
        public float NoiseW { get; set; }

        [JsonPropertyName("length")]
        public float Length { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("auto_split")]
        public bool AutoSplit { get; set; }

        [JsonPropertyName("split_interval")]
        public float SplitInterval { get; set; }

        [JsonPropertyName("assist_text")]
        public string AssistText { get; set; } = string.Empty;

        [JsonPropertyName("assist_text_weight")]
        public float AssistTextWeight { get; set; }

        [JsonPropertyName("reference_audio_path")]
        public string ReferenceAudioPath { get; set; } = string.Empty;
    }

    public class OtomeKairoAivisCloudSettings
    {
        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonPropertyName("endpoint_url")]
        public string EndpointUrl { get; set; } = string.Empty;

        [JsonPropertyName("model_uuid")]
        public string ModelUuid { get; set; } = string.Empty;

        [JsonPropertyName("speaker_uuid")]
        public string SpeakerUuid { get; set; } = string.Empty;

        [JsonPropertyName("style_id")]
        public int StyleId { get; set; }

        [JsonPropertyName("style_name")]
        public string StyleName { get; set; } = string.Empty;

        [JsonPropertyName("use_ssml")]
        public bool UseSsml { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("speaking_rate")]
        public float SpeakingRate { get; set; }

        [JsonPropertyName("emotional_intensity")]
        public float EmotionalIntensity { get; set; }

        [JsonPropertyName("tempo_dynamics")]
        public float TempoDynamics { get; set; }

        [JsonPropertyName("pitch")]
        public float Pitch { get; set; }

        [JsonPropertyName("volume")]
        public float Volume { get; set; }

        [JsonPropertyName("output_format")]
        public string OutputFormat { get; set; } = string.Empty;

        [JsonPropertyName("output_bitrate")]
        public int OutputBitrate { get; set; }

        [JsonPropertyName("output_sampling_rate")]
        public int OutputSamplingRate { get; set; }

        [JsonPropertyName("output_audio_channels")]
        public string OutputAudioChannels { get; set; } = string.Empty;
    }

    /// <summary>
    /// CocoroConsole端末設定の編集用レスポンスを表します。
    /// </summary>
    public class OtomeKairoConsoleClientEditorState
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("last_connected_at")]
        public string? LastConnectedAt { get; set; }

        [JsonPropertyName("settings")]
        public OtomeKairoConsoleClientSettings Settings { get; set; } = new OtomeKairoConsoleClientSettings();
    }

    public class OtomeKairoConsoleClientSettings
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("process")]
        public OtomeKairoConsoleProcessSettings Process { get; set; } = new OtomeKairoConsoleProcessSettings();

        [JsonPropertyName("display")]
        public OtomeKairoConsoleDisplaySettings Display { get; set; } = new OtomeKairoConsoleDisplaySettings();

        [JsonPropertyName("desktop_capture")]
        public OtomeKairoConsoleDesktopCaptureSettings DesktopCapture { get; set; } = new OtomeKairoConsoleDesktopCaptureSettings();

        [JsonPropertyName("avatar_presentations")]
        public List<OtomeKairoConsoleAvatarPresentation> AvatarPresentations { get; set; } = new List<OtomeKairoConsoleAvatarPresentation>();

        [JsonPropertyName("motion")]
        public OtomeKairoConsoleMotionSettings Motion { get; set; } = new OtomeKairoConsoleMotionSettings();
    }

    public class OtomeKairoConsoleProcessSettings
    {
        [JsonPropertyName("console_api_port")]
        public int ConsoleApiPort { get; set; }

        [JsonPropertyName("cocoro_shell_port")]
        public int CocoroShellPort { get; set; }
    }

    public class OtomeKairoConsoleDisplaySettings
    {
        [JsonPropertyName("restore_window_position")]
        public bool RestoreWindowPosition { get; set; }

        [JsonPropertyName("topmost")]
        public bool Topmost { get; set; }

        [JsonPropertyName("escape_cursor")]
        public bool EscapeCursor { get; set; }

        [JsonPropertyName("escape_positions")]
        public List<OtomeKairoConsoleEscapePosition> EscapePositions { get; set; } = new List<OtomeKairoConsoleEscapePosition>();

        [JsonPropertyName("touch_virtual_key_enabled")]
        public bool TouchVirtualKeyEnabled { get; set; }

        [JsonPropertyName("virtual_key")]
        public string VirtualKey { get; set; } = string.Empty;

        [JsonPropertyName("auto_move")]
        public bool AutoMove { get; set; }

        [JsonPropertyName("show_message_window")]
        public bool ShowMessageWindow { get; set; }

        [JsonPropertyName("ambient_occlusion_enabled")]
        public bool AmbientOcclusionEnabled { get; set; }

        [JsonPropertyName("msaa_level")]
        public int MsaaLevel { get; set; }

        [JsonPropertyName("avatar_shadow_mode")]
        public int AvatarShadowMode { get; set; }

        [JsonPropertyName("avatar_shadow_resolution")]
        public int AvatarShadowResolution { get; set; }

        [JsonPropertyName("background_shadow_mode")]
        public int BackgroundShadowMode { get; set; }

        [JsonPropertyName("background_shadow_resolution")]
        public int BackgroundShadowResolution { get; set; }

        [JsonPropertyName("avatar_window_size")]
        public int AvatarWindowSize { get; set; }

        [JsonPropertyName("avatar_position_x")]
        public float AvatarPositionX { get; set; }

        [JsonPropertyName("avatar_position_y")]
        public float AvatarPositionY { get; set; }

        [JsonPropertyName("message_window")]
        public OtomeKairoConsoleMessageWindowSettings MessageWindow { get; set; } = new OtomeKairoConsoleMessageWindowSettings();

        [JsonPropertyName("window_placements")]
        public Dictionary<string, OtomeKairoConsoleWindowPlacement> WindowPlacements { get; set; } = new Dictionary<string, OtomeKairoConsoleWindowPlacement>();
    }

    public class OtomeKairoConsoleEscapePosition
    {
        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }
    }

    public class OtomeKairoConsoleMessageWindowSettings
    {
        [JsonPropertyName("max_message_count")]
        public int MaxMessageCount { get; set; }

        [JsonPropertyName("max_total_characters")]
        public int MaxTotalCharacters { get; set; }

        [JsonPropertyName("min_window_size")]
        public float MinWindowSize { get; set; }

        [JsonPropertyName("max_window_size")]
        public float MaxWindowSize { get; set; }

        [JsonPropertyName("font_size")]
        public float FontSize { get; set; }

        [JsonPropertyName("horizontal_offset")]
        public float HorizontalOffset { get; set; }

        [JsonPropertyName("vertical_offset")]
        public float VerticalOffset { get; set; }
    }

    public class OtomeKairoConsoleWindowPlacement
    {
        [JsonPropertyName("left")]
        public double Left { get; set; }

        [JsonPropertyName("top")]
        public double Top { get; set; }
    }

    public class OtomeKairoConsoleDesktopCaptureSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("capture_active_window_only")]
        public bool CaptureActiveWindowOnly { get; set; }

        [JsonPropertyName("idle_timeout_minutes")]
        public int IdleTimeoutMinutes { get; set; }

        [JsonPropertyName("exclude_patterns")]
        public List<string> ExcludePatterns { get; set; } = new List<string>();
    }

    public class OtomeKairoConsoleAvatarPresentation
    {
        [JsonPropertyName("avatar_id")]
        public string AvatarId { get; set; } = string.Empty;

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("convert_unlit_to_mtoon")]
        public bool ConvertUnlitToMtoon { get; set; }

        [JsonPropertyName("shadow_exclusion_enabled")]
        public bool ShadowExclusionEnabled { get; set; }

        [JsonPropertyName("shadow_excluded_mesh_names")]
        public List<string> ShadowExcludedMeshNames { get; set; } = new List<string>();
    }

    public class OtomeKairoConsoleMotionSettings
    {
        [JsonPropertyName("selected_animation_set_id")]
        public string SelectedAnimationSetId { get; set; } = string.Empty;

        [JsonPropertyName("animation_sets")]
        public List<OtomeKairoConsoleAnimationSet> AnimationSets { get; set; } = new List<OtomeKairoConsoleAnimationSet>();
    }

    public class OtomeKairoConsoleAnimationSet
    {
        [JsonPropertyName("animation_set_id")]
        public string AnimationSetId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("posture_change_loop_count_standing")]
        public int PostureChangeLoopCountStanding { get; set; }

        [JsonPropertyName("posture_change_loop_count_sitting_floor")]
        public int PostureChangeLoopCountSittingFloor { get; set; }

        [JsonPropertyName("animations")]
        public List<OtomeKairoConsoleAnimation> Animations { get; set; } = new List<OtomeKairoConsoleAnimation>();
    }

    public class OtomeKairoConsoleAnimation
    {
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("animation_type")]
        public int AnimationType { get; set; }

        [JsonPropertyName("animation_name")]
        public string AnimationName { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }
    }
}
