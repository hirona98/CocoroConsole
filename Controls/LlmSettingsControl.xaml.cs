using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    public partial class LlmSettingsControl : UserControl
    {
        private const string DefaultGenerationModel = "openrouter/google/gemini-3.1-flash-lite-preview";
        private const int DefaultRecentTurnLimit = 30;
        private const int DefaultRecentTurnMinutes = 30;
        private const int DefaultMaxOutputTokens = 4000;
        private const double DefaultTimeoutSeconds = 90;

        // 入力途中の値を保持し、プリセットを切り替えても編集内容を失わないようにする。
        private sealed class ModelPresetEditorItem
        {
            public string ModelPresetId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Model { get; set; } = string.Empty;
            public string ApiBase { get; set; } = string.Empty;
            public string ApiKey { get; set; } = string.Empty;
            public string ReasoningEffort { get; set; } = string.Empty;
            public string MaxOutputTokensText { get; set; } = string.Empty;
            public string TimeoutSecondsText { get; set; } = string.Empty;
            public bool WebSearchEnabled { get; set; }
            public string RecentTurnLimitText { get; set; } = string.Empty;
            public string RecentTurnMinutesText { get; set; } = string.Empty;
        }

        private readonly List<ModelPresetEditorItem> _presets = new();
        private bool _isInitializing;
        private int _currentPresetIndex = -1;

        public event EventHandler? SettingsChanged;

        public LlmSettingsControl()
        {
            _isInitializing = true;
            try
            {
                InitializeComponent();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public bool IsUseLlm
        {
            get => IsUseLLMCheckBox.IsChecked ?? false;
            set
            {
                _isInitializing = true;
                try
                {
                    IsUseLLMCheckBox.IsChecked = value;
                }
                finally
                {
                    _isInitializing = false;
                }
            }
        }

        public void LoadSettingsList(List<OtomeKairoModelPresetDefinition>? presets, string? activePresetId = null)
        {
            _isInitializing = true;
            try
            {
                _presets.Clear();

                if (presets == null || presets.Count == 0)
                {
                    _currentPresetIndex = -1;
                    PresetSelectComboBox.Items.Clear();
                    ClearUi();
                    return;
                }

                _presets.AddRange(presets.Select(ToEditorItem));
                _currentPresetIndex = ResolveActiveIndex(activePresetId);
                RefreshComboBoxItems();
                LoadPresetToUi(_presets[_currentPresetIndex]);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public List<OtomeKairoModelPresetDefinition> GetAllPresets()
        {
            SyncCurrentPresetFromUi();
            return _presets.Select(ToDefinition).ToList();
        }

        public string? GetActivePresetId()
        {
            return GetCurrentPreset()?.ModelPresetId;
        }

        public string GetPreferredApiKeyForEmbeddingPaste()
        {
            SyncCurrentPresetFromUi();
            return GetCurrentPreset()?.ApiKey.Trim() ?? string.Empty;
        }

        private void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            SyncCurrentPresetFromUi();

            var item = CreateDefaultPreset(
                $"model_preset:{Guid.NewGuid():N}",
                GenerateUniqueName("新規モデルプリセット"));

            SelectNewPreset(item);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DuplicatePresetButton_Click(object sender, RoutedEventArgs e)
        {
            var current = GetCurrentPreset();
            if (current == null)
            {
                MessageBox.Show("複製するモデルプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SyncCurrentPresetFromUi();
            var item = ClonePreset(current);
            item.ModelPresetId = $"model_preset:{Guid.NewGuid():N}";
            item.DisplayName = GenerateUniqueName($"{current.DisplayName} (コピー)");

            SelectNewPreset(item);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentPreset() == null)
            {
                MessageBox.Show("削除するモデルプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_presets.Count == 1)
            {
                MessageBox.Show("最後のモデルプリセットは削除できません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isInitializing = true;
            try
            {
                _presets.RemoveAt(_currentPresetIndex);
                _currentPresetIndex = Math.Min(_currentPresetIndex, _presets.Count - 1);
                RefreshComboBoxItems();
                LoadPresetToUi(_presets[_currentPresetIndex]);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void PresetSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            // 選択変更前のプリセットへ、画面に残っている値を書き戻す。
            SyncCurrentPresetFromUi();
            var selectedIndex = PresetSelectComboBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _presets.Count)
            {
                return;
            }

            _isInitializing = true;
            try
            {
                _currentPresetIndex = selectedIndex;
                LoadPresetToUi(_presets[selectedIndex]);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnSettingChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SyncCurrentPresetFromUi();
            if (sender == PresetNameTextBox)
            {
                RefreshComboBoxItems();
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnCheckBoxChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SyncCurrentPresetFromUi();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void IsUseLLMCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing)
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(ApiKeyTextBox);
        }

        private void ApiKeyPasteButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(ApiKeyTextBox);
        }

        private void SelectNewPreset(ModelPresetEditorItem item)
        {
            _isInitializing = true;
            try
            {
                _presets.Add(item);
                _currentPresetIndex = _presets.Count - 1;
                RefreshComboBoxItems();
                LoadPresetToUi(item);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private ModelPresetEditorItem? GetCurrentPreset()
        {
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                return null;
            }

            return _presets[_currentPresetIndex];
        }

        private void SyncCurrentPresetFromUi()
        {
            if (_isInitializing)
            {
                return;
            }

            var current = GetCurrentPreset();
            if (current == null)
            {
                return;
            }

            current.DisplayName = PresetNameTextBox.Text;
            current.Model = ModelTextBox.Text;
            current.ApiBase = ApiBaseTextBox.Text;
            current.ApiKey = ApiKeyTextBox.Text;
            current.ReasoningEffort = ReasoningEffortTextBox.Text;
            current.MaxOutputTokensText = MaxOutputTokensTextBox.Text;
            current.TimeoutSecondsText = TimeoutSecondsTextBox.Text;
            current.WebSearchEnabled = WebSearchCheckBox.IsChecked ?? false;
            current.RecentTurnLimitText = RecentTurnLimitTextBox.Text;
            current.RecentTurnMinutesText = RecentTurnMinutesTextBox.Text;
        }

        private void LoadPresetToUi(ModelPresetEditorItem item)
        {
            PresetNameTextBox.Text = item.DisplayName;
            ModelTextBox.Text = item.Model;
            ApiBaseTextBox.Text = item.ApiBase;
            ApiKeyTextBox.Text = item.ApiKey;
            ReasoningEffortTextBox.Text = item.ReasoningEffort;
            MaxOutputTokensTextBox.Text = item.MaxOutputTokensText;
            TimeoutSecondsTextBox.Text = item.TimeoutSecondsText;
            WebSearchCheckBox.IsChecked = item.WebSearchEnabled;
            RecentTurnLimitTextBox.Text = item.RecentTurnLimitText;
            RecentTurnMinutesTextBox.Text = item.RecentTurnMinutesText;
        }

        private void ClearUi()
        {
            PresetNameTextBox.Text = string.Empty;
            ModelTextBox.Text = string.Empty;
            ApiBaseTextBox.Text = string.Empty;
            ApiKeyTextBox.Text = string.Empty;
            ReasoningEffortTextBox.Text = string.Empty;
            MaxOutputTokensTextBox.Text = string.Empty;
            TimeoutSecondsTextBox.Text = string.Empty;
            WebSearchCheckBox.IsChecked = false;
            RecentTurnLimitTextBox.Text = string.Empty;
            RecentTurnMinutesTextBox.Text = string.Empty;
        }

        private void RefreshComboBoxItems()
        {
            var wasInitializing = _isInitializing;
            _isInitializing = true;
            try
            {
                PresetSelectComboBox.Items.Clear();
                foreach (var preset in _presets)
                {
                    PresetSelectComboBox.Items.Add(preset.DisplayName);
                }

                PresetSelectComboBox.SelectedIndex = _currentPresetIndex;
            }
            finally
            {
                _isInitializing = wasInitializing;
            }
        }

        private int ResolveActiveIndex(string? activePresetId)
        {
            if (!string.IsNullOrWhiteSpace(activePresetId))
            {
                var index = _presets.FindIndex(preset =>
                    string.Equals(preset.ModelPresetId, activePresetId, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    return index;
                }
            }

            return 0;
        }

        private string GenerateUniqueName(string baseName)
        {
            var names = new HashSet<string>(_presets.Select(preset => preset.DisplayName), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(baseName))
            {
                return baseName;
            }

            for (var suffix = 2; ; suffix++)
            {
                var candidate = $"{baseName} {suffix}";
                if (!names.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        private static ModelPresetEditorItem ToEditorItem(OtomeKairoModelPresetDefinition definition)
        {
            var promptWindow = definition.PromptWindow ?? new OtomeKairoPromptWindowDefinition();
            return new ModelPresetEditorItem
            {
                ModelPresetId = definition.ModelPresetId,
                DisplayName = definition.DisplayName,
                Model = definition.Model,
                ApiBase = definition.ApiBase ?? string.Empty,
                ApiKey = definition.ApiKey,
                ReasoningEffort = definition.ReasoningEffort ?? string.Empty,
                MaxOutputTokensText = definition.MaxOutputTokens.ToString(CultureInfo.InvariantCulture),
                TimeoutSecondsText = definition.TimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                WebSearchEnabled = definition.WebSearchEnabled,
                RecentTurnLimitText = promptWindow.RecentTurnLimit.ToString(CultureInfo.InvariantCulture),
                RecentTurnMinutesText = promptWindow.RecentTurnMinutes.ToString(CultureInfo.InvariantCulture),
            };
        }

        private static OtomeKairoModelPresetDefinition ToDefinition(ModelPresetEditorItem item)
        {
            var displayName = RequireText(item.DisplayName, "プリセット名");
            var model = RequireText(item.Model, "モデル");

            return new OtomeKairoModelPresetDefinition
            {
                ModelPresetId = item.ModelPresetId,
                DisplayName = displayName,
                Model = model,
                ApiBase = EmptyToNull(item.ApiBase),
                ApiKey = item.ApiKey.Trim(),
                ReasoningEffort = EmptyToNull(item.ReasoningEffort),
                MaxOutputTokens = ParsePositiveInt(item.MaxOutputTokensText, "最大出力トークン"),
                TimeoutSeconds = ParsePositiveDouble(item.TimeoutSecondsText, "タイムアウト（秒）"),
                WebSearchEnabled = item.WebSearchEnabled,
                PromptWindow = new OtomeKairoPromptWindowDefinition
                {
                    RecentTurnLimit = ParsePositiveInt(item.RecentTurnLimitText, "直近会話の件数"),
                    RecentTurnMinutes = ParsePositiveInt(item.RecentTurnMinutesText, "直近会話の時間（分）"),
                },
            };
        }

        private static ModelPresetEditorItem CreateDefaultPreset(string id, string displayName)
        {
            return new ModelPresetEditorItem
            {
                ModelPresetId = id,
                DisplayName = displayName,
                Model = DefaultGenerationModel,
                MaxOutputTokensText = DefaultMaxOutputTokens.ToString(CultureInfo.InvariantCulture),
                TimeoutSecondsText = DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                RecentTurnLimitText = DefaultRecentTurnLimit.ToString(CultureInfo.InvariantCulture),
                RecentTurnMinutesText = DefaultRecentTurnMinutes.ToString(CultureInfo.InvariantCulture),
            };
        }

        private static ModelPresetEditorItem ClonePreset(ModelPresetEditorItem source)
        {
            return new ModelPresetEditorItem
            {
                ModelPresetId = source.ModelPresetId,
                DisplayName = source.DisplayName,
                Model = source.Model,
                ApiBase = source.ApiBase,
                ApiKey = source.ApiKey,
                ReasoningEffort = source.ReasoningEffort,
                MaxOutputTokensText = source.MaxOutputTokensText,
                TimeoutSecondsText = source.TimeoutSecondsText,
                WebSearchEnabled = source.WebSearchEnabled,
                RecentTurnLimitText = source.RecentTurnLimitText,
                RecentTurnMinutesText = source.RecentTurnMinutesText,
            };
        }

        private static string RequireText(string value, string fieldName)
        {
            var normalized = value.Trim();
            if (normalized.Length == 0)
            {
                throw new InvalidOperationException($"{fieldName}を入力してください。");
            }

            return normalized;
        }

        private static string? EmptyToNull(string value)
        {
            var normalized = value.Trim();
            return normalized.Length == 0 ? null : normalized;
        }

        private static int ParsePositiveInt(string value, string fieldName)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            {
                throw new InvalidOperationException($"{fieldName}には1以上の整数を入力してください。");
            }

            return parsed;
        }

        private static double ParsePositiveDouble(string value, string fieldName)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || !double.IsFinite(parsed)
                || parsed <= 0)
            {
                throw new InvalidOperationException($"{fieldName}には0より大きい数値を入力してください。");
            }

            return parsed;
        }
    }
}
