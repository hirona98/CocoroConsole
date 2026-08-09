using CocoroConsole.Communication;
using CocoroConsole.Services;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    /// <summary>
    /// マイク入力元と CocoroConsole 側デバイスの設定 UI。
    /// </summary>
    public partial class SystemSettingsControl : UserControl
    {
        public event EventHandler? SettingsChanged;

        private bool _isInitialized;
        private List<ConsoleMicrophoneInputDevice> _consoleInputDevices = new List<ConsoleMicrophoneInputDevice>();

        public SystemSettingsControl()
        {
            InitializeComponent();
        }

        public System.Threading.Tasks.Task InitializeAsync(
            OtomeKairoApiClient? apiClient,
            ICommunicationService? communicationService,
            string clientId)
        {
            try
            {
                LoadConsoleAudioInputDevices();
                ApplyAppSettingsToControls(AppSettings.Instance);
                SetupEventHandlers();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"マイク設定の初期化エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return System.Threading.Tasks.Task.CompletedTask;
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
            var microphoneSettings = appSettings.MicrophoneSettings;
            SelectMicrophoneInputSource(microphoneSettings.inputSource);
            LocalInputDeviceTextBox.Text = microphoneSettings.localInputDevice == null
                ? "未設定"
                : $"{microphoneSettings.localInputDevice.hostApi}: {microphoneSettings.localInputDevice.name}";
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

        private void SetupEventHandlers()
        {
            MicrophoneInputSourceComboBox.SelectionChanged += OnSettingsChanged;
            ConsoleInputDeviceComboBox.SelectionChanged += OnSettingsChanged;
        }

        private void OnSettingsChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                return;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// UI で編集する入力元と Console デバイスだけを反映し、
        /// OtomeKairo 側デバイス・VAD・話者しきい値は既存値を保持する。
        /// </summary>
        public MicrophoneSettings GetMicrophoneSettings()
        {
            var current = AppSettings.Instance.MicrophoneSettings;
            var sourceItem = MicrophoneInputSourceComboBox.SelectedItem as ComboBoxItem
                ?? throw new InvalidOperationException("通常のマイク入力元を選択してください。");
            var inputSource = sourceItem.Tag as string
                ?? throw new InvalidOperationException("通常のマイク入力元が不正です。");
            var selectedConsoleDevice = ConsoleInputDeviceComboBox.SelectedItem as ConsoleMicrophoneInputDevice;
            if (string.Equals(inputSource, "console_microphone", StringComparison.Ordinal) &&
                selectedConsoleDevice == null)
            {
                throw new InvalidOperationException("CocoroConsoleの入力デバイスを選択してください。");
            }

            return new MicrophoneSettings
            {
                inputSource = inputSource,
                localInputDevice = current.localInputDevice?.DeepCopy(),
                console = selectedConsoleDevice == null
                    ? current.console?.DeepCopy()
                        ?? new ConsoleMicrophoneSettings
                        {
                            clientId = AppSettings.Instance.ClientId,
                            inputDevice = null,
                        }
                    : new ConsoleMicrophoneSettings
                    {
                        clientId = AppSettings.Instance.ClientId,
                        inputDevice = selectedConsoleDevice.DeepCopy(),
                    },
                vadProbabilityThreshold = current.vadProbabilityThreshold,
                speakerRecognitionThreshold = current.speakerRecognitionThreshold,
            };
        }
    }
}
