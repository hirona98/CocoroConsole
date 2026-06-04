using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CocoroConsole.Utilities
{
    internal static class DesktopWakePolicyHelper
    {
        public const string DesktopWakeObservationId = "observation:main_desktop";

        private const int DefaultWakeIntervalSeconds = 300;
        private const double DefaultVisualObservationSimilarityThreshold = 0.3;

        public static bool HasDesktopWakeObservation(Dictionary<string, object?> wakePolicy, string? clientId)
        {
            return ReadWakeObservations(wakePolicy.GetValueOrDefault("observations"))
                .Any(observation => IsDesktopWakeObservation(observation, clientId));
        }

        public static Dictionary<string, object?> BuildWakePolicyRequest(
            string mode,
            int? intervalSeconds,
            double visualObservationSimilarityThreshold,
            string? clientId,
            bool desktopObservationEnabled,
            IEnumerable<Dictionary<string, object?>>? currentObservations = null)
        {
            var normalizedMode = string.Equals(mode, "interval", StringComparison.OrdinalIgnoreCase)
                ? "interval"
                : "disabled";
            var request = new Dictionary<string, object?>
            {
                ["mode"] = normalizedMode,
                ["visual_observation_similarity_threshold"] = ClampSimilarityThreshold(visualObservationSimilarityThreshold),
            };

            if (normalizedMode == "interval")
            {
                var normalizedIntervalSeconds = intervalSeconds.GetValueOrDefault(DefaultWakeIntervalSeconds);
                request["interval_seconds"] = normalizedIntervalSeconds > 0
                    ? normalizedIntervalSeconds
                    : DefaultWakeIntervalSeconds;
            }

            var observations = BuildWakeObservationRequests(currentObservations, clientId, desktopObservationEnabled);
            if (observations.Count > 0)
            {
                request["observations"] = observations;
            }

            return request;
        }

        public static Dictionary<string, object?> BuildWakePolicyRequestFromCurrent(
            Dictionary<string, object?> currentWakePolicy,
            string? clientId,
            bool desktopObservationEnabled)
        {
            var mode = ReadString(currentWakePolicy, "mode") ?? "disabled";
            var intervalSeconds = ReadPositiveInt(currentWakePolicy, "interval_seconds");
            var threshold = ReadDouble(
                currentWakePolicy,
                "visual_observation_similarity_threshold",
                DefaultVisualObservationSimilarityThreshold);
            return BuildWakePolicyRequest(
                mode,
                intervalSeconds,
                threshold,
                clientId,
                desktopObservationEnabled,
                ReadWakeObservations(currentWakePolicy.GetValueOrDefault("observations")));
        }

        public static string BuildDesktopVisionSourceId(string? clientId)
        {
            return $"vision_source:{NormalizeVisionSourceToken(clientId)}:desktop";
        }

        private static Dictionary<string, object?> BuildDesktopWakeObservation(string? clientId)
        {
            return new Dictionary<string, object?>
            {
                ["observation_id"] = DesktopWakeObservationId,
                ["enabled"] = true,
                ["capability_id"] = "vision.capture",
                ["input"] = new Dictionary<string, object?>
                {
                    ["vision_source_id"] = BuildDesktopVisionSourceId(clientId),
                    ["mode"] = "still",
                },
            };
        }

        private static List<object?> BuildWakeObservationRequests(
            IEnumerable<Dictionary<string, object?>>? currentObservations,
            string? clientId,
            bool desktopObservationEnabled)
        {
            var observations = new List<object?>();
            if (currentObservations != null)
            {
                observations.AddRange(currentObservations
                    .Where(observation => !IsDesktopWakeObservation(observation, clientId))
                    .Select(BuildWakeObservationRequest)
                    .Where(observation => observation != null)!);
            }

            if (desktopObservationEnabled)
            {
                observations.Add(BuildDesktopWakeObservation(clientId));
            }

            return observations;
        }

        private static Dictionary<string, object?>? BuildWakeObservationRequest(Dictionary<string, object?> observation)
        {
            var observationId = ReadString(observation, "observation_id")?.Trim();
            var capabilityId = ReadString(observation, "capability_id");
            var enabled = ReadBool(observation, "enabled");
            var input = ReadObject(observation.GetValueOrDefault("input"));
            var visionSourceId = ReadString(input, "vision_source_id")?.Trim();
            var mode = ReadString(input, "mode");
            if (string.IsNullOrWhiteSpace(observationId)
                || capabilityId != "vision.capture"
                || enabled == null
                || string.IsNullOrWhiteSpace(visionSourceId)
                || mode != "still")
            {
                return null;
            }

            return new Dictionary<string, object?>
            {
                ["observation_id"] = observationId,
                ["enabled"] = enabled.Value,
                ["capability_id"] = "vision.capture",
                ["input"] = new Dictionary<string, object?>
                {
                    ["vision_source_id"] = visionSourceId,
                    ["mode"] = "still",
                },
            };
        }

        private static double ClampSimilarityThreshold(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return DefaultVisualObservationSimilarityThreshold;
            }

            if (value < 0)
            {
                return 0;
            }

            if (value > 1)
            {
                return 1;
            }

            return value;
        }

        private static string NormalizeVisionSourceToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "console";
            }

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
                {
                    builder.Append(ch);
                }
            }

            return builder.Length == 0 ? "console" : builder.ToString();
        }

        private static bool IsDesktopWakeObservation(Dictionary<string, object?> observation, string? clientId)
        {
            var observationId = ReadString(observation, "observation_id");
            if (string.Equals(observationId, DesktopWakeObservationId, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.Equals(ReadString(observation, "capability_id"), "vision.capture", StringComparison.Ordinal))
            {
                return false;
            }

            var input = ReadObject(observation.GetValueOrDefault("input"));
            var sourceId = ReadString(input, "vision_source_id");
            return string.Equals(sourceId, BuildDesktopVisionSourceId(clientId), StringComparison.Ordinal);
        }

        private static List<Dictionary<string, object?>> ReadWakeObservations(object? value)
        {
            if (value is JsonElement element)
            {
                return ReadWakeObservations(element);
            }

            if (value is IEnumerable<object?> objects)
            {
                return objects
                    .Select(ReadObject)
                    .Where(item => item.Count > 0)
                    .ToList();
            }

            return new List<Dictionary<string, object?>>();
        }

        private static List<Dictionary<string, object?>> ReadWakeObservations(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                return new List<Dictionary<string, object?>>();
            }

            return element.EnumerateArray()
                .Select(ReadObject)
                .Where(item => item.Count > 0)
                .ToList();
        }

        private static Dictionary<string, object?> ReadObject(object? value)
        {
            if (value is Dictionary<string, object?> dictionary)
            {
                return new Dictionary<string, object?>(dictionary);
            }

            if (value is JsonElement element)
            {
                return ReadObject(element);
            }

            return new Dictionary<string, object?>();
        }

        private static Dictionary<string, object?> ReadObject(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, object?>();
            }

            return element.EnumerateObject()
                .ToDictionary(property => property.Name, property => ReadJsonValue(property.Value));
        }

        private static object? ReadJsonValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => ReadObject(element),
                JsonValueKind.Array => element.EnumerateArray().Select(ReadJsonValue).ToList(),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt32(out var intValue) ? intValue : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }

        private static int? ReadPositiveInt(Dictionary<string, object?> values, string key)
        {
            if (!values.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var jsonInt) && jsonInt > 0)
                {
                    return jsonInt;
                }

                return null;
            }

            if (value is int intValue && intValue > 0)
            {
                return intValue;
            }

            return int.TryParse(value.ToString(), out var parsed) && parsed > 0 ? parsed : null;
        }

        private static double ReadDouble(Dictionary<string, object?> values, string key, double defaultValue)
        {
            if (!values.TryGetValue(key, out var value) || value == null)
            {
                return defaultValue;
            }

            if (value is JsonElement element)
            {
                return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var jsonDouble)
                    ? jsonDouble
                    : defaultValue;
            }

            if (value is double doubleValue)
            {
                return doubleValue;
            }

            if (value is float floatValue)
            {
                return floatValue;
            }

            if (value is int intValue)
            {
                return intValue;
            }

            return double.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue;
        }

        private static bool? ReadBool(Dictionary<string, object?> values, string key)
        {
            if (!values.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            if (value is JsonElement element)
            {
                return element.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null,
                };
            }

            return value is bool boolValue ? boolValue : null;
        }

        private static string? ReadString(Dictionary<string, object?> values, string key)
        {
            if (!values.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            return value.ToString();
        }
    }
}
