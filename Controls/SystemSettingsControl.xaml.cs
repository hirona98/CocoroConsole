using CocoroConsole.Communication;
using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    /// <summary>
    /// SystemSettingsControl.xaml の相互作用ロジック。
    /// </summary>
    public partial class SystemSettingsControl : UserControl
    {
        public event EventHandler? SettingsChanged;

        private bool _isInitialized;

        public SystemSettingsControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        public Task InitializeAsync()
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

            return Task.CompletedTask;
        }

        /// <summary>
        /// OtomeKairo から取得した現在設定を反映する。
        /// </summary>
        public void ApplyOtomeKairoCurrentSettings(OtomeKairoCurrentSettings current)
        {
            var desktopWatch = current?.DesktopWatch ?? new OtomeKairoDesktopWatchSettings();
            DesktopWatchEnabledCheckBox.IsChecked = desktopWatch.Enabled;
            DesktopWatchIntervalSecondsTextBox.Text = (desktopWatch.IntervalSeconds > 0 ? desktopWatch.IntervalSeconds : 300)
                .ToString(CultureInfo.InvariantCulture);
        }

        private void ApplyDefaultRemoteSettings()
        {
            DesktopWatchEnabledCheckBox.IsChecked = false;
            DesktopWatchIntervalSecondsTextBox.Text = "300";
        }

        private void SetupEventHandlers()
        {
            DesktopWatchEnabledCheckBox.Checked += OnSettingsChanged;
            DesktopWatchEnabledCheckBox.Unchecked += OnSettingsChanged;
            DesktopWatchIntervalSecondsTextBox.TextChanged += OnSettingsChanged;
            DesktopWatchIdleTimeoutMinutesTextBox.TextChanged += OnSettingsChanged;
            ExcludeWindowTitlePatternsTextBox.TextChanged += OnSettingsChanged;
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
    }
}
