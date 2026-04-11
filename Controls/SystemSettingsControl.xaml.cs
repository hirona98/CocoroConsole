using CocoroConsole.Communication;
using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
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

        private bool _isInitialized;

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
                DesktopWatchIdleTimeoutMinutesTextBox.Text = idleTimeoutMinutes.ToString(CultureInfo.InvariantCulture);

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
            var desktopWatch = current?.DesktopWatch ?? new OtomeKairoDesktopWatchSettings();
            DesktopWatchEnabledCheckBox.IsChecked = desktopWatch.Enabled;
            DesktopWatchIntervalSecondsTextBox.Text = (desktopWatch.IntervalSeconds > 0 ? desktopWatch.IntervalSeconds : 300)
                .ToString(CultureInfo.InvariantCulture);

            var wakePolicy = current?.WakePolicy ?? new Dictionary<string, object?>();
            var mode = ReadString(wakePolicy, "mode");
            WakePolicyEnabledCheckBox.IsChecked = string.Equals(mode, "interval", StringComparison.OrdinalIgnoreCase);
            WakeIntervalMinutesTextBox.Text = ReadInt(wakePolicy, "interval_minutes", 5).ToString(CultureInfo.InvariantCulture);
        }

        private void ApplyDefaultRemoteSettings()
        {
            DesktopWatchEnabledCheckBox.IsChecked = false;
            DesktopWatchIntervalSecondsTextBox.Text = "300";
            WakePolicyEnabledCheckBox.IsChecked = false;
            WakeIntervalMinutesTextBox.Text = "5";
        }

        private void SetupEventHandlers()
        {
            DesktopWatchEnabledCheckBox.Checked += OnSettingsChanged;
            DesktopWatchEnabledCheckBox.Unchecked += OnSettingsChanged;
            DesktopWatchIntervalSecondsTextBox.TextChanged += OnSettingsChanged;
            DesktopWatchIdleTimeoutMinutesTextBox.TextChanged += OnSettingsChanged;
            ExcludeWindowTitlePatternsTextBox.TextChanged += OnSettingsChanged;
            WakePolicyEnabledCheckBox.Checked += OnSettingsChanged;
            WakePolicyEnabledCheckBox.Unchecked += OnSettingsChanged;
            WakeIntervalMinutesTextBox.TextChanged += OnSettingsChanged;
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

        public bool GetDesktopWatchEnabled()
        {
            return DesktopWatchEnabledCheckBox.IsChecked ?? false;
        }

        public int GetDesktopWatchIntervalSeconds()
        {
            if (int.TryParse(DesktopWatchIntervalSecondsTextBox.Text, out var seconds) && seconds > 0)
            {
                return seconds;
            }

            return 300;
        }

        public Dictionary<string, object?> GetWakePolicy()
        {
            if (WakePolicyEnabledCheckBox.IsChecked ?? false)
            {
                var intervalMinutes = 5;
                if (int.TryParse(WakeIntervalMinutesTextBox.Text, out var parsed) && parsed > 0)
                {
                    intervalMinutes = parsed;
                }

                return new Dictionary<string, object?>
                {
                    ["mode"] = "interval",
                    ["interval_minutes"] = intervalMinutes,
                };
            }

            return new Dictionary<string, object?>
            {
                ["mode"] = "disabled",
            };
        }

        public int GetDesktopWatchIdleTimeoutMinutes()
        {
            if (!int.TryParse(DesktopWatchIdleTimeoutMinutesTextBox.Text, out var minutes))
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
