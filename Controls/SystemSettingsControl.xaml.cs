using CocoroConsole.Communication;
using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using CocoroConsole.Utilities;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    public enum SystemSettingsSection
    {
        Connection,
        ConversationInput,
        DesktopObservation,
        PeriodicThinking,
    }

    public partial class SystemSettingsControl : UserControl
    {
        public event EventHandler? SettingsChanged;

        private const int DefaultThinkingSpeechLevel = 5;

        private bool _isInitialized;
        private Dictionary<string, object?> _wakePolicy = new Dictionary<string, object?>();
        private List<OtomeKairoAudioInputDevice> _audioInputDevices = new List<OtomeKairoAudioInputDevice>();
        private List<ConsoleMicrophoneInputDevice> _consoleInputDevices = new List<ConsoleMicrophoneInputDevice>();

        public SystemSettingsControl()
        {
            InitializeComponent();
            ShowSection(SystemSettingsSection.Connection);
        }

        /// <summary>
        /// 左ナビで選択された責務だけを表示する。
        /// </summary>
        public void ShowSection(SystemSettingsSection section)
        {
            ConnectionSettingsGroup.Visibility =
                section == SystemSettingsSection.Connection ? Visibility.Visible : Visibility.Collapsed;
            ConversationSettingsGroup.Visibility =
                section == SystemSettingsSection.ConversationInput ? Visibility.Visible : Visibility.Collapsed;
            VoiceInputSettingsGroup.Visibility =
                section == SystemSettingsSection.ConversationInput ? Visibility.Visible : Visibility.Collapsed;
            DesktopObservationSettingsGroup.Visibility =
                section == SystemSettingsSection.DesktopObservation ? Visibility.Visible : Visibility.Collapsed;
            PeriodicThinkingSettingsGroup.Visibility =
                section == SystemSettingsSection.PeriodicThinking ? Visibility.Visible : Visibility.Collapsed;
        }

        public async System.Threading.Tasks.Task InitializeAsync(
            OtomeKairoApiClient? apiClient,
            ICommunicationService? communicationService,
            string clientId)
        {
            try
            {
                var appSettings = AppSettings.Instance;

                ApplyDefaultRemoteSettings();
                LoadConsoleAudioInputDevices();
                ApplyAppSettingsToControls(appSettings);
                SetupEventHandlers();
                _isInitialized = true;

                if (apiClient == null || communicationService == null)
                {
                    LocalInputDeviceStatusText.Text = "OtomeKairo APIへ接続するとローカル入力デバイスを取得します。";
                    SpeakerManagementControl.SetUnavailable("OtomeKairo APIへ接続すると話者を管理できます。");
                    return;
                }

                await LoadAudioInputDevicesAsync(apiClient);
                await SpeakerManagementControl.InitializeAsync(
                    apiClient,
                    communicationService,
                    clientId,
                    appSettings.MicrophoneSettings.speakerRecognitionThreshold);
            }
            catch (OtomeKairoApiException ex) when (
                ex.ErrorCode == "invalid_token" ||
                ex.ErrorCode == "bootstrap_required")
            {
                // 接続設定を修正できるよう、認証エラーでも設定画面自体は開きます。
                var message = ex.ErrorCode == "bootstrap_required"
                    ? "OtomeKairoの初回登録が完了していません。"
                    : "保存済みのアクセストークンが接続先と一致しません。";
                LocalInputDeviceStatusText.Text = $"{message}「OtomeKairo接続」で認証情報を更新してください。";
                SpeakerManagementControl.SetUnavailable(message);
                System.Diagnostics.Debug.WriteLine($"システム設定の認証待ち: {ex.ErrorCode}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"システム設定の初期化エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ApplyOtomeKairoCurrentSettings(OtomeKairoCurrentSettings current)
        {
            var wakePolicy = current?.WakePolicy ?? new Dictionary<string, object?>();
            _wakePolicy = new Dictionary<string, object?>(wakePolicy);
            var mode = ReadString(wakePolicy, "mode");
            WakePolicyEnabledCheckBox.IsChecked = string.Equals(mode, "interval", StringComparison.OrdinalIgnoreCase);
            WakeIntervalSecondsTextBox.Text = ReadInt(wakePolicy, "interval_seconds", 300).ToString(CultureInfo.InvariantCulture);
            WakeDesktopObservationCheckBox.IsChecked = DesktopWakePolicyHelper.HasDesktopWakeObservation(wakePolicy, AppSettings.Instance.ClientId);
            ThinkingSpeechLevelTextBox.Text = ClampThinkingSpeechLevel(
                current?.ThinkingSpeechLevel ?? DefaultThinkingSpeechLevel).ToString(CultureInfo.InvariantCulture);
        }

        public void ReloadFromAppSettings()
        {
            var previousInitialized = _isInitialized;
            _isInitialized = false;
            ApplyAppSettingsToControls(AppSettings.Instance);
            _isInitialized = previousInitialized;
        }

        private void ApplyAppSettingsToControls(AppSettings appSettings)
        {
            OtomeKairoServerUrlTextBox.Text = appSettings.ServerUrl;
            OtomeKairoAccessTokenPasswordBox.Password = appSettings.OtomeKairoBearerToken;
            ConversationDisplayNameTextBox.Text = appSettings.ConversationDisplayName;
            ExcludeWindowTitlePatternsTextBox.Text = string.Join(
                Environment.NewLine,
                appSettings.ScreenshotSettings.excludePatterns ?? new List<string>());

            var idleTimeoutMinutes = appSettings.ScreenshotSettings.idleTimeoutMinutes;
            VisualCaptureIdleTimeoutMinutesTextBox.Text =
                idleTimeoutMinutes.ToString(CultureInfo.InvariantCulture);

            var microphoneSettings = appSettings.MicrophoneSettings;
            SelectMicrophoneInputSource(microphoneSettings.inputSource);
            ConsoleClientIdTextBox.Text = appSettings.ClientId;
            VadProbabilityThresholdSlider.Value = microphoneSettings.vadProbabilityThreshold;
            SpeakerManagementControl.SetThreshold(microphoneSettings.speakerRecognitionThreshold);
            SelectConfiguredLocalAudioInputDevice(microphoneSettings.localInputDevice);
            if (microphoneSettings.console != null &&
                !string.Equals(microphoneSettings.console.clientId, appSettings.ClientId, StringComparison.Ordinal))
            {
                SelectConfiguredConsoleAudioInputDevice(null);
                ConsoleInputDeviceStatusText.Text =
                    $"別のCocoroConsole（{microphoneSettings.console.clientId}）が設定されています。";
            }
            else
            {
                SelectConfiguredConsoleAudioInputDevice(microphoneSettings.console?.inputDevice);
            }
        }

        private async System.Threading.Tasks.Task LoadAudioInputDevicesAsync(OtomeKairoApiClient apiClient)
        {
            var response = await apiClient.GetAudioInputDevicesAsync();
            _audioInputDevices = response.Devices;
            LocalInputDeviceComboBox.ItemsSource = _audioInputDevices;
            SelectConfiguredLocalAudioInputDevice(AppSettings.Instance.MicrophoneSettings.localInputDevice);

            LocalInputDeviceStatusText.Text = response.ConnectorConnected
                ? $"connector: {response.ConnectorClientId} / {_audioInputDevices.Count}件"
                : "microphone connectorは未接続です。";
        }

        private void LoadConsoleAudioInputDevices()
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            _consoleInputDevices = new List<ConsoleMicrophoneInputDevice>();
            foreach (var endpoint in endpoints)
            {
                using (endpoint)
                {
                    _consoleInputDevices.Add(new ConsoleMicrophoneInputDevice
                    {
                        deviceId = endpoint.ID,
                        name = endpoint.FriendlyName,
                    });
                }
            }
            ConsoleInputDeviceComboBox.ItemsSource = _consoleInputDevices;
            ConsoleInputDeviceStatusText.Text = $"WASAPI / {_consoleInputDevices.Count}件";
        }

        private void SelectMicrophoneInputSource(string inputSource)
        {
            var selected = MicrophoneInputSourceComboBox.Items
                .OfType<ComboBoxItem>()
                .Single(item => string.Equals(item.Tag as string, inputSource, StringComparison.Ordinal));
            MicrophoneInputSourceComboBox.SelectedItem = selected;
        }

        private void SelectConfiguredLocalAudioInputDevice(MicrophoneInputDevice? configuredDevice)
        {
            if (configuredDevice == null)
            {
                LocalInputDeviceComboBox.SelectedItem = null;
                return;
            }

            var selectedDevice = _audioInputDevices.FirstOrDefault(device =>
                string.Equals(device.HostApi, configuredDevice.hostApi, StringComparison.Ordinal) &&
                string.Equals(device.Name, configuredDevice.name, StringComparison.Ordinal));
            if (selectedDevice == null)
            {
                selectedDevice = new OtomeKairoAudioInputDevice
                {
                    HostApi = configuredDevice.hostApi,
                    Name = configuredDevice.name,
                };
                _audioInputDevices = new[] { selectedDevice }
                    .Concat(_audioInputDevices)
                    .ToList();
                LocalInputDeviceComboBox.ItemsSource = _audioInputDevices;
            }

            LocalInputDeviceComboBox.SelectedItem = selectedDevice;
        }

        private void SelectConfiguredConsoleAudioInputDevice(ConsoleMicrophoneInputDevice? configuredDevice)
        {
            if (configuredDevice == null)
            {
                ConsoleInputDeviceComboBox.SelectedItem = null;
                return;
            }

            var selectedDevice = _consoleInputDevices.FirstOrDefault(device =>
                string.Equals(device.deviceId, configuredDevice.deviceId, StringComparison.Ordinal));
            if (selectedDevice == null)
            {
                selectedDevice = configuredDevice.DeepCopy();
                _consoleInputDevices = new[] { selectedDevice }
                    .Concat(_consoleInputDevices)
                    .ToList();
                ConsoleInputDeviceComboBox.ItemsSource = _consoleInputDevices;
                ConsoleInputDeviceStatusText.Text = "保存済みデバイスは現在利用できません。";
            }

            ConsoleInputDeviceComboBox.SelectedItem = selectedDevice;
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
            ThinkingSpeechLevelTextBox.Text = DefaultThinkingSpeechLevel.ToString(CultureInfo.InvariantCulture);
        }

        private void SetupEventHandlers()
        {
            OtomeKairoServerUrlTextBox.TextChanged += OnSettingsChanged;
            OtomeKairoAccessTokenPasswordBox.PasswordChanged += OnSettingsChanged;
            ConversationDisplayNameTextBox.TextChanged += OnSettingsChanged;
            VisualCaptureIdleTimeoutMinutesTextBox.TextChanged += OnSettingsChanged;
            ExcludeWindowTitlePatternsTextBox.TextChanged += OnSettingsChanged;
            WakePolicyEnabledCheckBox.Checked += OnSettingsChanged;
            WakePolicyEnabledCheckBox.Unchecked += OnSettingsChanged;
            WakeDesktopObservationCheckBox.Checked += OnSettingsChanged;
            WakeDesktopObservationCheckBox.Unchecked += OnSettingsChanged;
            WakeIntervalSecondsTextBox.TextChanged += OnSettingsChanged;
            ThinkingSpeechLevelTextBox.TextChanged += OnSettingsChanged;
            MicrophoneInputSourceComboBox.SelectionChanged += OnSettingsChanged;
            LocalInputDeviceComboBox.SelectionChanged += OnSettingsChanged;
            ConsoleInputDeviceComboBox.SelectionChanged += OnSettingsChanged;
            VadProbabilityThresholdSlider.ValueChanged += OnSettingsChanged;
            SpeakerManagementControl.ThresholdChanged += OnSpeakerThresholdChanged;
        }

        private void OnSettingsChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                return;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnSpeakerThresholdChanged(object? sender, EventArgs e)
        {
            if (_isInitialized)
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
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

        public int GetThinkingSpeechLevel()
        {
            if (!int.TryParse(ThinkingSpeechLevelTextBox.Text, out var level))
            {
                return DefaultThinkingSpeechLevel;
            }

            return ClampThinkingSpeechLevel(level);
        }

        private static int ClampThinkingSpeechLevel(int level)
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
            var sourceItem = MicrophoneInputSourceComboBox.SelectedItem as ComboBoxItem
                ?? throw new InvalidOperationException("通常のマイク入力元を選択してください。");
            var inputSource = sourceItem.Tag as string
                ?? throw new InvalidOperationException("通常のマイク入力元が不正です。");
            var selectedLocalDevice = LocalInputDeviceComboBox.SelectedItem as OtomeKairoAudioInputDevice;
            var selectedConsoleDevice = ConsoleInputDeviceComboBox.SelectedItem as ConsoleMicrophoneInputDevice;
            return new MicrophoneSettings
            {
                inputSource = inputSource,
                localInputDevice = selectedLocalDevice == null
                    ? null
                    : new MicrophoneInputDevice
                    {
                        hostApi = selectedLocalDevice.HostApi,
                        name = selectedLocalDevice.Name,
                    },
                console = selectedConsoleDevice == null
                    ? AppSettings.Instance.MicrophoneSettings.console?.DeepCopy()
                    : new ConsoleMicrophoneSettings
                    {
                        clientId = AppSettings.Instance.ClientId,
                        inputDevice = selectedConsoleDevice.DeepCopy(),
                    },
                vadProbabilityThreshold = (float)VadProbabilityThresholdSlider.Value,
                speakerRecognitionThreshold = SpeakerManagementControl.GetCurrentThreshold(),
            };
        }

        public string GetOtomeKairoAccessToken()
        {
            return OtomeKairoAccessTokenPasswordBox.Password.Trim();
        }

        public void SetOtomeKairoAccessToken(string accessToken)
        {
            // 自動取得した接続情報の反映をユーザー編集として扱わない。
            var previousInitialized = _isInitialized;
            _isInitialized = false;
            OtomeKairoAccessTokenPasswordBox.Password = accessToken;
            _isInitialized = previousInitialized;
        }

        public string GetOtomeKairoServerUrl()
        {
            return OtomeKairoServerUrlTextBox.Text.Trim();
        }

        public void SetOtomeKairoServerUrl(string serverUrl)
        {
            // 保存時の正規化結果をユーザー編集として扱わず画面へ反映します。
            var previousInitialized = _isInitialized;
            _isInitialized = false;
            OtomeKairoServerUrlTextBox.Text = serverUrl;
            _isInitialized = previousInitialized;
        }

        public string GetConversationDisplayName()
        {
            return ConversationDisplayNameTextBox.Text.Trim();
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
