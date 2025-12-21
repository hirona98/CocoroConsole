using System.Text.Json.Serialization;

namespace CocoroConsole.Models.CocoroGhostApi
{
    public class AddonPreset
    {
        [JsonPropertyName("addon_preset_id")]
        public string? AddonPresetId { get; set; }

        [JsonPropertyName("addon_preset_name")]
        public string AddonPresetName { get; set; } = string.Empty;

        [JsonPropertyName("addon_text")]
        public string AddonText { get; set; } = string.Empty;
    }
}
