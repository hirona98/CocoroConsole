using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.OtomeKairoApi
{
    /// <summary>
    /// microphone connector が報告した入力デバイスです。
    /// </summary>
    public class OtomeKairoAudioInputDevice
    {
        [JsonPropertyName("host_api")]
        public string HostApi { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("max_input_channels")]
        public int MaxInputChannels { get; set; }

        [JsonPropertyName("default_sample_rate")]
        public double DefaultSampleRate { get; set; }

        [JsonPropertyName("ambiguous")]
        public bool Ambiguous { get; set; }

        public override string ToString()
        {
            return $"{HostApi}: {Name}";
        }
    }

    public class OtomeKairoAudioInputDevicesResponse
    {
        [JsonPropertyName("connector_client_id")]
        public string ConnectorClientId { get; set; } = string.Empty;

        [JsonPropertyName("connector_connected")]
        public bool ConnectorConnected { get; set; }

        [JsonPropertyName("devices")]
        public List<OtomeKairoAudioInputDevice> Devices { get; set; } = new List<OtomeKairoAudioInputDevice>();
    }

    public class OtomeKairoAudioOutputDevice
    {
        [JsonPropertyName("host_api")]
        public string HostApi { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("max_output_channels")]
        public int MaxOutputChannels { get; set; }

        [JsonPropertyName("default_sample_rate")]
        public double DefaultSampleRate { get; set; }

        [JsonPropertyName("ambiguous")]
        public bool Ambiguous { get; set; }

        public override string ToString() => $"{HostApi}: {Name}";
    }

    public class OtomeKairoAudioOutputDevicesResponse
    {
        [JsonPropertyName("connector_client_id")]
        public string ConnectorClientId { get; set; } = string.Empty;

        [JsonPropertyName("connector_connected")]
        public bool ConnectorConnected { get; set; }

        [JsonPropertyName("devices")]
        public List<OtomeKairoAudioOutputDevice> Devices { get; set; } = new List<OtomeKairoAudioOutputDevice>();
    }

    /// <summary>
    /// DB上の保存入力元とWeb入力セッションを解決した実効入力状態です。
    /// </summary>
    public class OtomeKairoAudioInputState
    {
        [JsonPropertyName("configured_source")]
        public string ConfiguredSource { get; set; } = string.Empty;

        [JsonPropertyName("effective_source")]
        public string EffectiveSource { get; set; } = string.Empty;

        [JsonPropertyName("stt_enabled")]
        public bool SttEnabled { get; set; }

        [JsonPropertyName("tts_enabled")]
        public bool TtsEnabled { get; set; }

        [JsonPropertyName("selected_avatar_id")]
        public string SelectedAvatarId { get; set; } = string.Empty;

        [JsonPropertyName("local_input_device")]
        public OtomeKairoSelectedAudioInputDevice? LocalInputDevice { get; set; }

        [JsonPropertyName("console")]
        public OtomeKairoConsoleMicrophoneSettings? Console { get; set; }
    }

    /// <summary>
    /// 選択中アバターの STT 運用トグルです。
    /// </summary>
    public class OtomeKairoSttEnabledState
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("selected_avatar_id")]
        public string SelectedAvatarId { get; set; } = string.Empty;
    }

    public class OtomeKairoSttEnabledRequest
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// 選択中アバターの TTS 運用トグルです。
    /// </summary>
    public class OtomeKairoTtsEnabledState
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("selected_avatar_id")]
        public string SelectedAvatarId { get; set; } = string.Empty;
    }

    public class OtomeKairoTtsEnabledRequest
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// 音声人物と話者登録状態です。embedding は API 境界に出しません。
    /// </summary>
    public class OtomeKairoAudioSpeaker
    {
        [JsonPropertyName("person_ref")]
        public string PersonRef { get; set; } = string.Empty;

        [JsonPropertyName("conversation_display_name_id")]
        public string ConversationDisplayNameId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("registration_status")]
        public string RegistrationStatus { get; set; } = string.Empty;

        [JsonPropertyName("model_id")]
        public string? ModelId { get; set; }

        [JsonPropertyName("registered_at")]
        public string? RegisteredAt { get; set; }

        [JsonPropertyName("updated_at")]
        public string? UpdatedAt { get; set; }

        [JsonIgnore]
        public List<OtomeKairoConversationDisplayNameDefinition> AvailableConversationDisplayNames { get; set; }
            = new List<OtomeKairoConversationDisplayNameDefinition>();
    }

    public class OtomeKairoAudioSpeakersResponse
    {
        [JsonPropertyName("speakers")]
        public List<OtomeKairoAudioSpeaker> Speakers { get; set; } = new List<OtomeKairoAudioSpeaker>();
    }

    public class OtomeKairoSpeakerEnrollmentRequest
    {
        [JsonPropertyName("owner_client_id")]
        public string OwnerClientId { get; set; } = string.Empty;

        [JsonPropertyName("person_ref")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PersonRef { get; set; }

        [JsonPropertyName("conversation_display_name_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ConversationDisplayNameId { get; set; }
    }

    public class OtomeKairoSpeakerEnrollment
    {
        [JsonPropertyName("enrollment_id")]
        public string EnrollmentId { get; set; } = string.Empty;

        [JsonPropertyName("owner_client_id")]
        public string OwnerClientId { get; set; } = string.Empty;

        [JsonPropertyName("person_ref")]
        public string? PersonRef { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("conversation_display_name_id")]
        public string? ConversationDisplayNameId { get; set; }

        [JsonPropertyName("required_samples")]
        public int RequiredSamples { get; set; }

        [JsonPropertyName("completed_samples")]
        public int CompletedSamples { get; set; }

        [JsonPropertyName("expires_at")]
        public string ExpiresAt { get; set; } = string.Empty;
    }

    public class OtomeKairoAssignSpeakerConversationDisplayNameRequest
    {
        [JsonPropertyName("conversation_display_name_id")]
        public string ConversationDisplayNameId { get; set; } = string.Empty;
    }

    /// <summary>
    /// event stream で通知される音声 runtime の完全 snapshot です。
    /// </summary>
    public class OtomeKairoAudioRuntimeState
    {
        [JsonPropertyName("available")]
        public bool Available { get; set; }

        [JsonPropertyName("unavailable_reason")]
        public string? UnavailableReason { get; set; }

        [JsonPropertyName("configured_source")]
        public string? ConfiguredSource { get; set; }

        [JsonPropertyName("effective_source")]
        public string? EffectiveSource { get; set; }

        [JsonPropertyName("stt_enabled")]
        public bool SttEnabled { get; set; }

        [JsonPropertyName("tts_enabled")]
        public bool TtsEnabled { get; set; }

        [JsonPropertyName("selected_avatar_id")]
        public string? SelectedAvatarId { get; set; }

        [JsonPropertyName("audio_output_destination")]
        public string AudioOutputDestination { get; set; } = string.Empty;

        [JsonPropertyName("local_output_device")]
        public OtomeKairoSelectedAudioInputDevice? LocalOutputDevice { get; set; }

        [JsonPropertyName("audio_output_client_count")]
        public int AudioOutputClientCount { get; set; }

        [JsonPropertyName("active_source")]
        public string? ActiveSource { get; set; }

        [JsonPropertyName("lease_generation")]
        public int? LeaseGeneration { get; set; }

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = string.Empty;

        [JsonPropertyName("paused_reason")]
        public string? PausedReason { get; set; }

        [JsonPropertyName("vad")]
        public OtomeKairoAudioVadState Vad { get; set; } = new OtomeKairoAudioVadState();

        [JsonPropertyName("enrollment")]
        public OtomeKairoSpeakerEnrollment? Enrollment { get; set; }
    }

    public class OtomeKairoAudioVadState
    {
        [JsonPropertyName("speaking")]
        public bool Speaking { get; set; }

        [JsonPropertyName("probability")]
        public float? Probability { get; set; }

        [JsonPropertyName("dbfs")]
        public float? Dbfs { get; set; }
    }
}
