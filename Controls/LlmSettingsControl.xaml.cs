using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CocoroConsole.Models.CocoroGhostApi;
using CocoroConsole.Services;
using CocoroConsole.Utilities;

namespace CocoroConsole.Controls
{
    public partial class LlmSettingsControl : UserControl
    {
        private bool _isInitializing = false;
        private List<LlmPreset> _presets = new List<LlmPreset>();
        private int _currentPresetIndex = -1;
        private CocoroGhostApiClient? _apiClient;
        private Func<Task>? _onPresetListChanged;

        public event EventHandler? SettingsChanged;

        public LlmSettingsControl()
        {
            InitializeComponent();
        }

        public void SetApiClient(CocoroGhostApiClient apiClient, Func<Task> onPresetListChanged)
        {
            _apiClient = apiClient;
            _onPresetListChanged = onPresetListChanged;
        }

        public List<LlmPreset> GetAllPresets()
        {
            // 現在のUI値を現在のプリセットに反映
            if (_currentPresetIndex >= 0 && _currentPresetIndex < _presets.Count)
            {
                SaveCurrentUIToPreset();
            }
            return _presets.ToList();
        }

        private void SaveCurrentUIToPreset()
        {
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count) return;

            LlmPreset preset = _presets[_currentPresetIndex];
            preset.LlmPresetName = PresetNameTextBox.Text;
            preset.LlmApiKey = LlmApiKeyPasswordBox.Text ?? string.Empty;
            preset.LlmModel = LlmModelTextBox.Text ?? string.Empty;
            preset.LlmBaseUrl = string.IsNullOrWhiteSpace(LlmBaseUrlTextBox.Text) ? null : LlmBaseUrlTextBox.Text;
            preset.MaxTurnsWindow = int.TryParse(MaxTurnsWindowTextBox.Text, out int maxTurns) ? maxTurns : 50;
            preset.MaxTokens = int.TryParse(MaxTokensTextBox.Text, out int maxTokens) ? maxTokens : 4096;
            preset.ReasoningEffort = !string.IsNullOrWhiteSpace(ReasoningEffortTextBox.Text) ? ReasoningEffortTextBox.Text : null;
            preset.ImageModelApiKey = string.IsNullOrWhiteSpace(VisionApiKeyPasswordBox.Text) ? null : VisionApiKeyPasswordBox.Text;
            preset.ImageModel = VisionModelTextBox.Text ?? string.Empty;
            preset.ImageLlmBaseUrl = string.IsNullOrWhiteSpace(VisionBaseUrlTextBox.Text) ? null : VisionBaseUrlTextBox.Text;
            preset.MaxTokensVision = int.TryParse(MaxTokensVisionTextBox.Text, out int maxTokensVision) ? maxTokensVision : 4096;
            preset.ImageTimeoutSeconds = int.TryParse(ImageTimeoutSecondsTextBox.Text, out int imageTimeout) ? imageTimeout : 60;
        }

        public void LoadSettings(LlmPreset? preset)
        {
            _isInitializing = true;

            try
            {
                _presets.Clear();
                PresetSelectComboBox.Items.Clear();

                if (preset == null)
                {
                    ClearSettings();
                    _currentPresetIndex = -1;
                    return;
                }

                // 単一プリセットをリストに追加
                _presets.Add(preset);
                PresetSelectComboBox.Items.Add(preset.LlmPresetName);
                PresetSelectComboBox.SelectedIndex = 0;
                _currentPresetIndex = 0;

                LoadPresetToUI(preset);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public void LoadSettingsList(List<LlmPreset>? presets, string? activePresetId = null)
        {
            _isInitializing = true;

            try
            {
                _presets.Clear();
                PresetSelectComboBox.Items.Clear();

                if (presets == null || presets.Count == 0)
                {
                    ClearSettings();
                    _currentPresetIndex = -1;
                    return;
                }

                _presets.AddRange(presets);
                foreach (LlmPreset preset in presets)
                {
                    PresetSelectComboBox.Items.Add(preset.LlmPresetName);
                }

                var activeIndex = 0;
                if (!string.IsNullOrWhiteSpace(activePresetId))
                {
                    activeIndex = _presets.FindIndex(p => string.Equals(p.LlmPresetId, activePresetId, StringComparison.OrdinalIgnoreCase));
                    if (activeIndex < 0)
                    {
                        activeIndex = 0;
                    }
                }

                PresetSelectComboBox.SelectedIndex = activeIndex;
                _currentPresetIndex = activeIndex;

                LoadPresetToUI(_presets[activeIndex]);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void LoadPresetToUI(LlmPreset preset)
        {
            PresetNameTextBox.Text = preset.LlmPresetName ?? string.Empty;
            LlmApiKeyPasswordBox.Text = preset.LlmApiKey ?? string.Empty;
            LlmModelTextBox.Text = preset.LlmModel ?? string.Empty;
            LlmBaseUrlTextBox.Text = preset.LlmBaseUrl ?? string.Empty;
            MaxTurnsWindowTextBox.Text = preset.MaxTurnsWindow.ToString();
            MaxTokensTextBox.Text = preset.MaxTokens.ToString();

            // Reasoning Effort
            ReasoningEffortTextBox.Text = preset.ReasoningEffort ?? string.Empty;

            // 画像認識LLM設定
            VisionApiKeyPasswordBox.Text = preset.ImageModelApiKey ?? string.Empty;
            VisionModelTextBox.Text = preset.ImageModel ?? string.Empty;
            VisionBaseUrlTextBox.Text = preset.ImageLlmBaseUrl ?? string.Empty;
            MaxTokensVisionTextBox.Text = preset.MaxTokensVision.ToString();
            ImageTimeoutSecondsTextBox.Text = preset.ImageTimeoutSeconds.ToString();
        }

        public LlmPreset? GetSettings()
        {
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                return null;
            }

            LlmPreset currentPreset = _presets[_currentPresetIndex];

            LlmPreset preset = new LlmPreset
            {
                LlmPresetId = currentPreset.LlmPresetId,
                LlmPresetName = PresetNameTextBox.Text,
                LlmApiKey = LlmApiKeyPasswordBox.Text ?? string.Empty,
                LlmModel = LlmModelTextBox.Text ?? string.Empty,
                LlmBaseUrl = string.IsNullOrWhiteSpace(LlmBaseUrlTextBox.Text) ? null : LlmBaseUrlTextBox.Text,
                MaxTurnsWindow = int.TryParse(MaxTurnsWindowTextBox.Text, out int maxTurns) ? maxTurns : 50,
                MaxTokens = int.TryParse(MaxTokensTextBox.Text, out int maxTokens) ? maxTokens : 4096,
                ImageModelApiKey = string.IsNullOrWhiteSpace(VisionApiKeyPasswordBox.Text) ? null : VisionApiKeyPasswordBox.Text,
                ImageModel = VisionModelTextBox.Text ?? string.Empty,
                ImageLlmBaseUrl = string.IsNullOrWhiteSpace(VisionBaseUrlTextBox.Text) ? null : VisionBaseUrlTextBox.Text,
                MaxTokensVision = int.TryParse(MaxTokensVisionTextBox.Text, out int maxTokensVision) ? maxTokensVision : 4096,
                ImageTimeoutSeconds = int.TryParse(ImageTimeoutSecondsTextBox.Text, out int imageTimeout) ? imageTimeout : 60
            };

            // Reasoning Effort
            preset.ReasoningEffort = !string.IsNullOrWhiteSpace(ReasoningEffortTextBox.Text) ? ReasoningEffortTextBox.Text : null;

            return preset;
        }

        private void ClearSettings()
        {
            PresetNameTextBox.Text = string.Empty;
            LlmApiKeyPasswordBox.Text = string.Empty;
            LlmModelTextBox.Text = string.Empty;
            LlmBaseUrlTextBox.Text = string.Empty;
            ReasoningEffortTextBox.Text = string.Empty;
            MaxTurnsWindowTextBox.Text = "50";
            MaxTokensTextBox.Text = "4096";
            VisionApiKeyPasswordBox.Text = string.Empty;
            VisionModelTextBox.Text = string.Empty;
            VisionBaseUrlTextBox.Text = string.Empty;
            MaxTokensVisionTextBox.Text = "4096";
            ImageTimeoutSecondsTextBox.Text = "60";
        }

        private void PresetSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            // 現在のUI値を保存
            SaveCurrentUIToPreset();

            int selectedIndex = PresetSelectComboBox.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < _presets.Count)
            {
                _currentPresetIndex = selectedIndex;
                _isInitializing = true;
                try
                {
                    LoadPresetToUI(_presets[selectedIndex]);
                }
                finally
                {
                    _isInitializing = false;
                }
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentUIToPreset();

            LlmPreset newPreset = new LlmPreset
            {
                LlmPresetId = Guid.NewGuid().ToString(),
                LlmPresetName = GenerateNewPresetName(),
                LlmApiKey = string.Empty,
                LlmModel = string.Empty,
                ReasoningEffort = null,
                LlmBaseUrl = null,
                MaxTurnsWindow = 50,
                MaxTokens = 4096,
                ImageModelApiKey = null,
                ImageModel = string.Empty,
                ImageLlmBaseUrl = null,
                MaxTokensVision = 4096,
                ImageTimeoutSeconds = 60
            };

            _presets.Add(newPreset);
            PresetSelectComboBox.Items.Add(newPreset.LlmPresetName);
            PresetSelectComboBox.SelectedIndex = _presets.Count - 1;

            await SavePresetsToApiAsync();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void DuplicatePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                MessageBox.Show("複製するプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveCurrentUIToPreset();

            LlmPreset source = _presets[_currentPresetIndex];
            LlmPreset duplicate = new LlmPreset
            {
                LlmPresetId = Guid.NewGuid().ToString(),
                LlmPresetName = GenerateDuplicatePresetName(source.LlmPresetName),
                LlmApiKey = source.LlmApiKey,
                LlmModel = source.LlmModel,
                LlmBaseUrl = source.LlmBaseUrl,
                ReasoningEffort = source.ReasoningEffort,
                MaxTurnsWindow = source.MaxTurnsWindow,
                MaxTokens = source.MaxTokens,
                ImageModelApiKey = source.ImageModelApiKey,
                ImageModel = source.ImageModel,
                ImageLlmBaseUrl = source.ImageLlmBaseUrl,
                MaxTokensVision = source.MaxTokensVision,
                ImageTimeoutSeconds = source.ImageTimeoutSeconds
            };

            _presets.Add(duplicate);
            PresetSelectComboBox.Items.Add(duplicate.LlmPresetName);
            PresetSelectComboBox.SelectedIndex = _presets.Count - 1;

            await SavePresetsToApiAsync();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void LlmApiKeyPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(LlmApiKeyPasswordBox);
        }

        private void LlmApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(LlmApiKeyPasswordBox);
        }

        private void VisionApiKeyPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(VisionApiKeyPasswordBox);
        }

        private void VisionApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(VisionApiKeyPasswordBox);
        }

        private async void DeletePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                MessageBox.Show("削除するプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_presets.Count <= 1)
            {
                MessageBox.Show("最後のプリセットは削除できません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string presetName = _presets[_currentPresetIndex].LlmPresetName;
            MessageBoxResult result = MessageBox.Show(
                $"プリセット「{presetName}」を削除しますか？",
                "削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            _isInitializing = true;
            try
            {
                _presets.RemoveAt(_currentPresetIndex);
                PresetSelectComboBox.Items.RemoveAt(_currentPresetIndex);

                int newIndex = Math.Min(_currentPresetIndex, _presets.Count - 1);
                if (newIndex >= 0)
                {
                    PresetSelectComboBox.SelectedIndex = newIndex;
                    _currentPresetIndex = newIndex;
                    LoadPresetToUI(_presets[newIndex]);
                }
            }
            finally
            {
                _isInitializing = false;
            }

            await SavePresetsToApiAsync();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private string GenerateNewPresetName()
        {
            int counter = 1;
            string baseName = "新規プリセット";
            string name = baseName;

            while (_presets.Any(p => p.LlmPresetName == name))
            {
                counter++;
                name = $"{baseName} {counter}";
            }

            return name;
        }

        private string GenerateDuplicatePresetName(string sourceName)
        {
            int counter = 1;
            string baseName = $"{sourceName} (コピー)";
            string name = baseName;

            while (_presets.Any(p => p.LlmPresetName == name))
            {
                counter++;
                name = $"{baseName} {counter}";
            }

            return name;
        }

        private async Task SavePresetsToApiAsync()
        {
            if (_apiClient == null || _onPresetListChanged == null) return;

            try
            {
                await _onPresetListChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プリセットの保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing)
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnSettingChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                // 名前変更時はプリセットリストとComboBoxの表示を更新
                if (sender == PresetNameTextBox && _currentPresetIndex >= 0 && _currentPresetIndex < _presets.Count)
                {
                    // プリセットの名前を更新
                    _presets[_currentPresetIndex].LlmPresetName = PresetNameTextBox.Text;

                    // ComboBoxを更新
                    var currentIndex = _currentPresetIndex;
                    PresetSelectComboBox.SelectionChanged -= PresetSelectComboBox_SelectionChanged;
                    PresetSelectComboBox.Items.Clear();
                    foreach (var preset in _presets)
                    {
                        PresetSelectComboBox.Items.Add(preset.LlmPresetName);
                    }
                    PresetSelectComboBox.SelectedIndex = currentIndex;
                    PresetSelectComboBox.SelectionChanged += PresetSelectComboBox_SelectionChanged;
                }
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnSettingChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Utilities.UIHelper.HandleHyperlinkNavigation(e);
        }

        public string? GetActivePresetId()
        {
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                return null;
            }

            return _presets[_currentPresetIndex].LlmPresetId;
        }
    }
}
