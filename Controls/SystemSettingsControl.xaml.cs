using CocoroConsole.Communication;
using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    public partial class SystemSettingsControl : UserControl
    {
        public event EventHandler? SettingsChanged;

        private const string DesktopWakeObservationId = "observation:main_desktop";

        private bool _isInitialized;
        private Dictionary<string, object?> _wakePolicy = new Dictionary<string, object?>();

        public SystemSettingsControl()
        {
            InitializeComponent();
        }

        public System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                var appSettings = AppSettings.Instance;

                ApplyDefaultRemoteSettings();

                ExcludeWindowTitlePatternsTextBox.Text = string.Join(
                    Environment.NewLine,
                    appSettings.ScreenshotSettings.excludePatterns ?? new List<string>());

                var idleTimeoutMinutes = appSettings.ScreenshotSettings.idleTimeoutMinutes;
                if (idleTimeoutMinutes < 0)
                {
                    idleTimeoutMinutes = 10;
                }
                VisualCaptureIdleTimeoutMinutesTextBox.Text = idleTimeoutMinutes.ToString(CultureInfo.InvariantCulture);

                MicThresholdSlider.Value = appSettings.MicrophoneSettings.inputThreshold;

                var dbPath = System.IO.Path.Combine(appSettings.UserDataDirectory, "SpeakerRecognition.db");
                var speakerService = new SpeakerRecognitionService(dbPath, appSettings.MicrophoneSettings.speakerRecognitionThreshold);
                SpeakerManagementControl.Initialize(speakerService, appSettings.MicrophoneSettings.speakerRecognitionThreshold);

                SetupEventHandlers();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"システム設定の初期化エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void ApplyOtomeKairoCurrentSettings(OtomeKairoCurrentSettings current)
        {
            var wakePolicy = current?.WakePolicy ?? new Dictionary<string, object?>();
            _wakePolicy = new Dictionary<string, object?>(wakePolicy);
            var mode = ReadString(wakePolicy, "mode");
            WakePolicyEnabledCheckBox.IsChecked = string.Equals(mode, "interval", StringComparison.OrdinalIgnoreCase);
            WakeIntervalSecondsTextBox.Text = ReadInt(wakePolicy, "interval_seconds", 300).ToString(CultureInfo.InvariantCulture);
            WakeDesktopObservationCheckBox.IsChecked = HasDesktopWakeObservation(wakePolicy);
        }

        private void ApplyDefaultRemoteSettings()
        {
            _wakePolicy = new Dictionary<string, object?>
            {
                ["mode"] = "disabled",
            };
            WakePolicyEnabledCheckBox.IsChecked = false;
            WakeDesktopObservationCheckBox.IsChecked = false;
            WakeIntervalSecondsTextBox.Text = "300";
        }

        private void SetupEventHandlers()
        {
            VisualCaptureIdleTimeoutMinutesTextBox.TextChanged += OnSettingsChanged;
            ExcludeWindowTitlePatternsTextBox.TextChanged += OnSettingsChanged;
            WakePolicyEnabledCheckBox.Checked += OnSettingsChanged;
            WakePolicyEnabledCheckBox.Unchecked += OnSettingsChanged;
            WakeDesktopObservationCheckBox.Checked += OnSettingsChanged;
            WakeDesktopObservationCheckBox.Unchecked += OnSettingsChanged;
            WakeIntervalSecondsTextBox.TextChanged += OnSettingsChanged;
            MicThresholdSlider.ValueChanged += OnSettingsChanged;
        }

        private void OnSettingsChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                return;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public Dictionary<string, object?> GetWakePolicy()
        {
            var wakePolicy = new Dictionary<string, object?>(_wakePolicy);
            if (WakePolicyEnabledCheckBox.IsChecked ?? false)
            {
                var intervalSeconds = 300;
                if (int.TryParse(WakeIntervalSecondsTextBox.Text, out var parsed) && parsed > 0)
                {
                    intervalSeconds = parsed;
                }

                wakePolicy["mode"] = "interval";
                wakePolicy["interval_seconds"] = intervalSeconds;
                ApplyDesktopWakeObservation(wakePolicy, WakeDesktopObservationCheckBox.IsChecked ?? false);
                return wakePolicy;
            }

            wakePolicy["mode"] = "disabled";
            ApplyDesktopWakeObservation(wakePolicy, WakeDesktopObservationCheckBox.IsChecked ?? false);
            return wakePolicy;
        }

        private static bool HasDesktopWakeObservation(Dictionary<string, object?> wakePolicy)
        {
            return ReadWakeObservations(wakePolicy.GetValueOrDefault("observations"))
                .Any(IsDesktopWakeObservation);
        }

        private static void ApplyDesktopWakeObservation(Dictionary<string, object?> wakePolicy, bool enabled)
        {
            var observations = ReadWakeObservations(wakePolicy.GetValueOrDefault("observations"))
                .Where(observation => !IsDesktopWakeObservation(observation))
                .Cast<object?>()
                .ToList();

            if (enabled)
            {
                observations.Add(BuildDesktopWakeObservation());
            }

            if (observations.Count > 0)
            {
                wakePolicy["observations"] = observations;
            }
            else
            {
                wakePolicy.Remove("observations");
            }
        }

        private static Dictionary<string, object?> BuildDesktopWakeObservation()
        {
            return new Dictionary<string, object?>
            {
                ["observation_id"] = DesktopWakeObservationId,
                ["enabled"] = true,
                ["capability_id"] = "vision.capture",
                ["input"] = new Dictionary<string, object?>
                {
                    ["vision_source_id"] = BuildDesktopVisionSourceId(),
                    ["mode"] = "still",
                },
            };
        }

        private static string BuildDesktopVisionSourceId()
        {
            return $"vision_source:{NormalizeVisionSourceToken(AppSettings.Instance.ClientId)}:desktop";
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

        private static bool IsDesktopWakeObservation(Dictionary<string, object?> observation)
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
            return string.Equals(sourceId, BuildDesktopVisionSourceId(), StringComparison.Ordinal);
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

        public int GetVisualCaptureIdleTimeoutMinutes()
        {
            if (!int.TryParse(VisualCaptureIdleTimeoutMinutesTextBox.Text, out var minutes))
            {
                return 10;
            }

            if (minutes < 0)
            {
                return 10;
            }

            return minutes;
        }

        public MicrophoneSettings GetMicrophoneSettings()
        {
            return new MicrophoneSettings
            {
                inputThreshold = (int)MicThresholdSlider.Value,
                speakerRecognitionThreshold = SpeakerManagementControl.GetCurrentThreshold(),
            };
        }

        public List<string> GetWindowTitleExcludePatterns()
        {
            return ExcludeWindowTitlePatternsTextBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
        }

        private static string? ReadString(Dictionary<string, object?> values, string key)
        {
            if (!values.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            return value.ToString();
        }

        private static int ReadInt(Dictionary<string, object?> values, string key, int fallback)
        {
            if (!values.TryGetValue(key, out var value) || value == null)
            {
                return fallback;
            }

            if (value is int intValue)
            {
                return intValue;
            }

            if (int.TryParse(value.ToString(), out var parsed))
            {
                return parsed;
            }

            return fallback;
        }
    }
}
