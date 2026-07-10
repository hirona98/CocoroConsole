using System.Linq;

namespace CocoroConsole.Controls
{
    internal static class WatcherDefaults
    {
        public static string BuildVisionSourceId(string? displayName)
        {
            var suffix = IdentifierSuffix(string.IsNullOrWhiteSpace(displayName) ? "camera" : displayName.Trim());
            return $"vision_source:{(string.IsNullOrWhiteSpace(suffix) ? "camera" : suffix)}";
        }

        public static string BuildDefaultWatcherId(string? visionSourceId)
        {
            var sourceId = string.IsNullOrWhiteSpace(visionSourceId)
                ? "vision_source:camera"
                : visionSourceId.Trim();
            var suffix = sourceId.StartsWith("vision_source:", System.StringComparison.Ordinal)
                ? sourceId["vision_source:".Length..]
                : sourceId;
            var safeSuffix = IdentifierSuffix(suffix);
            return $"watcher:{(string.IsNullOrWhiteSpace(safeSuffix) ? "camera" : safeSuffix)}";
        }

        private static string IdentifierSuffix(string value)
        {
            var characters = value
                .Trim()
                .Select(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '_')
                .ToArray();
            return new string(characters).Trim('_');
        }
    }
}
