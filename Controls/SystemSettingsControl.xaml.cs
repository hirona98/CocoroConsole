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
        ConversationInput,
        DesktopObservation,
        PeriodicThinking,
    }

    public partial class SystemSettingsControl : UserControl
    {
        public event EventHandler? SettingsChanged;

        private const int DefaultThinkingSpeechLevel = 5;

        private bool _isInitialized;
        private int _currentAvatarIndex = -1;
        private Dictionary<string, object?> _wakePolicy = new Dictionary<string, object?>();
        private List<OtomeKairoAudioInputDevice> _audioInputDevices = new List<OtomeKairoAudioInputDevice>();
        private List<ConsoleMicrophoneInputDevice> _consoleInputDevices = new List<ConsoleMicrophoneInputDevice>();

        public SystemSettingsControl()
        {
            InitializeComponent();
            ShowSection(SystemSettingsSection.ConversationInput);
        }

        /// <summary>
        /// 左ナビで選択された責務だけを表示する。
        /// </summary>
        public void ShowSection(SystemSettingsSection section)
        {
            ConversationSettingsGroup.Visibility =
                section == SystemSettingsSection.ConversationInput ? Visibility.Visible : Visibility.Collapsed;
            SpeechRecognitionSettingsGroup.Visibility =
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
                var message = ex.ErrorCode == "bootstrap_required"
                    ? "OtomeKairoの初回登録が完了していません。"
                    : "保存済みのアクセストークンが接続先と一致しません。";
                LocalInputDeviceStatusText.Text = $"{message}トレイの「接続先設定」で接続し直してください。";
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

        public void ReloadFromAppSettings(int avatarIndex)
        {
            var previousInitialized = _isInitialized;
            _isInitialized = false;
            ApplyAppSettingsToControls(AppSettings.Instance, avatarIndex);
            _isInitialized = previousInitialized;
        }

        public void SelectAvatar(int avatarIndex)
        {
            var previousInitialized = _isInitialized;
            _isInitialized = false;
            LoadSpeechRecognitionSettings(AppSettings.Instance, avatarIndex);
            _isInitialized = previousInitialized;
        }

        private void ApplyAppSettingsToControls(AppSettings appSettings, int? avatarIndex = null)
        {
            ConversationDisplayNameTextBox.Text = appSettings.ConversationDisplayName;
            LoadSpeechRecognitionSettings(appSettings, avatarIndex ?? appSettings.CurrentAvatarIndex);
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

        /// <summary>
        /// 選択中のアバター音声プリセットから音声認識設定を読み込む。
        /// </summary>
        private void LoadSpeechRecognitionSettings(AppSettings appSettings, int avatarIndex)
        {
            _currentAvatarIndex = avatarIndex;
            var hasSelectedAvatar =
                avatarIndex >= 0 && avatarIndex < appSettings.AvatarList.Count;
            SpeechRecognitionSettingsGroup.IsEnabled = hasSelectedAvatar;

            if (!hasSelectedAvatar)
            {
                IsUseSTTCheckBox.IsChecked = false;
                STTEngineComboBox.SelectedItem = null;
                STTProfileIdTextBox.Clear();
                STTApiKeyPasswordBox.Clear();
                return;
            }

            var avatar = appSettings.AvatarList[avatarIndex];
            IsUseSTTCheckBox.IsChecked = avatar.isUseSTT;
            STTEngineComboBox.SelectedItem = STTEngineComboBox.Items
                .OfType<ComboBoxItem>()
                .Single(item => string.Equals(
                    item.Tag as string,
                    avatar.sttEngine,
                    StringComparison.Ordinal));
            STTProfileIdTextBox.Text = avatar.sttProfileId;
            STTApiKeyPasswordBox.Text = avatar.sttApiKey;
        }

        /// <summary>
        /// 会話入力ページの音声認識設定を選択中のアバターへ反映する。
        /// </summary>
        public void SyncSpeechRecognitionSettingsToSelectedAvatar()
        {
            if (_currentAvatarIndex < 0 ||
                _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
            {
                return;
            }

            var avatar = AppSettings.Instance.AvatarList[_currentAvatarIndex].DeepCopy();
            avatar.isUseSTT = IsUseSTTCheckBox.IsChecked ?? false;
            var selectedEngine = STTEngineComboBox.SelectedItem as ComboBoxItem
                ?? throw new InvalidOperationException("音声認識エンジンを選択してください。");
            avatar.sttEngine = selectedEngine.Tag?.ToString()
                ?? throw new InvalidOperationException("音声認識エンジンの設定が不正です。");
            avatar.sttProfileId = STTProfileIdTextBox.Text.Trim();
            avatar.sttApiKey = STTApiKeyPasswordBox.Text;
            AppSettings.Instance.AvatarList[_currentAvatarIndex] = avatar;
        }

        private async System.Threading.Tasks.Task LoadAudioInputDevicesAsync(OtomeKairoApiClient apiClient)
        {
            var response = await apiClient.GetAudioInputDevicesAsync();
            _audioInputDevices = response.Devices;
            LocalInputDeviceComboBox.ItemsSource = _audioInputDevices;
            SelectConfiguredLocalAudioInputDevice(AppSettings.Instance.MicrophoneSettings.localInputDevice);

            // connector が利用できない間も、空欄ではなく未検出状態を明示する。
            SetLocalInputDeviceAvailability(
                response.ConnectorConnected && response.Devices.Count > 0);

            LocalInputDeviceStatusText.Text = response.ConnectorConnected
                ? $"connector: {response.ConnectorClientId} / {response.Devices.Count}件"
                : "microphone connectorは未接続です。";
        }

        private void SetLocalInputDeviceAvailability(bool available)
        {
            LocalInputDeviceComboBox.Visibility = available
                ? Visibility.Visible
                : Visibility.Collapsed;
            LocalInputDeviceUnavailableComboBox.Visibility = available
                ? Visibility.Collapsed
                : Visibility.Visible;
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
            ConversationDisplayNameTextBox.TextChanged += OnSettingsChanged;
            IsUseSTTCheckBox.Checked += OnSpeechRecognitionSettingsChanged;
            IsUseSTTCheckBox.Unchecked += OnSpeechRecognitionSettingsChanged;
            STTEngineComboBox.SelectionChanged += OnSpeechRecognitionSettingsChanged;
            STTProfileIdTextBox.TextChanged += OnSpeechRecognitionSettingsChanged;
            STTApiKeyPasswordBox.TextChanged += OnSpeechRecognitionSettingsChanged;
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

        private void OnSpeechRecognitionSettingsChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                return;
            }

            // 他のアバターへ切り替える前に現在の編集値を保持する。
            SyncSpeechRecognitionSettingsToSelectedAvatar();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void STTApiKeyPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(STTApiKeyPasswordBox);
        }

        private void STTApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(STTApiKeyPasswordBox);
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
