using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using CocoroConsole.Utilities;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CocoroConsole.Controls
{
    /// <summary>
    /// アバターの表示設定（プリセット選択と VRM）と音声合成設定を編集する。
    /// </summary>
    public partial class AvatarManagementControl : UserControl
    {
        public event EventHandler? SettingsChanged;
        public event EventHandler? AvatarChanged;

        private int _currentAvatarIndex = -1;
        private bool _isInitialized = false;
        private bool _isUpdatingUi = false;
        private DispatcherTimer? _avatarNameChangeTimer;
        private const int CHARACTER_NAME_DEBOUNCE_DELAY_MS = 200;

        public AvatarManagementControl()
        {
            InitializeComponent();

            _avatarNameChangeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CHARACTER_NAME_DEBOUNCE_DELAY_MS)
            };
            _avatarNameChangeTimer.Tick += AvatarNameChangeTimer_Tick;
        }

        public void Initialize()
        {
            LoadAvatarList();

            if (AvatarSelectComboBox.SelectedIndex >= 0)
            {
                _currentAvatarIndex = AvatarSelectComboBox.SelectedIndex;
                UpdateAvatarUI();
            }

            _isInitialized = true;
        }

        private List<OtomeKairoAvatarSpeechDefinition> Avatars =>
            AppSettings.Instance.AvatarSpeech.Avatars;

        private OtomeKairoConsoleAvatarPresentation? FindPresentation(string avatarId)
        {
            return AppSettings.Instance.ConsoleClient.AvatarPresentations
                .FirstOrDefault(presentation => presentation.AvatarId == avatarId);
        }

        private void LoadAvatarList()
        {
            var avatars = Avatars;
            AvatarSelectComboBox.ItemsSource = avatars;
            var selectedIndex = avatars.FindIndex(avatar =>
                avatar.AvatarId == AppSettings.Instance.AvatarSpeech.SelectedAvatarId);
            if (selectedIndex >= 0)
            {
                AvatarSelectComboBox.SelectedIndex = selectedIndex;
            }
        }

        /// <summary>
        /// UI 上の VRM / 表示 / TTS 関連を AppSettings へ同期する。
        /// STT など UI に無い項目は既存値を保持する。
        /// </summary>
        public void SyncCurrentAvatarFromUi()
        {
            if (_currentAvatarIndex < 0 || _currentAvatarIndex >= Avatars.Count)
            {
                return;
            }

            var avatar = Avatars[_currentAvatarIndex];
            avatar.DisplayName = AvatarNameTextBox.Text;
            var tts = avatar.Tts;
            tts.Enabled = IsUseTTSCheckBox.IsChecked ?? false;
            tts.Engine = TTSEngineComboBox.SelectedItem is ComboBoxItem selectedTtsEngine
                ? selectedTtsEngine.Tag?.ToString() ?? "voicevox"
                : "voicevox";

            var voicevox = tts.VoicevoxConfig;
            voicevox.EndpointUrl = VoicevoxEndpointUrlTextBox.Text;
            voicevox.SecondaryEndpointUrl = VoicevoxSecondaryEndpointUrlTextBox.Text;
            if (int.TryParse(VoicevoxSpeakerIdTextBox.Text, out int voicevoxSpeakerId))
            {
                voicevox.SpeakerId = voicevoxSpeakerId;
            }

            voicevox.SpeedScale = (float)VoicevoxSpeedScaleSlider.Value;
            voicevox.PitchScale = (float)VoicevoxPitchScaleSlider.Value;
            voicevox.IntonationScale = (float)VoicevoxIntonationScaleSlider.Value;
            voicevox.VolumeScale = (float)VoicevoxVolumeScaleSlider.Value;
            voicevox.PrePhonemeLength = (float)VoicevoxPrePhonemeLengthSlider.Value;
            voicevox.PostPhonemeLength = (float)VoicevoxPostPhonemeLengthSlider.Value;

            if (VoicevoxOutputSamplingRateComboBox.SelectedItem is ComboBoxItem selectedSampleRate &&
                int.TryParse(selectedSampleRate.Tag?.ToString(), out int samplingRate))
            {
                voicevox.OutputSamplingRate = samplingRate;
            }

            voicevox.OutputStereo = VoicevoxOutputStereoCheckBox.IsChecked ?? false;

            var styleBert = tts.StyleBertVits2Config;
            styleBert.EndpointUrl = SBV2EndpointUrlTextBox.Text;
            styleBert.ModelName = SBV2ModelNameTextBox.Text;
            if (int.TryParse(SBV2ModelIdTextBox.Text, out int modelId))
            {
                styleBert.ModelId = modelId;
            }

            styleBert.SpeakerName = SBV2SpeakerNameTextBox.Text;
            if (int.TryParse(SBV2SpeakerIdTextBox.Text, out int speakerId))
            {
                styleBert.SpeakerId = speakerId;
            }

            styleBert.Style = SBV2StyleTextBox.Text;
            if (TryParseInvariantFloat(SBV2StyleWeightTextBox.Text, out float styleWeight))
            {
                styleBert.StyleWeight = styleWeight;
            }

            styleBert.Language = SBV2LanguageTextBox.Text;
            if (TryParseInvariantFloat(SBV2SdpRatioTextBox.Text, out float sdpRatio))
            {
                styleBert.SdpRatio = sdpRatio;
            }

            if (TryParseInvariantFloat(SBV2NoiseTextBox.Text, out float noise))
            {
                styleBert.Noise = noise;
            }

            if (TryParseInvariantFloat(SBV2NoiseWTextBox.Text, out float noiseW))
            {
                styleBert.NoiseW = noiseW;
            }

            if (TryParseInvariantFloat(SBV2LengthTextBox.Text, out float length))
            {
                styleBert.Length = length;
            }

            styleBert.AutoSplit = SBV2AutoSplitCheckBox.IsChecked ?? true;
            if (TryParseInvariantFloat(SBV2SplitIntervalTextBox.Text, out float splitInterval))
            {
                styleBert.SplitInterval = splitInterval;
            }

            var aivis = tts.AivisCloudConfig;
            aivis.ApiKey = AivisCloudApiKeyPasswordBox.Text;
            aivis.ModelUuid = AivisCloudModelUuidTextBox.Text;
            aivis.SpeakerUuid = AivisCloudSpeakerUuidTextBox.Text;
            if (int.TryParse(AivisCloudStyleIdTextBox.Text, out int styleId))
            {
                aivis.StyleId = styleId;
            }

            if (TryParseInvariantFloat(AivisCloudSpeakingRateTextBox.Text, out float speakingRate))
            {
                aivis.SpeakingRate = speakingRate;
            }

            if (TryParseInvariantFloat(AivisCloudEmotionalIntensityTextBox.Text, out float emotionalIntensity))
            {
                aivis.EmotionalIntensity = emotionalIntensity;
            }

            if (TryParseInvariantFloat(AivisCloudTempoDynamicsTextBox.Text, out float tempoDynamics))
            {
                aivis.TempoDynamics = tempoDynamics;
            }

            if (TryParseInvariantFloat(AivisCloudVolumeTextBox.Text, out float volume))
            {
                aivis.Volume = volume;
            }

            SyncPresentationFromUi(avatar.AvatarId);
            AppSettings.Instance.AvatarSpeech.SelectedAvatarId = avatar.AvatarId;
        }

        private void SyncPresentationFromUi(string avatarId)
        {
            var presentations = AppSettings.Instance.ConsoleClient.AvatarPresentations;
            var presentation = presentations.FirstOrDefault(item => item.AvatarId == avatarId);
            if (AppSettings.IsBuiltInAvatar(presentation))
            {
                presentation!.ConvertUnlitToMtoon = ConvertMToonCheckBox.IsChecked ?? false;
                presentation.ShadowExclusionEnabled = EnableShadowOffCheckBox.IsChecked ?? false;
                presentation.ShadowExcludedMeshNames = ParseShadowMeshes(ShadowOffMeshTextBox.Text);
                return;
            }

            var model = VRMFilePathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(model))
            {
                if (presentation != null)
                {
                    presentations.Remove(presentation);
                }
                return;
            }

            if (presentation == null)
            {
                presentation = new OtomeKairoConsoleAvatarPresentation
                {
                    AvatarId = avatarId,
                };
                presentations.Add(presentation);
            }

            presentation.Model = model;
            presentation.ConvertUnlitToMtoon = ConvertMToonCheckBox.IsChecked ?? false;
            presentation.ShadowExclusionEnabled = EnableShadowOffCheckBox.IsChecked ?? false;
            presentation.ShadowExcludedMeshNames = ParseShadowMeshes(ShadowOffMeshTextBox.Text);
        }

        private static List<string> ParseShadowMeshes(string text)
        {
            return text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.Trim())
                .Where(name => name.Length > 0)
                .ToList();
        }

        private static bool TryParseInvariantFloat(string value, out float parsed)
        {
            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out parsed);
        }

        private static string FormatInvariantFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        public int GetCurrentAvatarIndex()
        {
            return _currentAvatarIndex;
        }

        private void AvatarSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || AvatarSelectComboBox.SelectedIndex < 0)
            {
                return;
            }

            SyncCurrentAvatarFromUi();
            _currentAvatarIndex = AvatarSelectComboBox.SelectedIndex;
            UpdateAvatarUI();
            AvatarChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateAvatarUI()
        {
            if (_currentAvatarIndex < 0 || _currentAvatarIndex >= Avatars.Count)
            {
                return;
            }

            _isUpdatingUi = true;
            try
            {
                var avatar = Avatars[_currentAvatarIndex];
                var presentation = FindPresentation(avatar.AvatarId);
                var isReadOnly = AppSettings.IsBuiltInAvatar(presentation);
                var tts = avatar.Tts;
                var voicevox = tts.VoicevoxConfig;
                var styleBert = tts.StyleBertVits2Config;
                var aivis = tts.AivisCloudConfig;
                AvatarNameTextBox.Text = avatar.DisplayName;
                VRMFilePathTextBox.Text = presentation?.Model ?? string.Empty;
                ConvertMToonCheckBox.IsChecked = presentation?.ConvertUnlitToMtoon ?? false;
                EnableShadowOffCheckBox.IsChecked = presentation?.ShadowExclusionEnabled ?? false;
                ShadowOffMeshTextBox.Text = presentation == null
                    ? string.Empty
                    : string.Join(",", presentation.ShadowExcludedMeshNames);
                ShadowOffMeshTextBox.IsEnabled = presentation?.ShadowExclusionEnabled ?? false;

                IsUseTTSCheckBox.IsChecked = tts.Enabled;

                VoicevoxEndpointUrlTextBox.Text = voicevox.EndpointUrl;
                VoicevoxSecondaryEndpointUrlTextBox.Text = voicevox.SecondaryEndpointUrl;
                VoicevoxSpeakerIdTextBox.Text = voicevox.SpeakerId.ToString();
                VoicevoxSpeedScaleSlider.Value = voicevox.SpeedScale;
                VoicevoxPitchScaleSlider.Value = voicevox.PitchScale;
                VoicevoxIntonationScaleSlider.Value = voicevox.IntonationScale;
                VoicevoxVolumeScaleSlider.Value = voicevox.VolumeScale;
                VoicevoxPrePhonemeLengthSlider.Value = voicevox.PrePhonemeLength;
                VoicevoxPostPhonemeLengthSlider.Value = voicevox.PostPhonemeLength;
                VoicevoxOutputStereoCheckBox.IsChecked = voicevox.OutputStereo;

                foreach (ComboBoxItem item in VoicevoxOutputSamplingRateComboBox.Items)
                {
                    if (item.Tag?.ToString() == voicevox.OutputSamplingRate.ToString())
                    {
                        VoicevoxOutputSamplingRateComboBox.SelectedItem = item;
                        break;
                    }
                }

                foreach (ComboBoxItem item in TTSEngineComboBox.Items)
                {
                    if (item.Tag?.ToString() == tts.Engine)
                    {
                        TTSEngineComboBox.SelectedItem = item;
                        break;
                    }
                }

                SBV2EndpointUrlTextBox.Text = styleBert.EndpointUrl;
                SBV2ModelNameTextBox.Text = styleBert.ModelName;
                SBV2ModelIdTextBox.Text = styleBert.ModelId.ToString();
                SBV2SpeakerNameTextBox.Text = styleBert.SpeakerName;
                SBV2SpeakerIdTextBox.Text = styleBert.SpeakerId.ToString();
                SBV2StyleTextBox.Text = styleBert.Style;
                SBV2StyleWeightTextBox.Text = FormatInvariantFloat(styleBert.StyleWeight);
                SBV2LanguageTextBox.Text = styleBert.Language;
                SBV2SdpRatioTextBox.Text = FormatInvariantFloat(styleBert.SdpRatio);
                SBV2NoiseTextBox.Text = FormatInvariantFloat(styleBert.Noise);
                SBV2NoiseWTextBox.Text = FormatInvariantFloat(styleBert.NoiseW);
                SBV2LengthTextBox.Text = FormatInvariantFloat(styleBert.Length);
                SBV2AutoSplitCheckBox.IsChecked = styleBert.AutoSplit;
                SBV2SplitIntervalTextBox.Text = FormatInvariantFloat(styleBert.SplitInterval);

                AivisCloudApiKeyPasswordBox.Text = aivis.ApiKey;
                AivisCloudModelUuidTextBox.Text = aivis.ModelUuid;
                AivisCloudSpeakerUuidTextBox.Text = aivis.SpeakerUuid;
                AivisCloudStyleIdTextBox.Text = aivis.StyleId.ToString();
                AivisCloudSpeakingRateTextBox.Text = FormatInvariantFloat(aivis.SpeakingRate);
                AivisCloudEmotionalIntensityTextBox.Text = FormatInvariantFloat(aivis.EmotionalIntensity);
                AivisCloudTempoDynamicsTextBox.Text = FormatInvariantFloat(aivis.TempoDynamics);
                AivisCloudVolumeTextBox.Text = FormatInvariantFloat(aivis.Volume);

                UpdateTTSPanelVisibility(tts.Engine);

                DeleteAvatarButton.IsEnabled = !isReadOnly;
                VRMFilePathTextBox.IsEnabled = !isReadOnly;
                BrowseVrmFileButton.IsEnabled = !isReadOnly;
            }
            finally
            {
                _isUpdatingUi = false;
            }
        }

        private void AddAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SyncCurrentAvatarFromUi();

                var newName = "新規アバター";
                int avatarNumber = 1;
                while (Avatars.Any(avatar => avatar.DisplayName == newName))
                {
                    newName = $"新規アバター{avatarNumber}";
                    avatarNumber++;
                }

                var newAvatar = AppSettings.Instance.CreateAvatarFromDefaults(newName);
                Avatars.Add(newAvatar);

                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = Avatars;
                AvatarSelectComboBox.SelectedIndex = Avatars.Count - 1;
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アバター追加エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentAvatarIndex < 0 || _currentAvatarIndex >= Avatars.Count)
                {
                    return;
                }

                SyncCurrentAvatarFromUi();
                var avatar = Avatars[_currentAvatarIndex];
                var presentation = FindPresentation(avatar.AvatarId);
                if (AppSettings.IsBuiltInAvatar(presentation))
                {
                    return;
                }

                if (presentation != null)
                {
                    AppSettings.Instance.ConsoleClient.AvatarPresentations.Remove(presentation);
                }
                Avatars.RemoveAt(_currentAvatarIndex);
                _currentAvatarIndex = -1;

                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = Avatars;
                if (Avatars.Count > 0)
                {
                    AvatarSelectComboBox.SelectedIndex = 0;
                }

                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アバター削除エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DuplicateAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentAvatarIndex < 0 || _currentAvatarIndex >= Avatars.Count)
                {
                    return;
                }

                SyncCurrentAvatarFromUi();
                var sourceAvatar = Avatars[_currentAvatarIndex];

                var newName = sourceAvatar.DisplayName + "_copy";
                int copyNumber = 1;
                while (Avatars.Any(avatar => avatar.DisplayName == newName))
                {
                    newName = $"{sourceAvatar.DisplayName}_copy{copyNumber}";
                    copyNumber++;
                }

                var newAvatar = AppSettings.CloneDocument(sourceAvatar);
                newAvatar.AvatarId = $"avatar:{Guid.NewGuid():N}";
                newAvatar.DisplayName = newName;
                var sourcePresentation = FindPresentation(sourceAvatar.AvatarId);
                if (sourcePresentation != null && !AppSettings.IsBuiltInAvatar(sourcePresentation))
                {
                    var presentation = AppSettings.CloneDocument(sourcePresentation);
                    presentation.AvatarId = newAvatar.AvatarId;
                    AppSettings.Instance.ConsoleClient.AvatarPresentations.Add(presentation);
                }

                Avatars.Add(newAvatar);

                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = Avatars;
                AvatarSelectComboBox.SelectedIndex = Avatars.Count - 1;
                SettingsChanged?.Invoke(this, EventArgs.Empty);

                Debug.WriteLine($"アバター複製: {sourceAvatar.DisplayName} -> {newName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アバター複製エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseVrmFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "VRM Files (*.vrm)|*.vrm|All Files (*.*)|*.*",
                    Title = "VRMファイルを選択してください"
                };

                if (dialog.ShowDialog() == true)
                {
                    VRMFilePathTextBox.Text = dialog.FileName;
                    if (string.IsNullOrWhiteSpace(AvatarNameTextBox.Text))
                    {
                        AvatarNameTextBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                    }

                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイル選択エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnableShadowOffCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (ShadowOffMeshTextBox != null)
            {
                ShadowOffMeshTextBox.IsEnabled = true;
            }

            OnAvatarPresentationChanged(sender, e);
        }

        private void EnableShadowOffCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (ShadowOffMeshTextBox != null)
            {
                ShadowOffMeshTextBox.IsEnabled = false;
            }

            OnAvatarPresentationChanged(sender, e);
        }

        private void OnAvatarPresentationChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnTtsSettingChanged(object sender, RoutedEventArgs e)
        {
            NotifyTtsSettingsChanged();
        }

        private void OnTtsTextChanged(object sender, TextChangedEventArgs e)
        {
            NotifyTtsSettingsChanged();
        }

        private void OnTtsSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            NotifyTtsSettingsChanged();
        }

        private void OnTtsComboChanged(object sender, SelectionChangedEventArgs e)
        {
            NotifyTtsSettingsChanged();
        }

        private void NotifyTtsSettingsChanged()
        {
            if (_isInitialized && !_isUpdatingUi)
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void AivisCloudApiKeyPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(AivisCloudApiKeyPasswordBox);
            NotifyTtsSettingsChanged();
        }

        private void AivisCloudApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(AivisCloudApiKeyPasswordBox);
        }

        private void TTSEngineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || _isUpdatingUi || TTSEngineComboBox.SelectedItem == null)
            {
                return;
            }

            var selectedItem = (ComboBoxItem)TTSEngineComboBox.SelectedItem;
            var engineType = selectedItem.Tag?.ToString();
            UpdateTTSPanelVisibility(engineType);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateTTSPanelVisibility(string? engineType)
        {
            if (VoicevoxSettingsPanel == null ||
                StyleBertVits2BasicPanel == null ||
                StyleBertVits2SettingsPanel == null ||
                AivisCloudSettingsPanel == null)
            {
                return;
            }

            switch (engineType)
            {
                case "style-bert-vits2":
                    VoicevoxSettingsPanel.Visibility = Visibility.Collapsed;
                    StyleBertVits2BasicPanel.Visibility = Visibility.Visible;
                    StyleBertVits2SettingsPanel.Visibility = Visibility.Visible;
                    AivisCloudSettingsPanel.Visibility = Visibility.Collapsed;
                    break;
                case "aivis-cloud":
                    VoicevoxSettingsPanel.Visibility = Visibility.Collapsed;
                    StyleBertVits2BasicPanel.Visibility = Visibility.Collapsed;
                    StyleBertVits2SettingsPanel.Visibility = Visibility.Collapsed;
                    AivisCloudSettingsPanel.Visibility = Visibility.Visible;
                    break;
                case "voicevox":
                default:
                    VoicevoxSettingsPanel.Visibility = Visibility.Visible;
                    StyleBertVits2BasicPanel.Visibility = Visibility.Collapsed;
                    StyleBertVits2SettingsPanel.Visibility = Visibility.Collapsed;
                    AivisCloudSettingsPanel.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void AvatarNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _currentAvatarIndex < 0)
            {
                return;
            }

            if (_avatarNameChangeTimer != null)
            {
                _avatarNameChangeTimer.Stop();
                _avatarNameChangeTimer.Start();
            }
        }

        private void AvatarNameChangeTimer_Tick(object? sender, EventArgs e)
        {
            if (_avatarNameChangeTimer != null)
            {
                _avatarNameChangeTimer.Stop();
            }

            if (!_isInitialized || _currentAvatarIndex < 0 || _currentAvatarIndex >= Avatars.Count)
            {
                return;
            }

            var newName = AvatarNameTextBox.Text;
            if (string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            var currentSelectedIndex = _currentAvatarIndex;
            Avatars[_currentAvatarIndex].DisplayName = newName;

            AvatarSelectComboBox.SelectionChanged -= AvatarSelectComboBox_SelectionChanged;
            AvatarSelectComboBox.ItemsSource = null;
            AvatarSelectComboBox.ItemsSource = Avatars;
            AvatarSelectComboBox.SelectedIndex = currentSelectedIndex;
            AvatarSelectComboBox.SelectionChanged += AvatarSelectComboBox_SelectionChanged;

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RefreshAvatarList()
        {
            AvatarSelectComboBox.SelectionChanged -= AvatarSelectComboBox_SelectionChanged;
            try
            {
                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = Avatars;

                if (Avatars.Count > 0)
                {
                    int indexToSelect = Math.Min(_currentAvatarIndex, Avatars.Count - 1);
                    if (indexToSelect < 0)
                    {
                        indexToSelect = 0;
                    }

                    _currentAvatarIndex = indexToSelect;
                    AvatarSelectComboBox.SelectedIndex = indexToSelect;
                    UpdateAvatarUI();
                }
                else
                {
                    _currentAvatarIndex = -1;
                    AvatarNameTextBox.Text = string.Empty;
                    VRMFilePathTextBox.Text = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アバターリスト復元エラー: {ex.Message}");
            }
            finally
            {
                AvatarSelectComboBox.SelectionChanged += AvatarSelectComboBox_SelectionChanged;
            }
        }
    }
}
