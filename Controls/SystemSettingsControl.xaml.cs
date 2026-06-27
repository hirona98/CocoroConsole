using CocoroConsole.Communication;
using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using CocoroConsole.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    public partial class SystemSettingsControl : UserControl
    {
        public event EventHandler? SettingsChanged;

        private const int DefaultBackgroundWakeSpeechFrequencyLevel = 5;

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
            WakeDesktopObservationCheckBox.IsChecked = DesktopWakePolicyHelper.HasDesktopWakeObservation(wakePolicy, AppSettings.Instance.ClientId);
            BackgroundWakeSpeechFrequencyLevelTextBox.Text = ClampBackgroundWakeSpeechFrequencyLevel(
                current?.BackgroundWakeSpeechFrequencyLevel ?? DefaultBackgroundWakeSpeechFrequencyLevel).ToString(CultureInfo.InvariantCulture);
        }

        public void SetWakeDesktopObservationEnabled(bool enabled)
        {
            var previousInitialized = _isInitialized;
            _isInitialized = false;
            WakeDesktopObservationCheckBox.IsChecked = enabled;
            _isInitialized = previousInitialized;
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
            BackgroundWakeSpeechFrequencyLevelTextBox.Text = DefaultBackgroundWakeSpeechFrequencyLevel.ToString(CultureInfo.InvariantCulture);
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
            BackgroundWakeSpeechFrequencyLevelTextBox.TextChanged += OnSettingsChanged;
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
            var mode = WakePolicyEnabledCheckBox.IsChecked ?? false ? "interval" : "disabled";
            int? intervalSeconds = null;
            if (int.TryParse(WakeIntervalSecondsTextBox.Text, out var parsed) && parsed > 0)
            {
                intervalSeconds = parsed;
            }

            var wakePolicy = new Dictionary<string, object?>(_wakePolicy)
            {
                ["mode"] = mode,
                ["interval_seconds"] = intervalSeconds,
            };
            return DesktopWakePolicyHelper.BuildWakePolicyRequestFromCurrent(
                wakePolicy,
                AppSettings.Instance.ClientId,
                WakeDesktopObservationCheckBox.IsChecked ?? false);
        }

        public int GetBackgroundWakeSpeechFrequencyLevel()
        {
            if (!int.TryParse(BackgroundWakeSpeechFrequencyLevelTextBox.Text, out var level))
            {
                return DefaultBackgroundWakeSpeechFrequencyLevel;
            }

            return ClampBackgroundWakeSpeechFrequencyLevel(level);
        }

        private static int ClampBackgroundWakeSpeechFrequencyLevel(int level)
        {
            if (level < 1)
            {
                return 1;
            }

            if (level > 10)
            {
                return 10;
            }

            return level;
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
