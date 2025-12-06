using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CocoroConsole.Models.CocoroGhostApi;
using CocoroConsole.Services;

namespace CocoroConsole.Controls
{
    public partial class CharacterPresetManagementControl : UserControl
    {
        private CocoroGhostApiClient? _apiClient;
        private List<CharacterPreset> _presets = new();
        private CharacterPreset? _currentPreset;
        private string? _activePresetId;
        private bool _isLoading;

        public event EventHandler? PresetActivated;

        public CharacterPresetManagementControl()
        {
            InitializeComponent();
        }

        public void Initialize(CocoroGhostApiClient apiClient)
        {
            _apiClient = apiClient;
        }

        public async Task LoadPresetsAsync()
        {
            if (_apiClient == null) return;

            _isLoading = true;
            try
            {
                _presets = await _apiClient.GetCharacterPresetsAsync();
                var settings = await _apiClient.GetSettingsAsync();
                _activePresetId = settings?.ActiveCharacterPresetId;

                CharacterPresetComboBox.ItemsSource = _presets;

                CharacterPreset? selectedPreset = null;
                if (_presets.Count > 0)
                {
                    selectedPreset = _presets.FirstOrDefault(p => p.Id == _activePresetId) ?? _presets[0];
                }

                _isLoading = false;

                if (selectedPreset != null)
                {
                    CharacterPresetComboBox.SelectedItem = selectedPreset;
                    _currentPreset = selectedPreset;
                    LoadPresetToUI(selectedPreset);
                }
                else
                {
                    CharacterPresetComboBox.SelectedItem = null;
                    _currentPreset = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"キャラクタープリセットの読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void CharacterPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (CharacterPresetComboBox.SelectedItem is CharacterPreset preset)
            {
                _currentPreset = preset;
                LoadPresetToUI(preset);
            }
        }

        private void LoadPresetToUI(CharacterPreset preset)
        {
            PresetNameTextBox.Text = preset.Name ?? "";
            MemoryIdTextBox.Text = preset.MemoryId ?? "";
            SystemPromptTextBox.Text = preset.SystemPrompt ?? "";
        }

        private CharacterPreset GetPresetFromUI()
        {
            return new CharacterPreset
            {
                Id = _currentPreset?.Id,
                Name = PresetNameTextBox.Text,
                MemoryId = string.IsNullOrWhiteSpace(MemoryIdTextBox.Text) ? null : MemoryIdTextBox.Text,
                SystemPrompt = SystemPromptTextBox.Text
            };
        }

        private async void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_apiClient == null) return;

            try
            {
                var newPreset = new CharacterPreset
                {
                    Name = "新規プリセット",
                    SystemPrompt = "あなたはアシスタントです。",
                    MemoryId = "default"
                };

                var created = await _apiClient.CreateCharacterPresetAsync(newPreset);
                if (created != null)
                {
                    await LoadPresetsAsync();
                    CharacterPresetComboBox.SelectedItem = _presets.FirstOrDefault(p => p.Id == created.Id);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プリセットの作成に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DuplicatePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_apiClient == null || _currentPreset == null) return;

            try
            {
                var duplicated = GetPresetFromUI();
                duplicated.Id = null;
                duplicated.Name = $"{duplicated.Name} (コピー)";

                var created = await _apiClient.CreateCharacterPresetAsync(duplicated);
                if (created != null)
                {
                    await LoadPresetsAsync();
                    CharacterPresetComboBox.SelectedItem = _presets.FirstOrDefault(p => p.Id == created.Id);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プリセットの複製に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeletePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_apiClient == null || _currentPreset?.Id == null) return;

            if (_presets.Count <= 1)
            {
                MessageBox.Show("最後のプリセットは削除できません。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"プリセット「{_currentPreset.Name}」を削除しますか？",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                await _apiClient.DeleteCharacterPresetAsync(_currentPreset.Id);
                await LoadPresetsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プリセットの削除に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 現在選択されているプリセットを保存する
        /// </summary>
        public async Task SaveCurrentPresetAsync()
        {
            if (_apiClient == null || _currentPreset?.Id == null) return;

            var preset = GetPresetFromUI();
            await _apiClient.UpdateCharacterPresetAsync(_currentPreset.Id, preset);

            // 更新後にリストをリロード
            var selectedId = _currentPreset.Id;
            await LoadPresetsAsync();
            CharacterPresetComboBox.SelectedItem = _presets.FirstOrDefault(p => p.Id == selectedId);
        }

        /// <summary>
        /// 現在選択されているプリセットを有効化する
        /// </summary>
        public async Task ActivateSelectedPresetAsync()
        {
            if (_apiClient == null || _currentPreset?.Id == null) return;

            await _apiClient.ActivateCharacterPresetAsync(_currentPreset.Id);
            _activePresetId = _currentPreset.Id;

            PresetActivated?.Invoke(this, EventArgs.Empty);
        }

        public string? GetActivePresetId() => _activePresetId;
    }
}
