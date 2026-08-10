using CocoroConsole.Communication;
using CocoroConsole.Services;
using CocoroConsole.Utilities;
using Microsoft.Win32;
using System;
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

        private void LoadAvatarList()
        {
            var appSettings = AppSettings.Instance;
            AvatarSelectComboBox.ItemsSource = appSettings.AvatarList;

            if (appSettings.AvatarList.Count > 0 &&
                appSettings.CurrentAvatarIndex >= 0 &&
                appSettings.CurrentAvatarIndex < appSettings.AvatarList.Count)
            {
                AvatarSelectComboBox.SelectedIndex = appSettings.CurrentAvatarIndex;
            }
        }

        /// <summary>
        /// UI 上の VRM / 表示 / TTS 関連を AppSettings へ同期する。
        /// STT など UI に無い項目は既存値を保持する。
        /// </summary>
        public void SyncCurrentAvatarFromUi()
        {
            if (_currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
            {
                return;
            }

            var avatar = AppSettings.Instance.AvatarList[_currentAvatarIndex].DeepCopy();
            avatar.modelName = AvatarNameTextBox.Text;
            avatar.vrmFilePath = VRMFilePathTextBox.Text;
            avatar.isConvertMToon = ConvertMToonCheckBox.IsChecked ?? false;
            avatar.isEnableShadowOff = EnableShadowOffCheckBox.IsChecked ?? false;
            avatar.shadowOffMesh = ShadowOffMeshTextBox.Text;

            avatar.isUseTTS = IsUseTTSCheckBox.IsChecked ?? false;
            avatar.ttsType = TTSEngineComboBox.SelectedItem is ComboBoxItem selectedTtsEngine
                ? selectedTtsEngine.Tag?.ToString() ?? "voicevox"
                : "voicevox";

            avatar.voicevoxConfig.endpointUrl = VoicevoxEndpointUrlTextBox.Text;
            avatar.voicevoxConfig.secondaryEndpointUrl = VoicevoxSecondaryEndpointUrlTextBox.Text;
            if (int.TryParse(VoicevoxSpeakerIdTextBox.Text, out int voicevoxSpeakerId))
            {
                avatar.voicevoxConfig.speakerId = voicevoxSpeakerId;
            }

            avatar.voicevoxConfig.speedScale = (float)VoicevoxSpeedScaleSlider.Value;
            avatar.voicevoxConfig.pitchScale = (float)VoicevoxPitchScaleSlider.Value;
            avatar.voicevoxConfig.intonationScale = (float)VoicevoxIntonationScaleSlider.Value;
            avatar.voicevoxConfig.volumeScale = (float)VoicevoxVolumeScaleSlider.Value;
            avatar.voicevoxConfig.prePhonemeLength = (float)VoicevoxPrePhonemeLengthSlider.Value;
            avatar.voicevoxConfig.postPhonemeLength = (float)VoicevoxPostPhonemeLengthSlider.Value;

            if (VoicevoxOutputSamplingRateComboBox.SelectedItem is ComboBoxItem selectedSampleRate &&
                int.TryParse(selectedSampleRate.Tag?.ToString(), out int samplingRate))
            {
                avatar.voicevoxConfig.outputSamplingRate = samplingRate;
            }

            avatar.voicevoxConfig.outputStereo = VoicevoxOutputStereoCheckBox.IsChecked ?? false;

            avatar.styleBertVits2Config.endpointUrl = SBV2EndpointUrlTextBox.Text;
            avatar.styleBertVits2Config.modelName = SBV2ModelNameTextBox.Text;
            if (int.TryParse(SBV2ModelIdTextBox.Text, out int modelId))
            {
                avatar.styleBertVits2Config.modelId = modelId;
            }

            avatar.styleBertVits2Config.speakerName = SBV2SpeakerNameTextBox.Text;
            if (int.TryParse(SBV2SpeakerIdTextBox.Text, out int speakerId))
            {
                avatar.styleBertVits2Config.speakerId = speakerId;
            }

            avatar.styleBertVits2Config.style = SBV2StyleTextBox.Text;
            if (TryParseInvariantFloat(SBV2StyleWeightTextBox.Text, out float styleWeight))
            {
                avatar.styleBertVits2Config.styleWeight = styleWeight;
            }

            avatar.styleBertVits2Config.language = SBV2LanguageTextBox.Text;
            if (TryParseInvariantFloat(SBV2SdpRatioTextBox.Text, out float sdpRatio))
            {
                avatar.styleBertVits2Config.sdpRatio = sdpRatio;
            }

            if (TryParseInvariantFloat(SBV2NoiseTextBox.Text, out float noise))
            {
                avatar.styleBertVits2Config.noise = noise;
            }

            if (TryParseInvariantFloat(SBV2NoiseWTextBox.Text, out float noiseW))
            {
                avatar.styleBertVits2Config.noiseW = noiseW;
            }

            if (TryParseInvariantFloat(SBV2LengthTextBox.Text, out float length))
            {
                avatar.styleBertVits2Config.length = length;
            }

            avatar.styleBertVits2Config.autoSplit = SBV2AutoSplitCheckBox.IsChecked ?? true;
            if (TryParseInvariantFloat(SBV2SplitIntervalTextBox.Text, out float splitInterval))
            {
                avatar.styleBertVits2Config.splitInterval = splitInterval;
            }

            avatar.aivisCloudConfig.apiKey = AivisCloudApiKeyPasswordBox.Text;
            avatar.aivisCloudConfig.modelUuid = AivisCloudModelUuidTextBox.Text;
            avatar.aivisCloudConfig.speakerUuid = AivisCloudSpeakerUuidTextBox.Text;
            if (int.TryParse(AivisCloudStyleIdTextBox.Text, out int styleId))
            {
                avatar.aivisCloudConfig.styleId = styleId;
            }

            if (TryParseInvariantFloat(AivisCloudSpeakingRateTextBox.Text, out float speakingRate))
            {
                avatar.aivisCloudConfig.speakingRate = speakingRate;
            }

            if (TryParseInvariantFloat(AivisCloudEmotionalIntensityTextBox.Text, out float emotionalIntensity))
            {
                avatar.aivisCloudConfig.emotionalIntensity = emotionalIntensity;
            }

            if (TryParseInvariantFloat(AivisCloudTempoDynamicsTextBox.Text, out float tempoDynamics))
            {
                avatar.aivisCloudConfig.tempoDynamics = tempoDynamics;
            }

            if (TryParseInvariantFloat(AivisCloudVolumeTextBox.Text, out float volume))
            {
                avatar.aivisCloudConfig.volume = volume;
            }

            AppSettings.Instance.AvatarList[_currentAvatarIndex] = avatar;
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
            if (_currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
            {
                return;
            }

            _isUpdatingUi = true;
            try
            {
                var avatar = AppSettings.Instance.AvatarList[_currentAvatarIndex];
                AvatarNameTextBox.Text = avatar.modelName;
                VRMFilePathTextBox.Text = avatar.vrmFilePath;
                ConvertMToonCheckBox.IsChecked = avatar.isConvertMToon;
                EnableShadowOffCheckBox.IsChecked = avatar.isEnableShadowOff;
                ShadowOffMeshTextBox.Text = avatar.shadowOffMesh;
                ShadowOffMeshTextBox.IsEnabled = avatar.isEnableShadowOff;

                IsUseTTSCheckBox.IsChecked = avatar.isUseTTS;

                VoicevoxEndpointUrlTextBox.Text = avatar.voicevoxConfig.endpointUrl;
                VoicevoxSecondaryEndpointUrlTextBox.Text = avatar.voicevoxConfig.secondaryEndpointUrl;
                VoicevoxSpeakerIdTextBox.Text = avatar.voicevoxConfig.speakerId.ToString();
                VoicevoxSpeedScaleSlider.Value = avatar.voicevoxConfig.speedScale;
                VoicevoxPitchScaleSlider.Value = avatar.voicevoxConfig.pitchScale;
                VoicevoxIntonationScaleSlider.Value = avatar.voicevoxConfig.intonationScale;
                VoicevoxVolumeScaleSlider.Value = avatar.voicevoxConfig.volumeScale;
                VoicevoxPrePhonemeLengthSlider.Value = avatar.voicevoxConfig.prePhonemeLength;
                VoicevoxPostPhonemeLengthSlider.Value = avatar.voicevoxConfig.postPhonemeLength;
                VoicevoxOutputStereoCheckBox.IsChecked = avatar.voicevoxConfig.outputStereo;

                foreach (ComboBoxItem item in VoicevoxOutputSamplingRateComboBox.Items)
                {
                    if (item.Tag?.ToString() == avatar.voicevoxConfig.outputSamplingRate.ToString())
                    {
                        VoicevoxOutputSamplingRateComboBox.SelectedItem = item;
                        break;
                    }
                }

                foreach (ComboBoxItem item in TTSEngineComboBox.Items)
                {
                    if (item.Tag?.ToString() == avatar.ttsType)
                    {
                        TTSEngineComboBox.SelectedItem = item;
                        break;
                    }
                }

                SBV2EndpointUrlTextBox.Text = avatar.styleBertVits2Config.endpointUrl;
                SBV2ModelNameTextBox.Text = avatar.styleBertVits2Config.modelName;
                SBV2ModelIdTextBox.Text = avatar.styleBertVits2Config.modelId.ToString();
                SBV2SpeakerNameTextBox.Text = avatar.styleBertVits2Config.speakerName;
                SBV2SpeakerIdTextBox.Text = avatar.styleBertVits2Config.speakerId.ToString();
                SBV2StyleTextBox.Text = avatar.styleBertVits2Config.style;
                SBV2StyleWeightTextBox.Text = FormatInvariantFloat(avatar.styleBertVits2Config.styleWeight);
                SBV2LanguageTextBox.Text = avatar.styleBertVits2Config.language;
                SBV2SdpRatioTextBox.Text = FormatInvariantFloat(avatar.styleBertVits2Config.sdpRatio);
                SBV2NoiseTextBox.Text = FormatInvariantFloat(avatar.styleBertVits2Config.noise);
                SBV2NoiseWTextBox.Text = FormatInvariantFloat(avatar.styleBertVits2Config.noiseW);
                SBV2LengthTextBox.Text = FormatInvariantFloat(avatar.styleBertVits2Config.length);
                SBV2AutoSplitCheckBox.IsChecked = avatar.styleBertVits2Config.autoSplit;
                SBV2SplitIntervalTextBox.Text = FormatInvariantFloat(avatar.styleBertVits2Config.splitInterval);

                AivisCloudApiKeyPasswordBox.Text = avatar.aivisCloudConfig.apiKey;
                AivisCloudModelUuidTextBox.Text = avatar.aivisCloudConfig.modelUuid;
                AivisCloudSpeakerUuidTextBox.Text = avatar.aivisCloudConfig.speakerUuid;
                AivisCloudStyleIdTextBox.Text = avatar.aivisCloudConfig.styleId.ToString();
                AivisCloudSpeakingRateTextBox.Text = FormatInvariantFloat(avatar.aivisCloudConfig.speakingRate);
                AivisCloudEmotionalIntensityTextBox.Text = FormatInvariantFloat(avatar.aivisCloudConfig.emotionalIntensity);
                AivisCloudTempoDynamicsTextBox.Text = FormatInvariantFloat(avatar.aivisCloudConfig.tempoDynamics);
                AivisCloudVolumeTextBox.Text = FormatInvariantFloat(avatar.aivisCloudConfig.volume);

                UpdateTTSPanelVisibility(avatar.ttsType);

                DeleteAvatarButton.IsEnabled = !avatar.isReadOnly;
                VRMFilePathTextBox.IsEnabled = !avatar.isReadOnly;
                BrowseVrmFileButton.IsEnabled = !avatar.isReadOnly;
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
                while (AppSettings.Instance.AvatarList.Any(c => c.modelName == newName))
                {
                    newName = $"新規アバター{avatarNumber}";
                    avatarNumber++;
                }

                var newAvatar = AppSettings.Instance.CreateAvatarFromDefaults(newName);
                AppSettings.Instance.AvatarList.Add(newAvatar);

                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;
                AvatarSelectComboBox.SelectedIndex = AppSettings.Instance.AvatarList.Count - 1;
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
                if (_currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
                {
                    return;
                }

                SyncCurrentAvatarFromUi();
                var avatar = AppSettings.Instance.AvatarList[_currentAvatarIndex];
                if (avatar.isReadOnly)
                {
                    return;
                }

                AppSettings.Instance.AvatarList.RemoveAt(_currentAvatarIndex);
                _currentAvatarIndex = -1;

                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;
                if (AppSettings.Instance.AvatarList.Count > 0)
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
                if (_currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
                {
                    return;
                }

                SyncCurrentAvatarFromUi();
                var sourceAvatar = AppSettings.Instance.AvatarList[_currentAvatarIndex];

                var newName = sourceAvatar.modelName + "_copy";
                int copyNumber = 1;
                while (AppSettings.Instance.AvatarList.Any(c => c.modelName == newName))
                {
                    newName = $"{sourceAvatar.modelName}_copy{copyNumber}";
                    copyNumber++;
                }

                // 音声設定を含む既存値を複製し、表示用の識別だけ付け替える。
                var newAvatar = sourceAvatar.DeepCopy();
                newAvatar.avatarId = $"avatar:{Guid.NewGuid():N}";
                newAvatar.modelName = newName;
                newAvatar.isReadOnly = false;

                AppSettings.Instance.AvatarList.Add(newAvatar);

                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;
                AvatarSelectComboBox.SelectedIndex = AppSettings.Instance.AvatarList.Count - 1;
                SettingsChanged?.Invoke(this, EventArgs.Empty);

                Debug.WriteLine($"アバター複製: {sourceAvatar.modelName} -> {newName}");
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

            if (!_isInitialized || _currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
            {
                return;
            }

            var newName = AvatarNameTextBox.Text;
            if (string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            var currentSelectedIndex = _currentAvatarIndex;
            AppSettings.Instance.AvatarList[_currentAvatarIndex].modelName = newName;

            AvatarSelectComboBox.SelectionChanged -= AvatarSelectComboBox_SelectionChanged;
            AvatarSelectComboBox.ItemsSource = null;
            AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;
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
                AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;

                if (AppSettings.Instance.AvatarList.Count > 0)
                {
                    int indexToSelect = Math.Min(_currentAvatarIndex, AppSettings.Instance.AvatarList.Count - 1);
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
