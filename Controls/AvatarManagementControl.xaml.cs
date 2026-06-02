using CocoroConsole.Communication;
using CocoroConsole.Services;
using CocoroConsole.Utilities;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CocoroConsole.Controls
{
    /// <summary>
    /// AvatarManagementControl.xaml の相互作用ロジック
    /// </summary>
    public partial class AvatarManagementControl : UserControl
    {
        /// <summary>
        /// 設定が変更されたときに発生するイベント
        /// </summary>
        public event EventHandler? SettingsChanged;

        /// <summary>
        /// アバターが変更されたときに発生するイベント
        /// </summary>
        public event EventHandler? AvatarChanged;

        /// <summary>
        /// 現在選択中のアバターインデックス
        /// </summary>
        private int _currentAvatarIndex = -1;

        /// <summary>
        /// 読み込み完了フラグ
        /// </summary>
        private bool _isInitialized = false;

        /// <summary>
        /// アバター名変更のデバウンス用タイマー
        /// </summary>
        private DispatcherTimer? _avatarNameChangeTimer;

        /// <summary>
        /// デバウンス遅延時間（ミリ秒）
        /// </summary>
        private const int CHARACTER_NAME_DEBOUNCE_DELAY_MS = 200;

        public AvatarManagementControl()
        {
            InitializeComponent();

            // アバター名変更用のデバウンスタイマーを初期化
            _avatarNameChangeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CHARACTER_NAME_DEBOUNCE_DELAY_MS)
            };
            _avatarNameChangeTimer.Tick += AvatarNameChangeTimer_Tick;

        }

        private void AivisCloudApiKeyPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(AivisCloudApiKeyPasswordBox);
        }

        private void AivisCloudApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(AivisCloudApiKeyPasswordBox);
        }

        private void STTApiKeyPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(STTApiKeyPasswordBox);
        }

        private void STTApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(STTApiKeyPasswordBox);
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        public void Initialize()
        {
            LoadAvatarList();

            // 選択されたアバターの設定をUIに反映
            if (AvatarSelectComboBox.SelectedIndex >= 0)
            {
                _currentAvatarIndex = AvatarSelectComboBox.SelectedIndex;
                UpdateAvatarUI();
            }

            _isInitialized = true;
        }

        /// <summary>
        /// アバターリストを読み込み
        /// </summary>
        private void LoadAvatarList()
        {
            var appSettings = AppSettings.Instance;

            // ItemsSourceを使用
            AvatarSelectComboBox.ItemsSource = appSettings.AvatarList;

            if (appSettings.AvatarList.Count > 0 &&
                appSettings.CurrentAvatarIndex >= 0 &&
                appSettings.CurrentAvatarIndex < appSettings.AvatarList.Count)
            {
                AvatarSelectComboBox.SelectedIndex = appSettings.CurrentAvatarIndex;
            }
        }

        /// <summary>
        /// UI上の現在のアバター設定を取得（UIから値を読み取ってディープコピーを返却）
        /// </summary>
        public AvatarSettings? GetCurrentAvatarSettingFromUI()
        {
            if (_currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
                return null;

            // 既存のアバター設定のディープコピーを作成
            var originalAvatar = AppSettings.Instance.AvatarList[_currentAvatarIndex];
            var avatar = originalAvatar.DeepCopy();

            // UIから最新の値を取得してコピーに設定
            avatar.modelName = AvatarNameTextBox.Text;
            avatar.vrmFilePath = VRMFilePathTextBox.Text;
            avatar.isConvertMToon = ConvertMToonCheckBox.IsChecked ?? false;
            avatar.isEnableShadowOff = EnableShadowOffCheckBox.IsChecked ?? false;
            avatar.shadowOffMesh = ShadowOffMeshTextBox.Text;
            avatar.isUseSTT = IsUseSTTCheckBox.IsChecked ?? false;
            avatar.sttEngine = STTEngineComboBox.SelectedItem is ComboBoxItem selectedSttEngine ? selectedSttEngine.Tag?.ToString() ?? "amivoice" : "amivoice";
            avatar.sttWakeWord = STTWakeWordTextBox.Text;
            avatar.sttProfileId = STTProfileIdTextBox.Text;
            avatar.sttApiKey = STTApiKeyPasswordBox.Text;
            avatar.isUseTTS = IsUseTTSCheckBox.IsChecked ?? false;

            // TTSエンジンタイプ
            avatar.ttsType = TTSEngineComboBox.SelectedItem is ComboBoxItem selectedTtsEngine ? selectedTtsEngine.Tag?.ToString() ?? "voicevox" : "voicevox";

            // VOICEVOX詳細設定
            avatar.voicevoxConfig.endpointUrl = VoicevoxEndpointUrlTextBox.Text;
            if (int.TryParse(VoicevoxSpeakerIdTextBox.Text, out int voicevoxSpeakerId))
                avatar.voicevoxConfig.speakerId = voicevoxSpeakerId;
            avatar.voicevoxConfig.speedScale = (float)VoicevoxSpeedScaleSlider.Value;
            avatar.voicevoxConfig.pitchScale = (float)VoicevoxPitchScaleSlider.Value;
            avatar.voicevoxConfig.intonationScale = (float)VoicevoxIntonationScaleSlider.Value;
            avatar.voicevoxConfig.volumeScale = (float)VoicevoxVolumeScaleSlider.Value;
            avatar.voicevoxConfig.prePhonemeLength = (float)VoicevoxPrePhonemeLengthSlider.Value;
            avatar.voicevoxConfig.postPhonemeLength = (float)VoicevoxPostPhonemeLengthSlider.Value;

            // サンプリングレート設定
            if (VoicevoxOutputSamplingRateComboBox.SelectedItem is ComboBoxItem selectedSampleRate &&
                int.TryParse(selectedSampleRate.Tag?.ToString(), out int samplingRate))
                avatar.voicevoxConfig.outputSamplingRate = samplingRate;

            avatar.voicevoxConfig.outputStereo = VoicevoxOutputStereoCheckBox.IsChecked ?? false;

            // Style-Bert-VITS2設定
            avatar.styleBertVits2Config.endpointUrl = SBV2EndpointUrlTextBox.Text;
            avatar.styleBertVits2Config.modelName = SBV2ModelNameTextBox.Text;
            if (int.TryParse(SBV2ModelIdTextBox.Text, out int modelId))
                avatar.styleBertVits2Config.modelId = modelId;
            avatar.styleBertVits2Config.speakerName = SBV2SpeakerNameTextBox.Text;
            if (int.TryParse(SBV2SpeakerIdTextBox.Text, out int speakerId))
                avatar.styleBertVits2Config.speakerId = speakerId;
            avatar.styleBertVits2Config.style = SBV2StyleTextBox.Text;
            if (float.TryParse(SBV2StyleWeightTextBox.Text, out float styleWeight))
                avatar.styleBertVits2Config.styleWeight = styleWeight;
            avatar.styleBertVits2Config.language = SBV2LanguageTextBox.Text;
            if (float.TryParse(SBV2SdpRatioTextBox.Text, out float sdpRatio))
                avatar.styleBertVits2Config.sdpRatio = sdpRatio;
            if (float.TryParse(SBV2NoiseTextBox.Text, out float noise))
                avatar.styleBertVits2Config.noise = noise;
            if (float.TryParse(SBV2NoiseWTextBox.Text, out float noiseW))
                avatar.styleBertVits2Config.noiseW = noiseW;
            if (float.TryParse(SBV2LengthTextBox.Text, out float length))
                avatar.styleBertVits2Config.length = length;
            avatar.styleBertVits2Config.autoSplit = SBV2AutoSplitCheckBox.IsChecked ?? true;
            if (float.TryParse(SBV2SplitIntervalTextBox.Text, out float splitInterval))
                avatar.styleBertVits2Config.splitInterval = splitInterval;

            // AivisCloud設定
            avatar.aivisCloudConfig.endpointUrl = String.Empty; // AivisCloudのエンドポイントURLはCocoroShellで設定
            avatar.aivisCloudConfig.apiKey = AivisCloudApiKeyPasswordBox.Text;
            avatar.aivisCloudConfig.modelUuid = AivisCloudModelUuidTextBox.Text;
            avatar.aivisCloudConfig.speakerUuid = AivisCloudSpeakerUuidTextBox.Text;
            if (int.TryParse(AivisCloudStyleIdTextBox.Text, out int styleId))
                avatar.aivisCloudConfig.styleId = styleId;
            if (float.TryParse(AivisCloudSpeakingRateTextBox.Text, out float speakingRate))
                avatar.aivisCloudConfig.speakingRate = speakingRate;
            if (float.TryParse(AivisCloudEmotionalIntensityTextBox.Text, out float emotionalIntensity))
                avatar.aivisCloudConfig.emotionalIntensity = emotionalIntensity;
            if (float.TryParse(AivisCloudTempoDynamicsTextBox.Text, out float tempoDynamics))
                avatar.aivisCloudConfig.tempoDynamics = tempoDynamics;
            if (float.TryParse(AivisCloudVolumeTextBox.Text, out float volume))
                avatar.aivisCloudConfig.volume = volume;

            return avatar;
        }

        /// <summary>
        /// 現在のアバターインデックスを取得
        /// </summary>
        public int GetCurrentAvatarIndex()
        {
            return _currentAvatarIndex;
        }

        /// <summary>
        /// アバター選択変更イベント
        /// </summary>
        private void AvatarSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || AvatarSelectComboBox.SelectedIndex < 0)
                return;

            _currentAvatarIndex = AvatarSelectComboBox.SelectedIndex;
            UpdateAvatarUI();

            // アバター変更イベントを発生
            AvatarChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// アバターUIを更新
        /// </summary>
        private void UpdateAvatarUI()
        {
            if (_currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
                return;

            var avatar = AppSettings.Instance.AvatarList[_currentAvatarIndex];

            // 基本設定
            AvatarNameTextBox.Text = avatar.modelName;
            VRMFilePathTextBox.Text = avatar.vrmFilePath;
            ConvertMToonCheckBox.IsChecked = avatar.isConvertMToon;
            EnableShadowOffCheckBox.IsChecked = avatar.isEnableShadowOff;
            ShadowOffMeshTextBox.Text = avatar.shadowOffMesh;
            ShadowOffMeshTextBox.IsEnabled = avatar.isEnableShadowOff;

            // STT設定
            IsUseSTTCheckBox.IsChecked = avatar.isUseSTT;

            // STTエンジンComboBox設定
            foreach (ComboBoxItem item in STTEngineComboBox.Items)
            {
                if (item.Tag?.ToString() == avatar.sttEngine)
                {
                    STTEngineComboBox.SelectedItem = item;
                    break;
                }
            }

            STTWakeWordTextBox.Text = avatar.sttWakeWord;
            STTProfileIdTextBox.Text = avatar.sttProfileId;
            STTApiKeyPasswordBox.Text = avatar.sttApiKey;

            // TTS設定
            IsUseTTSCheckBox.IsChecked = avatar.isUseTTS;

            // VOICEVOX詳細設定の読み込み
            VoicevoxEndpointUrlTextBox.Text = avatar.voicevoxConfig.endpointUrl;
            VoicevoxSpeakerIdTextBox.Text = avatar.voicevoxConfig.speakerId.ToString();
            VoicevoxSpeedScaleSlider.Value = avatar.voicevoxConfig.speedScale;
            VoicevoxPitchScaleSlider.Value = avatar.voicevoxConfig.pitchScale;
            VoicevoxIntonationScaleSlider.Value = avatar.voicevoxConfig.intonationScale;
            VoicevoxVolumeScaleSlider.Value = avatar.voicevoxConfig.volumeScale;
            VoicevoxPrePhonemeLengthSlider.Value = avatar.voicevoxConfig.prePhonemeLength;
            VoicevoxPostPhonemeLengthSlider.Value = avatar.voicevoxConfig.postPhonemeLength;
            VoicevoxOutputStereoCheckBox.IsChecked = avatar.voicevoxConfig.outputStereo;

            // サンプリングレート設定
            foreach (ComboBoxItem item in VoicevoxOutputSamplingRateComboBox.Items)
            {
                if (item.Tag?.ToString() == avatar.voicevoxConfig.outputSamplingRate.ToString())
                {
                    VoicevoxOutputSamplingRateComboBox.SelectedItem = item;
                    break;
                }
            }

            // TTSエンジンComboBox設定
            foreach (ComboBoxItem item in TTSEngineComboBox.Items)
            {
                if (item.Tag?.ToString() == avatar.ttsType)
                {
                    TTSEngineComboBox.SelectedItem = item;
                    break;
                }
            }

            // Style-Bert-VITS2設定の読み込み
            SBV2EndpointUrlTextBox.Text = avatar.styleBertVits2Config.endpointUrl;
            SBV2ModelNameTextBox.Text = avatar.styleBertVits2Config.modelName;
            SBV2ModelIdTextBox.Text = avatar.styleBertVits2Config.modelId.ToString();
            SBV2SpeakerNameTextBox.Text = avatar.styleBertVits2Config.speakerName;
            SBV2SpeakerIdTextBox.Text = avatar.styleBertVits2Config.speakerId.ToString();
            SBV2StyleTextBox.Text = avatar.styleBertVits2Config.style;
            SBV2StyleWeightTextBox.Text = avatar.styleBertVits2Config.styleWeight.ToString("F1");
            SBV2LanguageTextBox.Text = avatar.styleBertVits2Config.language;
            SBV2SdpRatioTextBox.Text = avatar.styleBertVits2Config.sdpRatio.ToString("F1");
            SBV2NoiseTextBox.Text = avatar.styleBertVits2Config.noise.ToString("F1");
            SBV2NoiseWTextBox.Text = avatar.styleBertVits2Config.noiseW.ToString("F1");
            SBV2LengthTextBox.Text = avatar.styleBertVits2Config.length.ToString("F1");
            SBV2AutoSplitCheckBox.IsChecked = avatar.styleBertVits2Config.autoSplit;
            SBV2SplitIntervalTextBox.Text = avatar.styleBertVits2Config.splitInterval.ToString("F1");

            // AivisCloud設定の読み込み
            AivisCloudApiKeyPasswordBox.Text = avatar.aivisCloudConfig.apiKey;
            AivisCloudModelUuidTextBox.Text = avatar.aivisCloudConfig.modelUuid;
            AivisCloudSpeakerUuidTextBox.Text = avatar.aivisCloudConfig.speakerUuid;
            AivisCloudStyleIdTextBox.Text = avatar.aivisCloudConfig.styleId.ToString();
            AivisCloudSpeakingRateTextBox.Text = avatar.aivisCloudConfig.speakingRate.ToString("F1");
            AivisCloudEmotionalIntensityTextBox.Text = avatar.aivisCloudConfig.emotionalIntensity.ToString("F1");
            AivisCloudTempoDynamicsTextBox.Text = avatar.aivisCloudConfig.tempoDynamics.ToString("F1");
            AivisCloudVolumeTextBox.Text = avatar.aivisCloudConfig.volume.ToString("F1");

            // TTSパネルの表示を更新
            UpdateTTSPanelVisibility(avatar.ttsType);

            // 読み取り専用の場合は削除ボタン、VRMファイル欄、開くボタンを無効化
            DeleteAvatarButton.IsEnabled = !avatar.isReadOnly;
            VRMFilePathTextBox.IsEnabled = !avatar.isReadOnly;
            BrowseVrmFileButton.IsEnabled = !avatar.isReadOnly;
        }

        /// <summary>
        /// アバター追加ボタンクリック
        /// </summary>
        private void AddAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 新規アバターの名前を生成
                var newName = "新規アバター";

                // 同名のアバターが既に存在する場合は番号を付ける
                int avatarNumber = 1;
                while (AppSettings.Instance.AvatarList.Any(c => c.modelName == newName))
                {
                    newName = $"新規アバター{avatarNumber}";
                    avatarNumber++;
                }

                var newAvatar = AppSettings.Instance.CreateAvatarFromDefaults(newName);

                AppSettings.Instance.AvatarList.Add(newAvatar);

                // ComboBoxのItemsSourceを更新
                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;
                int newIndex = AppSettings.Instance.AvatarList.Count - 1;
                AvatarSelectComboBox.SelectedIndex = newIndex;

                // 設定変更イベントを発生
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アバター追加エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// アバター削除ボタンクリック
        /// </summary>
        private void DeleteAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
                    return;

                var avatar = AppSettings.Instance.AvatarList[_currentAvatarIndex];
                if (avatar.isReadOnly)
                {
                    return;
                }

                AppSettings.Instance.AvatarList.RemoveAt(_currentAvatarIndex);

                // ComboBoxのItemsSourceを更新
                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;

                if (AppSettings.Instance.AvatarList.Count > 0)
                {
                    AvatarSelectComboBox.SelectedIndex = 0;
                }

                // 設定変更イベントを発生
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アバター削除エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// アバター複製ボタンクリック
        /// </summary>
        private void DuplicateAvatarButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
                    return;

                var sourceAvatar = AppSettings.Instance.AvatarList[_currentAvatarIndex];

                // 複製するアバターの名前を生成
                var newName = sourceAvatar.modelName + "_copy";

                // 同名のアバターが既に存在する場合は番号を付ける
                int copyNumber = 1;
                while (AppSettings.Instance.AvatarList.Any(c => c.modelName == newName))
                {
                    newName = $"{sourceAvatar.modelName}_copy{copyNumber}";
                    copyNumber++;
                }

                // アバター設定をコピー
                var newAvatar = new AvatarSettings
                {
                    modelName = newName,
                    vrmFilePath = sourceAvatar.vrmFilePath,
                    isUseTTS = sourceAvatar.isUseTTS,
                    ttsType = sourceAvatar.ttsType,

                    // VOICEVOX詳細設定のコピー
                    voicevoxConfig = new VoicevoxConfig
                    {
                        endpointUrl = sourceAvatar.voicevoxConfig.endpointUrl,
                        speakerId = sourceAvatar.voicevoxConfig.speakerId,
                        speedScale = sourceAvatar.voicevoxConfig.speedScale,
                        pitchScale = sourceAvatar.voicevoxConfig.pitchScale,
                        intonationScale = sourceAvatar.voicevoxConfig.intonationScale,
                        volumeScale = sourceAvatar.voicevoxConfig.volumeScale,
                        prePhonemeLength = sourceAvatar.voicevoxConfig.prePhonemeLength,
                        postPhonemeLength = sourceAvatar.voicevoxConfig.postPhonemeLength,
                        outputSamplingRate = sourceAvatar.voicevoxConfig.outputSamplingRate,
                        outputStereo = sourceAvatar.voicevoxConfig.outputStereo
                    },
                    styleBertVits2Config = new StyleBertVits2Config
                    {
                        endpointUrl = sourceAvatar.styleBertVits2Config.endpointUrl,
                        modelName = sourceAvatar.styleBertVits2Config.modelName,
                        modelId = sourceAvatar.styleBertVits2Config.modelId,
                        speakerName = sourceAvatar.styleBertVits2Config.speakerName,
                        speakerId = sourceAvatar.styleBertVits2Config.speakerId,
                        style = sourceAvatar.styleBertVits2Config.style,
                        styleWeight = sourceAvatar.styleBertVits2Config.styleWeight,
                        language = sourceAvatar.styleBertVits2Config.language,
                        sdpRatio = sourceAvatar.styleBertVits2Config.sdpRatio,
                        noise = sourceAvatar.styleBertVits2Config.noise,
                        noiseW = sourceAvatar.styleBertVits2Config.noiseW,
                        length = sourceAvatar.styleBertVits2Config.length,
                        autoSplit = sourceAvatar.styleBertVits2Config.autoSplit,
                        splitInterval = sourceAvatar.styleBertVits2Config.splitInterval,
                        assistText = sourceAvatar.styleBertVits2Config.assistText,
                        assistTextWeight = sourceAvatar.styleBertVits2Config.assistTextWeight,
                        referenceAudioPath = sourceAvatar.styleBertVits2Config.referenceAudioPath
                    },
                    aivisCloudConfig = new AivisCloudConfig
                    {
                        apiKey = sourceAvatar.aivisCloudConfig.apiKey,
                        endpointUrl = sourceAvatar.aivisCloudConfig.endpointUrl,
                        modelUuid = sourceAvatar.aivisCloudConfig.modelUuid,
                        speakerUuid = sourceAvatar.aivisCloudConfig.speakerUuid,
                        styleId = sourceAvatar.aivisCloudConfig.styleId,
                        styleName = sourceAvatar.aivisCloudConfig.styleName,
                        useSSML = sourceAvatar.aivisCloudConfig.useSSML,
                        language = sourceAvatar.aivisCloudConfig.language,
                        speakingRate = sourceAvatar.aivisCloudConfig.speakingRate,
                        emotionalIntensity = sourceAvatar.aivisCloudConfig.emotionalIntensity,
                        tempoDynamics = sourceAvatar.aivisCloudConfig.tempoDynamics,
                        pitch = sourceAvatar.aivisCloudConfig.pitch,
                        volume = sourceAvatar.aivisCloudConfig.volume,
                        outputFormat = sourceAvatar.aivisCloudConfig.outputFormat,
                        outputBitrate = sourceAvatar.aivisCloudConfig.outputBitrate,
                        outputSamplingRate = sourceAvatar.aivisCloudConfig.outputSamplingRate,
                        outputAudioChannels = sourceAvatar.aivisCloudConfig.outputAudioChannels,
                    },
                    isUseSTT = sourceAvatar.isUseSTT,
                    sttEngine = sourceAvatar.sttEngine,
                    sttWakeWord = sourceAvatar.sttWakeWord,
                    sttProfileId = sourceAvatar.sttProfileId,
                    sttApiKey = sourceAvatar.sttApiKey,
                    sttLanguage = sourceAvatar.sttLanguage,
                    isConvertMToon = sourceAvatar.isConvertMToon,
                    isEnableShadowOff = sourceAvatar.isEnableShadowOff,
                    shadowOffMesh = sourceAvatar.shadowOffMesh,
                    isReadOnly = false
                };

                // リストに追加
                AppSettings.Instance.AvatarList.Add(newAvatar);

                // ComboBoxのItemsSourceを更新（ItemsSourceとItemsの併用を避ける）
                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;

                // 新しく追加したアバターを選択
                int newIndex = AppSettings.Instance.AvatarList.Count - 1;
                AvatarSelectComboBox.SelectedIndex = newIndex;

                // 設定変更イベントを発生
                SettingsChanged?.Invoke(this, EventArgs.Empty);

                Debug.WriteLine($"アバター複製: {sourceAvatar.modelName} -> {newName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アバター複製エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// VRMファイル選択ボタンクリック
        /// </summary>
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

                    // ファイル名から自動的にアバター名を更新（ユーザーが変更可能）
                    if (string.IsNullOrWhiteSpace(AvatarNameTextBox.Text))
                    {
                        AvatarNameTextBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                    }

                    // 設定変更イベントを発生
                    SettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイル選択エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 影オフチェックボックスのチェック状態変更
        /// </summary>
        private void EnableShadowOffCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (ShadowOffMeshTextBox != null)
            {
                ShadowOffMeshTextBox.IsEnabled = true;
            }
        }

        private void EnableShadowOffCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (ShadowOffMeshTextBox != null)
            {
                ShadowOffMeshTextBox.IsEnabled = false;
            }
        }

        /// <summary>
        /// TTSエンジン選択変更処理
        /// </summary>
        private void TTSEngineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || TTSEngineComboBox.SelectedItem == null)
                return;

            var selectedItem = (ComboBoxItem)TTSEngineComboBox.SelectedItem;
            var engineType = selectedItem.Tag?.ToString();

            // エンジンタイプに応じて表示パネルを切り替え
            UpdateTTSPanelVisibility(engineType);

            // 設定変更イベントを発火
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// TTSパネルの表示/非表示を切り替え
        /// </summary>
        private void UpdateTTSPanelVisibility(string? engineType)
        {
            if (VoicevoxSettingsPanel == null || StyleBertVits2BasicPanel == null || StyleBertVits2SettingsPanel == null || AivisCloudSettingsPanel == null)
                return;

            switch (engineType)
            {
                case "voicevox":
                    VoicevoxSettingsPanel.Visibility = Visibility.Visible;
                    StyleBertVits2BasicPanel.Visibility = Visibility.Collapsed;
                    StyleBertVits2SettingsPanel.Visibility = Visibility.Collapsed;
                    AivisCloudSettingsPanel.Visibility = Visibility.Collapsed;
                    break;
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
                default:
                    VoicevoxSettingsPanel.Visibility = Visibility.Visible;
                    StyleBertVits2BasicPanel.Visibility = Visibility.Collapsed;
                    StyleBertVits2SettingsPanel.Visibility = Visibility.Collapsed;
                    AivisCloudSettingsPanel.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        /// <summary>
        /// アバター名のテキスト変更イベント（リアルタイム更新）
        /// </summary>
        private void AvatarNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _currentAvatarIndex < 0)
                return;

            // タイマーがすでに動作中の場合はリセット
            if (_avatarNameChangeTimer != null)
            {
                _avatarNameChangeTimer.Stop();
                _avatarNameChangeTimer.Start();
            }
        }

        /// <summary>
        /// アバター名変更タイマーのTickイベント（デバウンス処理）
        /// </summary>
        private void AvatarNameChangeTimer_Tick(object? sender, EventArgs e)
        {
            if (_avatarNameChangeTimer != null)
            {
                _avatarNameChangeTimer.Stop();
            }

            if (!_isInitialized || _currentAvatarIndex < 0 || _currentAvatarIndex >= AppSettings.Instance.AvatarList.Count)
                return;

            var newName = AvatarNameTextBox.Text;
            if (!string.IsNullOrWhiteSpace(newName))
            {
                // 現在選択されているアイテムのインデックスを保存
                var currentSelectedIndex = _currentAvatarIndex;

                // アバター設定の名前を更新
                AppSettings.Instance.AvatarList[_currentAvatarIndex].modelName = newName;

                // ComboBoxのItemsSourceを一時的に無効にしてSelectionChangedイベントを防ぐ
                AvatarSelectComboBox.SelectionChanged -= AvatarSelectComboBox_SelectionChanged;

                // ComboBoxのItemsSourceを更新
                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;

                // 選択状態を復元
                AvatarSelectComboBox.SelectedIndex = currentSelectedIndex;

                // SelectionChangedイベントハンドラーを再設定
                AvatarSelectComboBox.SelectionChanged += AvatarSelectComboBox_SelectionChanged;

                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// アバターリストのUIを更新
        /// </summary>
        public void RefreshAvatarList()
        {
            try
            {
                AvatarSelectComboBox.ItemsSource = null;
                AvatarSelectComboBox.ItemsSource = AppSettings.Instance.AvatarList;

                if (AppSettings.Instance.AvatarList.Count > 0)
                {
                    int indexToSelect = Math.Min(_currentAvatarIndex, AppSettings.Instance.AvatarList.Count - 1);
                    if (indexToSelect < 0) indexToSelect = 0;

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
        }
    }
}
