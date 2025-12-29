using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class PartnerMoodState
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("intensity")]
        public double? Intensity { get; set; }

        [JsonPropertyName("components")]
        public Dictionary<string, double>? Components { get; set; }

        [JsonPropertyName("response_policy")]
        public PartnerMoodResponsePolicy? ResponsePolicy { get; set; }
    }

    public class PartnerMoodResponsePolicy
    {
        [JsonPropertyName("cooperation")]
        public double? Cooperation { get; set; }

        [JsonPropertyName("refusal_bias")]
        public double? RefusalBias { get; set; }

        [JsonPropertyName("refusal_allowed")]
        public bool? RefusalAllowed { get; set; }
    }

    public class PartnerMoodOverrideRequest
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("intensity")]
        public double? Intensity { get; set; }

        [JsonPropertyName("components")]
        public Dictionary<string, double>? Components { get; set; }

        [JsonPropertyName("response_policy")]
        public PartnerMoodResponsePolicy? ResponsePolicy { get; set; }
    }
}
