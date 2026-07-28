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

    /// <summary>
    /// 音声人物と話者登録状態です。embedding は API 境界に出しません。
    /// </summary>
    public class OtomeKairoAudioSpeaker
    {
        [JsonPropertyName("person_ref")]
        public string PersonRef { get; set; } = string.Empty;

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

        [JsonPropertyName("display_name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DisplayName { get; set; }
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

        [JsonPropertyName("required_samples")]
        public int RequiredSamples { get; set; }

        [JsonPropertyName("completed_samples")]
        public int CompletedSamples { get; set; }

        [JsonPropertyName("expires_at")]
        public string ExpiresAt { get; set; } = string.Empty;
    }

    public class OtomeKairoRenameSpeakerRequest
    {
        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;
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
        public float Probability { get; set; }

        [JsonPropertyName("dbfs")]
        public float Dbfs { get; set; }
    }
}
