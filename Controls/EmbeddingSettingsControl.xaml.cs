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
    public partial class EmbeddingSettingsControl : UserControl
    {
        private bool _isInitializing = false;
        private List<EmbeddingPreset> _presets = new List<EmbeddingPreset>();
        private int _currentPresetIndex = -1;
        private CocoroGhostApiClient? _apiClient;
        private Func<Task>? _onPresetListChanged;

        public event EventHandler? SettingsChanged;

        public EmbeddingSettingsControl()
        {
            InitializeComponent();
        }

        public void SetApiClient(CocoroGhostApiClient apiClient, Func<Task> onPresetListChanged)
        {
            _apiClient = apiClient;
            _onPresetListChanged = onPresetListChanged;
        }

        public List<EmbeddingPreset> GetAllPresets()
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

            EmbeddingPreset preset = _presets[_currentPresetIndex];
            preset.EmbeddingPresetName = MemoryIdTextBox.Text;
            preset.EmbeddingModelApiKey = string.IsNullOrWhiteSpace(EmbeddingApiKeyPasswordBox.Text) ? null : EmbeddingApiKeyPasswordBox.Text;
            preset.EmbeddingModel = EmbeddingModelTextBox.Text ?? string.Empty;
            preset.EmbeddingBaseUrl = string.IsNullOrWhiteSpace(EmbeddingBaseUrlTextBox.Text) ? null : EmbeddingBaseUrlTextBox.Text;
            preset.EmbeddingDimension = int.TryParse(EmbeddingDimensionTextBox.Text, out int dimension) ? dimension : 3072;
            preset.SimilarEpisodesLimit = int.TryParse(SimilarEpisodesLimitTextBox.Text, out int limit) ? limit : 5;
        }

        public void LoadSettings(EmbeddingPreset? preset)
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
                PresetSelectComboBox.Items.Add(preset.EmbeddingPresetName);
                PresetSelectComboBox.SelectedIndex = 0;
                _currentPresetIndex = 0;

                LoadPresetToUI(preset);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public void LoadSettingsList(List<EmbeddingPreset>? presets, string? activePresetId = null)
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
                foreach (EmbeddingPreset preset in presets)
                {
                    PresetSelectComboBox.Items.Add(preset.EmbeddingPresetName);
                }
                var activeIndex = 0;
                if (!string.IsNullOrWhiteSpace(activePresetId))
                {
                    activeIndex = _presets.FindIndex(p => string.Equals(p.EmbeddingPresetId, activePresetId, StringComparison.OrdinalIgnoreCase));
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

        private void LoadPresetToUI(EmbeddingPreset preset)
        {
            MemoryIdTextBox.Text = preset.EmbeddingPresetName ?? string.Empty;
            EmbeddingApiKeyPasswordBox.Text = preset.EmbeddingModelApiKey ?? string.Empty;
            EmbeddingModelTextBox.Text = preset.EmbeddingModel ?? string.Empty;
            EmbeddingBaseUrlTextBox.Text = preset.EmbeddingBaseUrl ?? string.Empty;
            EmbeddingDimensionTextBox.Text = preset.EmbeddingDimension.ToString();
            SimilarEpisodesLimitTextBox.Text = preset.SimilarEpisodesLimit.ToString();
        }

        public EmbeddingPreset? GetSettings()
        {
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                return null;
            }

            EmbeddingPreset currentPreset = _presets[_currentPresetIndex];

            EmbeddingPreset preset = new EmbeddingPreset
            {
                EmbeddingPresetId = currentPreset.EmbeddingPresetId,
                EmbeddingPresetName = MemoryIdTextBox.Text,
                EmbeddingModelApiKey = string.IsNullOrWhiteSpace(EmbeddingApiKeyPasswordBox.Text) ? null : EmbeddingApiKeyPasswordBox.Text,
                EmbeddingModel = EmbeddingModelTextBox.Text ?? string.Empty,
                EmbeddingBaseUrl = string.IsNullOrWhiteSpace(EmbeddingBaseUrlTextBox.Text) ? null : EmbeddingBaseUrlTextBox.Text,
                EmbeddingDimension = int.TryParse(EmbeddingDimensionTextBox.Text, out int dimension) ? dimension : 3072,
                SimilarEpisodesLimit = int.TryParse(SimilarEpisodesLimitTextBox.Text, out int limit) ? limit : 5
            };

            return preset;
        }

        private void ClearSettings()
        {
            MemoryIdTextBox.Text = string.Empty;
            EmbeddingApiKeyPasswordBox.Text = string.Empty;
            EmbeddingModelTextBox.Text = string.Empty;
            EmbeddingBaseUrlTextBox.Text = string.Empty;
            EmbeddingDimensionTextBox.Text = "3072";
            SimilarEpisodesLimitTextBox.Text = "5";
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

            EmbeddingPreset newPreset = new EmbeddingPreset
            {
                EmbeddingPresetId = Guid.NewGuid().ToString(),
                EmbeddingPresetName = GenerateNewPresetName(),
                EmbeddingModelApiKey = null,
                EmbeddingModel = string.Empty,
                EmbeddingBaseUrl = null,
                EmbeddingDimension = 3072,
                SimilarEpisodesLimit = 5
            };

            _presets.Add(newPreset);
            PresetSelectComboBox.Items.Add(newPreset.EmbeddingPresetName);
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

            EmbeddingPreset source = _presets[_currentPresetIndex];
            EmbeddingPreset duplicate = new EmbeddingPreset
            {
                EmbeddingPresetId = Guid.NewGuid().ToString(),
                EmbeddingPresetName = GenerateDuplicatePresetName(source.EmbeddingPresetName),
                EmbeddingModelApiKey = source.EmbeddingModelApiKey,
                EmbeddingModel = source.EmbeddingModel,
                EmbeddingBaseUrl = source.EmbeddingBaseUrl,
                EmbeddingDimension = source.EmbeddingDimension,
                SimilarEpisodesLimit = source.SimilarEpisodesLimit
            };

            _presets.Add(duplicate);
            PresetSelectComboBox.Items.Add(duplicate.EmbeddingPresetName);
            PresetSelectComboBox.SelectedIndex = _presets.Count - 1;

            await SavePresetsToApiAsync();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void EmbeddingApiKeyPasteOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(EmbeddingApiKeyPasswordBox);
        }

        private void EmbeddingApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(EmbeddingApiKeyPasswordBox);
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

            string presetName = _presets[_currentPresetIndex].EmbeddingPresetName ?? "不明";
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

            while (_presets.Any(p => p.EmbeddingPresetName == name))
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

            while (_presets.Any(p => p.EmbeddingPresetName == name))
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
                if (sender == MemoryIdTextBox && _currentPresetIndex >= 0 && _currentPresetIndex < _presets.Count)
                {
                    // プリセットの名前を更新
                    _presets[_currentPresetIndex].EmbeddingPresetName = MemoryIdTextBox.Text;

                    // ComboBoxを更新
                    var currentIndex = _currentPresetIndex;
                    PresetSelectComboBox.SelectionChanged -= PresetSelectComboBox_SelectionChanged;
                    PresetSelectComboBox.Items.Clear();
                    foreach (var preset in _presets)
                    {
                        PresetSelectComboBox.Items.Add(preset.EmbeddingPresetName);
                    }
                    PresetSelectComboBox.SelectedIndex = currentIndex;
                    PresetSelectComboBox.SelectionChanged += PresetSelectComboBox_SelectionChanged;
                }
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

            return _presets[_currentPresetIndex].EmbeddingPresetId;
        }
    }
}
