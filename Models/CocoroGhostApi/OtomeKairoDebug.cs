using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class OtomeKairoSnapshotResponse
    {
        [JsonPropertyName("computed")]
        public OtomeKairoState? Computed { get; set; }

        [JsonPropertyName("override")]
        public OtomeKairoState? Override { get; set; }

        [JsonPropertyName("effective")]
        public OtomeKairoState? Effective { get; set; }
    }

    public class OtomeKairoState
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("intensity")]
        public double? Intensity { get; set; }

        [JsonPropertyName("components")]
        public Dictionary<string, double>? Components { get; set; }

        [JsonPropertyName("policy")]
        public OtomeKairoPolicy? Policy { get; set; }
    }

    public class OtomeKairoPolicy
    {
        [JsonPropertyName("cooperation")]
        public double? Cooperation { get; set; }

        [JsonPropertyName("refusal_bias")]
        public double? RefusalBias { get; set; }

        [JsonPropertyName("refusal_allowed")]
        public bool? RefusalAllowed { get; set; }
    }

    public class OtomeKairoOverrideRequest
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("intensity")]
        public double? Intensity { get; set; }

        [JsonPropertyName("components")]
        public Dictionary<string, double>? Components { get; set; }

        [JsonPropertyName("policy")]
        public OtomeKairoPolicy? Policy { get; set; }
    }
}
