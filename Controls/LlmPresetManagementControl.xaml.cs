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
    public partial class LlmPresetManagementControl : UserControl
    {
        private CocoroGhostApiClient? _apiClient;
        private List<LlmPreset> _presets = new();
        private LlmPreset? _currentPreset;
        private string? _activePresetId;
        private bool _isLoading;

        public event EventHandler? PresetActivated;

        public LlmPresetManagementControl()
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
                _presets = await _apiClient.GetLlmPresetsAsync();
                var settings = await _apiClient.GetSettingsAsync();
                _activePresetId = settings?.ActiveLlmPresetId;

                LlmPresetComboBox.ItemsSource = _presets;

                LlmPreset? selectedPreset = null;
                if (_presets.Count > 0)
                {
                    selectedPreset = _presets.FirstOrDefault(p => p.Id == _activePresetId) ?? _presets[0];
                }

                _isLoading = false;

                if (selectedPreset != null)
                {
                    LlmPresetComboBox.SelectedItem = selectedPreset;
                    _currentPreset = selectedPreset;
                    LoadPresetToUI(selectedPreset);
                }
                else
                {
                    LlmPresetComboBox.SelectedItem = null;
                    _currentPreset = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"LLMプリセットの読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void LlmPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            if (LlmPresetComboBox.SelectedItem is LlmPreset preset)
            {
                _currentPreset = preset;
                LoadPresetToUI(preset);
            }
        }

        private void LoadPresetToUI(LlmPreset preset)
        {
            PresetNameTextBox.Text = preset.Name ?? "";
            LlmModelTextBox.Text = preset.LlmModel ?? "";
            ApiKeyTextBox.Text = preset.LlmApiKey ?? "";
            BaseUrlTextBox.Text = preset.LlmBaseUrl ?? "";
            ReasoningEffortTextBox.Text = preset.ReasoningEffort ?? "";
            MaxTurnsWindowTextBox.Text = preset.MaxTurnsWindow?.ToString() ?? "";
            MaxTokensTextBox.Text = preset.MaxTokens?.ToString() ?? "";
            MaxTokensVisionTextBox.Text = preset.MaxTokensVision?.ToString() ?? "";

            // 画像設定
            ImageModelTextBox.Text = preset.ImageModel ?? "";
            ImageApiKeyTextBox.Text = preset.ImageModelApiKey ?? "";
            ImageBaseUrlTextBox.Text = preset.ImageLlmBaseUrl ?? "";
            ImageTimeoutTextBox.Text = preset.ImageTimeoutSeconds?.ToString() ?? "";

            // Embedding設定
            EmbeddingModelTextBox.Text = preset.EmbeddingModel ?? "";
            EmbeddingApiKeyTextBox.Text = preset.EmbeddingApiKey ?? "";
            EmbeddingBaseUrlTextBox.Text = preset.EmbeddingBaseUrl ?? "";
            EmbeddingDimensionTextBox.Text = preset.EmbeddingDimension?.ToString() ?? "";
            SimilarEpisodesLimitTextBox.Text = preset.SimilarEpisodesLimit?.ToString() ?? "";

            // アクティブ表示
            UpdateActiveStatus(preset);
        }

        private void UpdateActiveStatus(LlmPreset preset)
        {
            bool isActive = preset.Id == _activePresetId;
            ActivatePresetButton.IsEnabled = !isActive;
            ActivatePresetButton.Content = isActive ? "有効中" : "このプリセットを有効化";
        }

        private LlmPreset GetPresetFromUI()
        {
            var preset = new LlmPreset
            {
                Id = _currentPreset?.Id,
                Name = PresetNameTextBox.Text,
                LlmModel = LlmModelTextBox.Text,
                LlmApiKey = ApiKeyTextBox.Text,
                LlmBaseUrl = string.IsNullOrWhiteSpace(BaseUrlTextBox.Text) ? null : BaseUrlTextBox.Text,
                ReasoningEffort = string.IsNullOrWhiteSpace(ReasoningEffortTextBox.Text) ? null : ReasoningEffortTextBox.Text,
                ImageModel = string.IsNullOrWhiteSpace(ImageModelTextBox.Text) ? null : ImageModelTextBox.Text,
                ImageModelApiKey = string.IsNullOrWhiteSpace(ImageApiKeyTextBox.Text) ? null : ImageApiKeyTextBox.Text,
                ImageLlmBaseUrl = string.IsNullOrWhiteSpace(ImageBaseUrlTextBox.Text) ? null : ImageBaseUrlTextBox.Text,
                EmbeddingModel = string.IsNullOrWhiteSpace(EmbeddingModelTextBox.Text) ? null : EmbeddingModelTextBox.Text,
                EmbeddingApiKey = string.IsNullOrWhiteSpace(EmbeddingApiKeyTextBox.Text) ? null : EmbeddingApiKeyTextBox.Text,
                EmbeddingBaseUrl = string.IsNullOrWhiteSpace(EmbeddingBaseUrlTextBox.Text) ? null : EmbeddingBaseUrlTextBox.Text,
            };

            if (int.TryParse(MaxTurnsWindowTextBox.Text, out int maxTurns))
                preset.MaxTurnsWindow = maxTurns;
            if (int.TryParse(MaxTokensTextBox.Text, out int maxTokens))
                preset.MaxTokens = maxTokens;
            if (int.TryParse(MaxTokensVisionTextBox.Text, out int maxTokensVision))
                preset.MaxTokensVision = maxTokensVision;
            if (int.TryParse(ImageTimeoutTextBox.Text, out int imageTimeout))
                preset.ImageTimeoutSeconds = imageTimeout;
            if (int.TryParse(EmbeddingDimensionTextBox.Text, out int embDim))
                preset.EmbeddingDimension = embDim;
            if (int.TryParse(SimilarEpisodesLimitTextBox.Text, out int simLimit))
                preset.SimilarEpisodesLimit = simLimit;

            return preset;
        }

        private async void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_apiClient == null) return;

            try
            {
                var newPreset = new LlmPreset
                {
                    Name = "新規プリセット",
                    LlmModel = "openai/gpt-4o-mini"
                };

                var created = await _apiClient.CreateLlmPresetAsync(newPreset);
                if (created != null)
                {
                    await LoadPresetsAsync();
                    LlmPresetComboBox.SelectedItem = _presets.FirstOrDefault(p => p.Id == created.Id);
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

                var created = await _apiClient.CreateLlmPresetAsync(duplicated);
                if (created != null)
                {
                    await LoadPresetsAsync();
                    LlmPresetComboBox.SelectedItem = _presets.FirstOrDefault(p => p.Id == created.Id);
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
                await _apiClient.DeleteLlmPresetAsync(_currentPreset.Id);
                await LoadPresetsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プリセットの削除に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SavePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_apiClient == null || _currentPreset?.Id == null) return;

            try
            {
                var preset = GetPresetFromUI();
                await _apiClient.UpdateLlmPresetAsync(_currentPreset.Id, preset);

                // 更新後にリストをリロード
                var selectedId = _currentPreset.Id;
                await LoadPresetsAsync();
                LlmPresetComboBox.SelectedItem = _presets.FirstOrDefault(p => p.Id == selectedId);

                MessageBox.Show("保存しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ActivatePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_apiClient == null || _currentPreset?.Id == null) return;

            try
            {
                await _apiClient.ActivateLlmPresetAsync(_currentPreset.Id);
                _activePresetId = _currentPreset.Id;
                UpdateActiveStatus(_currentPreset);

                RestartNoticeText.Visibility = Visibility.Visible;
                PresetActivated?.Invoke(this, EventArgs.Empty);

                MessageBox.Show(
                    "プリセットを有効化しました。\n変更を反映するには cocoro_ghost を再起動してください。",
                    "プリセット有効化",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"有効化に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApiKeyPasteButton_Click(object sender, RoutedEventArgs e)
        {
            PasteFromClipboard(ApiKeyTextBox);
        }

        private void ImageApiKeyPasteButton_Click(object sender, RoutedEventArgs e)
        {
            PasteFromClipboard(ImageApiKeyTextBox);
        }

        private void EmbeddingApiKeyPasteButton_Click(object sender, RoutedEventArgs e)
        {
            PasteFromClipboard(EmbeddingApiKeyTextBox);
        }

        private void PasteFromClipboard(TextBox textBox)
        {
            if (Clipboard.ContainsText())
            {
                textBox.Text = Clipboard.GetText();
            }
        }

        public string? GetActivePresetId() => _activePresetId;
    }
}
