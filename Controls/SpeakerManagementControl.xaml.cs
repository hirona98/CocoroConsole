using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    /// <summary>
    /// OtomeKairo の話者 API と登録 runtime を操作します。
    /// </summary>
    public partial class SpeakerManagementControl : UserControl
    {
        private OtomeKairoApiClient? _apiClient;
        private ICommunicationService? _communicationService;
        private string _ownerClientId = string.Empty;
        private OtomeKairoSpeakerEnrollment? _activeEnrollment;
        private float _currentThreshold = 0.6f;

        public event EventHandler? ThresholdChanged;

        public SpeakerManagementControl()
        {
            InitializeComponent();
            ThresholdSlider.Value = _currentThreshold;
            UpdateThresholdText();
            Unloaded += OnUnloaded;
        }

        public async Task InitializeAsync(
            OtomeKairoApiClient apiClient,
            ICommunicationService communicationService,
            string ownerClientId,
            float threshold)
        {
            if (_communicationService != null)
            {
                _communicationService.AudioRuntimeStateChanged -= OnAudioRuntimeStateChanged;
            }

            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _communicationService = communicationService ?? throw new ArgumentNullException(nameof(communicationService));
            _ownerClientId = string.IsNullOrWhiteSpace(ownerClientId)
                ? throw new ArgumentException("ownerClientIdを指定してください", nameof(ownerClientId))
                : ownerClientId.Trim();
            _communicationService.AudioRuntimeStateChanged += OnAudioRuntimeStateChanged;
            SpeakerActionsPanel.IsEnabled = true;
            SetThreshold(threshold);
            await RefreshSpeakerListAsync();
        }

        public void SetUnavailable(string message)
        {
            SpeakerActionsPanel.IsEnabled = false;
            SpeakersListBox.IsEnabled = false;
            EnrollmentStatusText.Text = message;
        }

        public void SetThreshold(float threshold)
        {
            _currentThreshold = threshold;
            ThresholdSlider.Value = threshold;
            UpdateThresholdText();
        }

        public float GetCurrentThreshold()
        {
            return _currentThreshold;
        }

        private async Task RefreshSpeakerListAsync()
        {
            if (_apiClient == null)
            {
                return;
            }

            var response = await _apiClient.GetAudioSpeakersAsync();
            SpeakersListBox.ItemsSource = response.Speakers;
            SpeakersListBox.IsEnabled = true;
        }

        private async void StartEnrollment_Click(object sender, RoutedEventArgs e)
        {
            var displayName = NewSpeakerNameBox.Text.Trim();
            if (displayName.Length == 0)
            {
                MessageBox.Show(
                    "話者の呼ばれ方を入力してください。",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            await StartEnrollmentAsync(new OtomeKairoSpeakerEnrollmentRequest
            {
                OwnerClientId = _ownerClientId,
                DisplayName = displayName,
            });
        }

        private async void ReenrollSpeaker_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not OtomeKairoAudioSpeaker speaker)
            {
                return;
            }

            await StartEnrollmentAsync(new OtomeKairoSpeakerEnrollmentRequest
            {
                OwnerClientId = _ownerClientId,
                PersonRef = speaker.PersonRef,
            });
        }

        private async Task StartEnrollmentAsync(OtomeKairoSpeakerEnrollmentRequest request)
        {
            if (_apiClient == null)
            {
                return;
            }

            try
            {
                _activeEnrollment = await _apiClient.StartSpeakerEnrollmentAsync(request);
                ApplyEnrollmentState(_activeEnrollment);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"話者登録を開始できませんでした: {ex.Message}",
                    "話者登録エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void CancelEnrollment_Click(object sender, RoutedEventArgs e)
        {
            if (_apiClient == null || _activeEnrollment == null)
            {
                return;
            }

            try
            {
                await _apiClient.CancelSpeakerEnrollmentAsync(_activeEnrollment.EnrollmentId);
                _activeEnrollment = null;
                ApplyEnrollmentState(null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"話者登録を中止できませんでした: {ex.Message}",
                    "話者登録エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void RenameSpeaker_Click(object sender, RoutedEventArgs e)
        {
            if (_apiClient == null ||
                sender is not Button button ||
                button.Tag is not OtomeKairoAudioSpeaker speaker)
            {
                return;
            }

            var displayName = speaker.DisplayName.Trim();
            if (displayName.Length == 0)
            {
                MessageBox.Show(
                    "話者の呼ばれ方を入力してください。",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                await _apiClient.RenameAudioSpeakerAsync(speaker.PersonRef, displayName);
                await RefreshSpeakerListAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"話者名を変更できませんでした: {ex.Message}",
                    "話者管理エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void UnregisterSpeaker_Click(object sender, RoutedEventArgs e)
        {
            if (_apiClient == null ||
                sender is not Button button ||
                button.Tag is not OtomeKairoAudioSpeaker speaker)
            {
                return;
            }

            var result = MessageBox.Show(
                $"「{speaker.DisplayName}」の音声登録を解除します。",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await _apiClient.UnregisterAudioSpeakerAsync(speaker.PersonRef);
                await RefreshSpeakerListAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"音声登録を解除できませんでした: {ex.Message}",
                    "話者管理エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnAudioRuntimeStateChanged(object? sender, OtomeKairoAudioRuntimeState state)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                var enrollment = state.Enrollment;
                if (enrollment != null &&
                    string.Equals(enrollment.OwnerClientId, _ownerClientId, StringComparison.Ordinal))
                {
                    _activeEnrollment = enrollment;
                    ApplyEnrollmentState(enrollment);
                    return;
                }

                if (_activeEnrollment != null)
                {
                    _activeEnrollment = null;
                    ApplyEnrollmentState(null);
                    await RefreshSpeakerListAsync();
                }
            });
        }

        private void ApplyEnrollmentState(OtomeKairoSpeakerEnrollment? enrollment)
        {
            var isActive = enrollment != null;
            StartEnrollmentButton.IsEnabled = !isActive;
            CancelEnrollmentButton.IsEnabled = isActive;
            NewSpeakerNameBox.IsEnabled = !isActive;
            EnrollmentStatusText.Text = enrollment == null
                ? "物理マイクへ2秒以上の発話を3回入力すると登録が完了します。"
                : $"登録中: {enrollment.CompletedSamples}/{enrollment.RequiredSamples} 発話";
        }

        private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _currentThreshold = (float)e.NewValue;
            UpdateThresholdText();
            ThresholdChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateThresholdText()
        {
            if (ThresholdValueText != null)
            {
                ThresholdValueText.Text = $"現在値: {_currentThreshold:F2}";
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_communicationService != null)
            {
                _communicationService.AudioRuntimeStateChanged -= OnAudioRuntimeStateChanged;
            }
        }
    }
}
