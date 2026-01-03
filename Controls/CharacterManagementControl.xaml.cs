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
    /// CharacterManagementControl.xaml の相互作用ロジック
    /// </summary>
    public partial class CharacterManagementControl : UserControl
    {
        /// <summary>
        /// 設定が変更されたときに発生するイベント
        /// </summary>
        public event EventHandler? SettingsChanged;

        /// <summary>
        /// キャラクターが変更されたときに発生するイベント
        /// </summary>
        public event EventHandler? CharacterChanged;

        /// <summary>
        /// 現在選択中のキャラクターインデックス
        /// </summary>
        private int _currentCharacterIndex = -1;

        /// <summary>
        /// 読み込み完了フラグ
        /// </summary>
        private bool _isInitialized = false;

        /// <summary>
        /// キャラクター名変更のデバウンス用タイマー
        /// </summary>
        private DispatcherTimer? _characterNameChangeTimer;

        /// <summary>
        /// デバウンス遅延時間（ミリ秒）
        /// </summary>
        private const int CHARACTER_NAME_DEBOUNCE_DELAY_MS = 200;

        public CharacterManagementControl()
        {
            InitializeComponent();

            // キャラクター名変更用のデバウンスタイマーを初期化
            _characterNameChangeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CHARACTER_NAME_DEBOUNCE_DELAY_MS)
            };
            _characterNameChangeTimer.Tick += CharacterNameChangeTimer_Tick;

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
            LoadCharacterList();

            // 選択されたキャラクターの設定をUIに反映
            if (CharacterSelectComboBox.SelectedIndex >= 0)
            {
                _currentCharacterIndex = CharacterSelectComboBox.SelectedIndex;
                UpdateCharacterUI();
            }

            _isInitialized = true;
        }

        /// <summary>
        /// キャラクターリストを読み込み
        /// </summary>
        private void LoadCharacterList()
        {
            var appSettings = AppSettings.Instance;

            // ItemsSourceを使用
            CharacterSelectComboBox.ItemsSource = appSettings.CharacterList;

            if (appSettings.CharacterList.Count > 0 &&
                appSettings.CurrentCharacterIndex >= 0 &&
                appSettings.CurrentCharacterIndex < appSettings.CharacterList.Count)
            {
                CharacterSelectComboBox.SelectedIndex = appSettings.CurrentCharacterIndex;
            }
        }

        /// <summary>
        /// UI上の現在のキャラクター設定を取得（UIから値を読み取ってディープコピーを返却）
        /// </summary>
        public CharacterSettings? GetCurrentCharacterSettingFromUI()
        {
            if (_currentCharacterIndex < 0 || _currentCharacterIndex >= AppSettings.Instance.CharacterList.Count)
                return null;

            // 既存のキャラクター設定のディープコピーを作成
            var originalCharacter = AppSettings.Instance.CharacterList[_currentCharacterIndex];
            var character = originalCharacter.DeepCopy();

            // UIから最新の値を取得してコピーに設定
            character.modelName = CharacterNameTextBox.Text;
            character.vrmFilePath = VRMFilePathTextBox.Text;
            character.isConvertMToon = ConvertMToonCheckBox.IsChecked ?? false;
            character.isEnableShadowOff = EnableShadowOffCheckBox.IsChecked ?? false;
            character.shadowOffMesh = ShadowOffMeshTextBox.Text;
            character.isUseSTT = IsUseSTTCheckBox.IsChecked ?? false;
            character.sttEngine = STTEngineComboBox.SelectedItem is ComboBoxItem selectedSttEngine ? selectedSttEngine.Tag?.ToString() ?? "amivoice" : "amivoice";
            character.sttWakeWord = STTWakeWordTextBox.Text;
            character.sttApiKey = STTApiKeyPasswordBox.Text;
            character.isUseTTS = IsUseTTSCheckBox.IsChecked ?? false;

            // TTSエンジンタイプ
            character.ttsType = TTSEngineComboBox.SelectedItem is ComboBoxItem selectedTtsEngine ? selectedTtsEngine.Tag?.ToString() ?? "voicevox" : "voicevox";

            // VOICEVOX詳細設定
            character.voicevoxConfig.endpointUrl = VoicevoxEndpointUrlTextBox.Text;
            if (int.TryParse(VoicevoxSpeakerIdTextBox.Text, out int voicevoxSpeakerId))
                character.voicevoxConfig.speakerId = voicevoxSpeakerId;
            character.voicevoxConfig.speedScale = (float)VoicevoxSpeedScaleSlider.Value;
            character.voicevoxConfig.pitchScale = (float)VoicevoxPitchScaleSlider.Value;
            character.voicevoxConfig.intonationScale = (float)VoicevoxIntonationScaleSlider.Value;
            character.voicevoxConfig.volumeScale = (float)VoicevoxVolumeScaleSlider.Value;
            character.voicevoxConfig.prePhonemeLength = (float)VoicevoxPrePhonemeLengthSlider.Value;
            character.voicevoxConfig.postPhonemeLength = (float)VoicevoxPostPhonemeLengthSlider.Value;

            // サンプリングレート設定
            if (VoicevoxOutputSamplingRateComboBox.SelectedItem is ComboBoxItem selectedSampleRate &&
                int.TryParse(selectedSampleRate.Tag?.ToString(), out int samplingRate))
                character.voicevoxConfig.outputSamplingRate = samplingRate;

            character.voicevoxConfig.outputStereo = VoicevoxOutputStereoCheckBox.IsChecked ?? false;

            // Style-Bert-VITS2設定
            character.styleBertVits2Config.endpointUrl = SBV2EndpointUrlTextBox.Text;
            character.styleBertVits2Config.modelName = SBV2ModelNameTextBox.Text;
            if (int.TryParse(SBV2ModelIdTextBox.Text, out int modelId))
                character.styleBertVits2Config.modelId = modelId;
            character.styleBertVits2Config.speakerName = SBV2SpeakerNameTextBox.Text;
            if (int.TryParse(SBV2SpeakerIdTextBox.Text, out int speakerId))
                character.styleBertVits2Config.speakerId = speakerId;
            character.styleBertVits2Config.style = SBV2StyleTextBox.Text;
            if (float.TryParse(SBV2StyleWeightTextBox.Text, out float styleWeight))
                character.styleBertVits2Config.styleWeight = styleWeight;
            character.styleBertVits2Config.language = SBV2LanguageTextBox.Text;
            if (float.TryParse(SBV2SdpRatioTextBox.Text, out float sdpRatio))
                character.styleBertVits2Config.sdpRatio = sdpRatio;
            if (float.TryParse(SBV2NoiseTextBox.Text, out float noise))
                character.styleBertVits2Config.noise = noise;
            if (float.TryParse(SBV2NoiseWTextBox.Text, out float noiseW))
                character.styleBertVits2Config.noiseW = noiseW;
            if (float.TryParse(SBV2LengthTextBox.Text, out float length))
                character.styleBertVits2Config.length = length;
            character.styleBertVits2Config.autoSplit = SBV2AutoSplitCheckBox.IsChecked ?? true;
            if (float.TryParse(SBV2SplitIntervalTextBox.Text, out float splitInterval))
                character.styleBertVits2Config.splitInterval = splitInterval;

            // AivisCloud設定
            character.aivisCloudConfig.endpointUrl = String.Empty; // AivisCloudのエンドポイントURLはCocoroShellで設定
            character.aivisCloudConfig.apiKey = AivisCloudApiKeyPasswordBox.Text;
            character.aivisCloudConfig.modelUuid = AivisCloudModelUuidTextBox.Text;
            character.aivisCloudConfig.speakerUuid = AivisCloudSpeakerUuidTextBox.Text;
            if (int.TryParse(AivisCloudStyleIdTextBox.Text, out int styleId))
                character.aivisCloudConfig.styleId = styleId;
            if (float.TryParse(AivisCloudSpeakingRateTextBox.Text, out float speakingRate))
                character.aivisCloudConfig.speakingRate = speakingRate;
            if (float.TryParse(AivisCloudEmotionalIntensityTextBox.Text, out float emotionalIntensity))
                character.aivisCloudConfig.emotionalIntensity = emotionalIntensity;
            if (float.TryParse(AivisCloudTempoDynamicsTextBox.Text, out float tempoDynamics))
                character.aivisCloudConfig.tempoDynamics = tempoDynamics;
            if (float.TryParse(AivisCloudVolumeTextBox.Text, out float volume))
                character.aivisCloudConfig.volume = volume;

            return character;
        }

        /// <summary>
        /// 現在のキャラクターインデックスを取得
        /// </summary>
        public int GetCurrentCharacterIndex()
        {
            return _currentCharacterIndex;
        }

        /// <summary>
        /// キャラクター選択変更イベント
        /// </summary>
        private void CharacterSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || CharacterSelectComboBox.SelectedIndex < 0)
                return;

            _currentCharacterIndex = CharacterSelectComboBox.SelectedIndex;
            UpdateCharacterUI();

            // キャラクター変更イベントを発生
            CharacterChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// キャラクターUIを更新
        /// </summary>
        private void UpdateCharacterUI()
        {
            if (_currentCharacterIndex < 0 || _currentCharacterIndex >= AppSettings.Instance.CharacterList.Count)
                return;

            var character = AppSettings.Instance.CharacterList[_currentCharacterIndex];

            // 基本設定
            CharacterNameTextBox.Text = character.modelName;
            VRMFilePathTextBox.Text = character.vrmFilePath;
            ConvertMToonCheckBox.IsChecked = character.isConvertMToon;
            EnableShadowOffCheckBox.IsChecked = character.isEnableShadowOff;
            ShadowOffMeshTextBox.Text = character.shadowOffMesh;
            ShadowOffMeshTextBox.IsEnabled = character.isEnableShadowOff;

            // STT設定
            IsUseSTTCheckBox.IsChecked = character.isUseSTT;

            // STTエンジンComboBox設定
            foreach (ComboBoxItem item in STTEngineComboBox.Items)
            {
                if (item.Tag?.ToString() == character.sttEngine)
                {
                    STTEngineComboBox.SelectedItem = item;
                    break;
                }
            }

            STTWakeWordTextBox.Text = character.sttWakeWord;
            STTApiKeyPasswordBox.Text = character.sttApiKey;

            // TTS設定
            IsUseTTSCheckBox.IsChecked = character.isUseTTS;

            // VOICEVOX詳細設定の読み込み
            VoicevoxEndpointUrlTextBox.Text = character.voicevoxConfig.endpointUrl;
            VoicevoxSpeakerIdTextBox.Text = character.voicevoxConfig.speakerId.ToString();
            VoicevoxSpeedScaleSlider.Value = character.voicevoxConfig.speedScale;
            VoicevoxPitchScaleSlider.Value = character.voicevoxConfig.pitchScale;
            VoicevoxIntonationScaleSlider.Value = character.voicevoxConfig.intonationScale;
            VoicevoxVolumeScaleSlider.Value = character.voicevoxConfig.volumeScale;
            VoicevoxPrePhonemeLengthSlider.Value = character.voicevoxConfig.prePhonemeLength;
            VoicevoxPostPhonemeLengthSlider.Value = character.voicevoxConfig.postPhonemeLength;
            VoicevoxOutputStereoCheckBox.IsChecked = character.voicevoxConfig.outputStereo;

            // サンプリングレート設定
            foreach (ComboBoxItem item in VoicevoxOutputSamplingRateComboBox.Items)
            {
                if (item.Tag?.ToString() == character.voicevoxConfig.outputSamplingRate.ToString())
                {
                    VoicevoxOutputSamplingRateComboBox.SelectedItem = item;
                    break;
                }
            }

            // TTSエンジンComboBox設定
            foreach (ComboBoxItem item in TTSEngineComboBox.Items)
            {
                if (item.Tag?.ToString() == character.ttsType)
                {
                    TTSEngineComboBox.SelectedItem = item;
                    break;
                }
            }

            // Style-Bert-VITS2設定の読み込み
            SBV2EndpointUrlTextBox.Text = character.styleBertVits2Config.endpointUrl;
            SBV2ModelNameTextBox.Text = character.styleBertVits2Config.modelName;
            SBV2ModelIdTextBox.Text = character.styleBertVits2Config.modelId.ToString();
            SBV2SpeakerNameTextBox.Text = character.styleBertVits2Config.speakerName;
            SBV2SpeakerIdTextBox.Text = character.styleBertVits2Config.speakerId.ToString();
            SBV2StyleTextBox.Text = character.styleBertVits2Config.style;
            SBV2StyleWeightTextBox.Text = character.styleBertVits2Config.styleWeight.ToString("F1");
            SBV2LanguageTextBox.Text = character.styleBertVits2Config.language;
            SBV2SdpRatioTextBox.Text = character.styleBertVits2Config.sdpRatio.ToString("F1");
            SBV2NoiseTextBox.Text = character.styleBertVits2Config.noise.ToString("F1");
            SBV2NoiseWTextBox.Text = character.styleBertVits2Config.noiseW.ToString("F1");
            SBV2LengthTextBox.Text = character.styleBertVits2Config.length.ToString("F1");
            SBV2AutoSplitCheckBox.IsChecked = character.styleBertVits2Config.autoSplit;
            SBV2SplitIntervalTextBox.Text = character.styleBertVits2Config.splitInterval.ToString("F1");

            // AivisCloud設定の読み込み
            AivisCloudApiKeyPasswordBox.Text = character.aivisCloudConfig.apiKey;
            AivisCloudModelUuidTextBox.Text = character.aivisCloudConfig.modelUuid;
            AivisCloudSpeakerUuidTextBox.Text = character.aivisCloudConfig.speakerUuid;
            AivisCloudStyleIdTextBox.Text = character.aivisCloudConfig.styleId.ToString();
            AivisCloudSpeakingRateTextBox.Text = character.aivisCloudConfig.speakingRate.ToString("F1");
            AivisCloudEmotionalIntensityTextBox.Text = character.aivisCloudConfig.emotionalIntensity.ToString("F1");
            AivisCloudTempoDynamicsTextBox.Text = character.aivisCloudConfig.tempoDynamics.ToString("F1");
            AivisCloudVolumeTextBox.Text = character.aivisCloudConfig.volume.ToString("F1");

            // TTSパネルの表示を更新
            UpdateTTSPanelVisibility(character.ttsType);

            // 読み取り専用の場合は削除ボタン、VRMファイル欄、開くボタンを無効化
            DeleteCharacterButton.IsEnabled = !character.isReadOnly;
            VRMFilePathTextBox.IsEnabled = !character.isReadOnly;
            BrowseVrmFileButton.IsEnabled = !character.isReadOnly;
        }

        /// <summary>
        /// キャラクター追加ボタンクリック
        /// </summary>
        private void AddCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 新規キャラクターの名前を生成
                var newName = "新規キャラクター";

                // 同名のキャラクターが既に存在する場合は番号を付ける
                int characterNumber = 1;
                while (AppSettings.Instance.CharacterList.Any(c => c.modelName == newName))
                {
                    newName = $"新規キャラクター{characterNumber}";
                    characterNumber++;
                }

                var newCharacter = AppSettings.Instance.CreateCharacterFromDefaults(newName);

                AppSettings.Instance.CharacterList.Add(newCharacter);

                // ComboBoxのItemsSourceを更新
                CharacterSelectComboBox.ItemsSource = null;
                CharacterSelectComboBox.ItemsSource = AppSettings.Instance.CharacterList;
                int newIndex = AppSettings.Instance.CharacterList.Count - 1;
                CharacterSelectComboBox.SelectedIndex = newIndex;

                // 設定変更イベントを発生
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"キャラクター追加エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// キャラクター削除ボタンクリック
        /// </summary>
        private void DeleteCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentCharacterIndex < 0 || _currentCharacterIndex >= AppSettings.Instance.CharacterList.Count)
                    return;

                var character = AppSettings.Instance.CharacterList[_currentCharacterIndex];
                if (character.isReadOnly)
                {
                    return;
                }

                AppSettings.Instance.CharacterList.RemoveAt(_currentCharacterIndex);

                // ComboBoxのItemsSourceを更新
                CharacterSelectComboBox.ItemsSource = null;
                CharacterSelectComboBox.ItemsSource = AppSettings.Instance.CharacterList;

                if (AppSettings.Instance.CharacterList.Count > 0)
                {
                    CharacterSelectComboBox.SelectedIndex = 0;
                }

                // 設定変更イベントを発生
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"キャラクター削除エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// キャラクター複製ボタンクリック
        /// </summary>
        private void DuplicateCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentCharacterIndex < 0 || _currentCharacterIndex >= AppSettings.Instance.CharacterList.Count)
                    return;

                var sourceCharacter = AppSettings.Instance.CharacterList[_currentCharacterIndex];

                // 複製するキャラクターの名前を生成
                var newName = sourceCharacter.modelName + "_copy";

                // 同名のキャラクターが既に存在する場合は番号を付ける
                int copyNumber = 1;
                while (AppSettings.Instance.CharacterList.Any(c => c.modelName == newName))
                {
                    newName = $"{sourceCharacter.modelName}_copy{copyNumber}";
                    copyNumber++;
                }

                // キャラクター設定をコピー
                var newCharacter = new CharacterSettings
                {
                    modelName = newName,
                    vrmFilePath = sourceCharacter.vrmFilePath,
                    isUseTTS = sourceCharacter.isUseTTS,
                    ttsType = sourceCharacter.ttsType,

                    // VOICEVOX詳細設定のコピー
                    voicevoxConfig = new VoicevoxConfig
                    {
                        endpointUrl = sourceCharacter.voicevoxConfig.endpointUrl,
                        speakerId = sourceCharacter.voicevoxConfig.speakerId,
                        speedScale = sourceCharacter.voicevoxConfig.speedScale,
                        pitchScale = sourceCharacter.voicevoxConfig.pitchScale,
                        intonationScale = sourceCharacter.voicevoxConfig.intonationScale,
                        volumeScale = sourceCharacter.voicevoxConfig.volumeScale,
                        prePhonemeLength = sourceCharacter.voicevoxConfig.prePhonemeLength,
                        postPhonemeLength = sourceCharacter.voicevoxConfig.postPhonemeLength,
                        outputSamplingRate = sourceCharacter.voicevoxConfig.outputSamplingRate,
                        outputStereo = sourceCharacter.voicevoxConfig.outputStereo
                    },
                    styleBertVits2Config = new StyleBertVits2Config
                    {
                        endpointUrl = sourceCharacter.styleBertVits2Config.endpointUrl,
                        modelName = sourceCharacter.styleBertVits2Config.modelName,
                        modelId = sourceCharacter.styleBertVits2Config.modelId,
                        speakerName = sourceCharacter.styleBertVits2Config.speakerName,
                        speakerId = sourceCharacter.styleBertVits2Config.speakerId,
                        style = sourceCharacter.styleBertVits2Config.style,
                        styleWeight = sourceCharacter.styleBertVits2Config.styleWeight,
                        language = sourceCharacter.styleBertVits2Config.language,
                        sdpRatio = sourceCharacter.styleBertVits2Config.sdpRatio,
                        noise = sourceCharacter.styleBertVits2Config.noise,
                        noiseW = sourceCharacter.styleBertVits2Config.noiseW,
                        length = sourceCharacter.styleBertVits2Config.length,
                        autoSplit = sourceCharacter.styleBertVits2Config.autoSplit,
                        splitInterval = sourceCharacter.styleBertVits2Config.splitInterval,
                        assistText = sourceCharacter.styleBertVits2Config.assistText,
                        assistTextWeight = sourceCharacter.styleBertVits2Config.assistTextWeight,
                        referenceAudioPath = sourceCharacter.styleBertVits2Config.referenceAudioPath
                    },
                    aivisCloudConfig = new AivisCloudConfig
                    {
                        apiKey = sourceCharacter.aivisCloudConfig.apiKey,
                        endpointUrl = sourceCharacter.aivisCloudConfig.endpointUrl,
                        modelUuid = sourceCharacter.aivisCloudConfig.modelUuid,
                        speakerUuid = sourceCharacter.aivisCloudConfig.speakerUuid,
                        styleId = sourceCharacter.aivisCloudConfig.styleId,
                        styleName = sourceCharacter.aivisCloudConfig.styleName,
                        useSSML = sourceCharacter.aivisCloudConfig.useSSML,
                        language = sourceCharacter.aivisCloudConfig.language,
                        speakingRate = sourceCharacter.aivisCloudConfig.speakingRate,
                        emotionalIntensity = sourceCharacter.aivisCloudConfig.emotionalIntensity,
                        tempoDynamics = sourceCharacter.aivisCloudConfig.tempoDynamics,
                        pitch = sourceCharacter.aivisCloudConfig.pitch,
                        volume = sourceCharacter.aivisCloudConfig.volume,
                        outputFormat = sourceCharacter.aivisCloudConfig.outputFormat,
                        outputBitrate = sourceCharacter.aivisCloudConfig.outputBitrate,
                        outputSamplingRate = sourceCharacter.aivisCloudConfig.outputSamplingRate,
                        outputAudioChannels = sourceCharacter.aivisCloudConfig.outputAudioChannels,
                    },
                    isUseSTT = sourceCharacter.isUseSTT,
                    sttEngine = sourceCharacter.sttEngine,
                    sttWakeWord = sourceCharacter.sttWakeWord,
                    sttApiKey = sourceCharacter.sttApiKey,
                    sttLanguage = sourceCharacter.sttLanguage,
                    isConvertMToon = sourceCharacter.isConvertMToon,
                    isEnableShadowOff = sourceCharacter.isEnableShadowOff,
                    shadowOffMesh = sourceCharacter.shadowOffMesh,
                    isReadOnly = false
                };

                // リストに追加
                AppSettings.Instance.CharacterList.Add(newCharacter);

                // ComboBoxのItemsSourceを更新（ItemsSourceとItemsの併用を避ける）
                CharacterSelectComboBox.ItemsSource = null;
                CharacterSelectComboBox.ItemsSource = AppSettings.Instance.CharacterList;

                // 新しく追加したキャラクターを選択
                int newIndex = AppSettings.Instance.CharacterList.Count - 1;
                CharacterSelectComboBox.SelectedIndex = newIndex;

                // 設定変更イベントを発生
                SettingsChanged?.Invoke(this, EventArgs.Empty);

                Debug.WriteLine($"キャラクター複製: {sourceCharacter.modelName} -> {newName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"キャラクター複製エラー: {ex.Message}", "エラー",
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

                    // ファイル名から自動的にキャラクター名を更新（ユーザーが変更可能）
                    if (string.IsNullOrWhiteSpace(CharacterNameTextBox.Text))
                    {
                        CharacterNameTextBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
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
        /// キャラクター名のテキスト変更イベント（リアルタイム更新）
        /// </summary>
        private void CharacterNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || _currentCharacterIndex < 0)
                return;

            // タイマーがすでに動作中の場合はリセット
            if (_characterNameChangeTimer != null)
            {
                _characterNameChangeTimer.Stop();
                _characterNameChangeTimer.Start();
            }
        }

        /// <summary>
        /// キャラクター名変更タイマーのTickイベント（デバウンス処理）
        /// </summary>
        private void CharacterNameChangeTimer_Tick(object? sender, EventArgs e)
        {
            if (_characterNameChangeTimer != null)
            {
                _characterNameChangeTimer.Stop();
            }

            if (!_isInitialized || _currentCharacterIndex < 0 || _currentCharacterIndex >= AppSettings.Instance.CharacterList.Count)
                return;

            var newName = CharacterNameTextBox.Text;
            if (!string.IsNullOrWhiteSpace(newName))
            {
                // 現在選択されているアイテムのインデックスを保存
                var currentSelectedIndex = _currentCharacterIndex;

                // キャラクター設定の名前を更新
                AppSettings.Instance.CharacterList[_currentCharacterIndex].modelName = newName;

                // ComboBoxのItemsSourceを一時的に無効にしてSelectionChangedイベントを防ぐ
                CharacterSelectComboBox.SelectionChanged -= CharacterSelectComboBox_SelectionChanged;

                // ComboBoxのItemsSourceを更新
                CharacterSelectComboBox.ItemsSource = null;
                CharacterSelectComboBox.ItemsSource = AppSettings.Instance.CharacterList;

                // 選択状態を復元
                CharacterSelectComboBox.SelectedIndex = currentSelectedIndex;

                // SelectionChangedイベントハンドラーを再設定
                CharacterSelectComboBox.SelectionChanged += CharacterSelectComboBox_SelectionChanged;

                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// キャラクターリストのUIを更新
        /// </summary>
        public void RefreshCharacterList()
        {
            try
            {
                CharacterSelectComboBox.ItemsSource = null;
                CharacterSelectComboBox.ItemsSource = AppSettings.Instance.CharacterList;

                if (AppSettings.Instance.CharacterList.Count > 0)
                {
                    int indexToSelect = Math.Min(_currentCharacterIndex, AppSettings.Instance.CharacterList.Count - 1);
                    if (indexToSelect < 0) indexToSelect = 0;

                    _currentCharacterIndex = indexToSelect;
                    CharacterSelectComboBox.SelectedIndex = indexToSelect;
                    UpdateCharacterUI();
                }
                else
                {
                    _currentCharacterIndex = -1;
                    CharacterNameTextBox.Text = string.Empty;
                    VRMFilePathTextBox.Text = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"キャラクターリスト復元エラー: {ex.Message}");
            }
        }
    }
}
