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

        public static bool HasDesktopWakeObservation(Dictionary<string, object?> wakePolicy, string? clientId)
        {
            return ReadWakeObservations(wakePolicy.GetValueOrDefault("observations"))
                .Any(observation => IsDesktopWakeObservation(observation, clientId));
        }

        public static Dictionary<string, object?> SetDesktopWakeObservationEnabled(
            Dictionary<string, object?> wakePolicy,
            string? clientId,
            bool enabled)
        {
            var updatedWakePolicy = new Dictionary<string, object?>(wakePolicy);
            var observations = ReadWakeObservations(updatedWakePolicy.GetValueOrDefault("observations"))
                .Where(observation => !IsDesktopWakeObservation(observation, clientId))
                .Cast<object?>()
                .ToList();

            if (enabled)
            {
                observations.Add(BuildDesktopWakeObservation(clientId));
            }

            if (observations.Count > 0)
            {
                updatedWakePolicy["observations"] = observations;
            }
            else
            {
                updatedWakePolicy.Remove("observations");
            }

            return updatedWakePolicy;
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
