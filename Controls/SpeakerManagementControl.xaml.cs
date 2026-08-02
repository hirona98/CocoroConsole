using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public IReadOnlyList<OtomeKairoConversationDisplayNameDefinition> ConversationDisplayNames { get; private set; }
            = Array.Empty<OtomeKairoConversationDisplayNameDefinition>();

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
            float threshold,
            IReadOnlyList<OtomeKairoConversationDisplayNameDefinition> conversationDisplayNames)
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
            SetConversationDisplayNames(conversationDisplayNames);
            SetThreshold(threshold);
            await RefreshSpeakerListAsync();
        }

        public void SetConversationDisplayNames(
            IReadOnlyList<OtomeKairoConversationDisplayNameDefinition> conversationDisplayNames)
        {
            ConversationDisplayNames = conversationDisplayNames;
            var speakers = SpeakersListBox.ItemsSource is IEnumerable<OtomeKairoAudioSpeaker> current
                ? current.ToList()
                : new List<OtomeKairoAudioSpeaker>();
            ApplyConversationDisplayNameChoices(speakers);
            SpeakersListBox.ItemsSource = null;
            SpeakersListBox.ItemsSource = speakers;
            UpdateNewSpeakerChoices(speakers);
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
            ApplyConversationDisplayNameChoices(response.Speakers);
            SpeakersListBox.ItemsSource = response.Speakers;
            SpeakersListBox.IsEnabled = true;
            UpdateNewSpeakerChoices(response.Speakers);
        }

        private void UpdateNewSpeakerChoices(
            IReadOnlyList<OtomeKairoAudioSpeaker> speakers)
        {
            // 呼ばれ方の更新時にも選択肢と登録操作の可否を同じ状態へ揃えます。
            var assignedIds = speakers
                .Select(speaker => speaker.ConversationDisplayNameId)
                .ToHashSet(StringComparer.Ordinal);
            NewSpeakerDisplayNameComboBox.ItemsSource = ConversationDisplayNames
                .Where(definition => !assignedIds.Contains(definition.ConversationDisplayNameId))
                .ToList();
            StartEnrollmentButton.IsEnabled = _activeEnrollment == null
                && NewSpeakerDisplayNameComboBox.Items.Count > 0;
            NewSpeakerDisplayNameComboBox.IsEnabled = _activeEnrollment == null;
        }

        private void ApplyConversationDisplayNameChoices(
            IReadOnlyList<OtomeKairoAudioSpeaker> speakers)
        {
            var assignedIds = speakers
                .Select(speaker => speaker.ConversationDisplayNameId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var speaker in speakers)
            {
                speaker.DisplayName = ConversationDisplayNames.Single(definition =>
                    string.Equals(
                        definition.ConversationDisplayNameId,
                        speaker.ConversationDisplayNameId,
                        StringComparison.Ordinal)).DisplayName;
                speaker.AvailableConversationDisplayNames = ConversationDisplayNames
                    .Where(definition =>
                        string.Equals(
                            definition.ConversationDisplayNameId,
                            speaker.ConversationDisplayNameId,
                            StringComparison.Ordinal)
                        || !assignedIds.Contains(definition.ConversationDisplayNameId))
                    .ToList();
            }
        }

        private async void StartEnrollment_Click(object sender, RoutedEventArgs e)
        {
            if (NewSpeakerDisplayNameComboBox.SelectedValue is not string conversationDisplayNameId)
            {
                MessageBox.Show(
                    "未割当の呼ばれ方を選択してください。",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            await StartEnrollmentAsync(new OtomeKairoSpeakerEnrollmentRequest
            {
                OwnerClientId = _ownerClientId,
                ConversationDisplayNameId = conversationDisplayNameId,
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

        private async void AssignSpeakerDisplayName_Click(object sender, RoutedEventArgs e)
        {
            if (_apiClient == null ||
                sender is not Button button ||
                button.Tag is not OtomeKairoAudioSpeaker speaker)
            {
                return;
            }

            if (button.CommandParameter is not string conversationDisplayNameId)
            {
                MessageBox.Show(
                    "割り当てる呼ばれ方を選択してください。",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                await _apiClient.AssignAudioSpeakerConversationDisplayNameAsync(
                    speaker.PersonRef,
                    conversationDisplayNameId);
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
            StartEnrollmentButton.IsEnabled = !isActive
                && NewSpeakerDisplayNameComboBox.Items.Count > 0;
            CancelEnrollmentButton.IsEnabled = isActive;
            NewSpeakerDisplayNameComboBox.IsEnabled = !isActive;
            EnrollmentStatusText.Text = enrollment == null
                ? "選択中のマイクへ2秒以上の発話を3回入力すると登録が完了します。"
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
