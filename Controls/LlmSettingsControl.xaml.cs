using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    public partial class LlmSettingsControl : UserControl
    {
        private sealed class PromptWindowEditorItem
        {
            public string RecentTurnLimitText { get; set; } = string.Empty;
            public string RecentTurnMinutesText { get; set; } = string.Empty;
        }

        private sealed class RoleEditorItem
        {
            public string Model { get; set; } = string.Empty;
            public string ApiBase { get; set; } = string.Empty;
            public string ApiKey { get; set; } = string.Empty;
            public string ReasoningEffort { get; set; } = string.Empty;
            public string MaxOutputTokensText { get; set; } = string.Empty;
            public bool WebSearchEnabled { get; set; }
        }

        private sealed class ModelPresetEditorItem
        {
            public string ModelPresetId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public PromptWindowEditorItem PromptWindow { get; set; } = new PromptWindowEditorItem();
            public RoleEditorItem ObservationRole { get; set; } = new RoleEditorItem();
            public RoleEditorItem DecisionRole { get; set; } = new RoleEditorItem();
            public RoleEditorItem ExpressionRole { get; set; } = new RoleEditorItem();
            public RoleEditorItem MemoryRole { get; set; } = new RoleEditorItem();
        }

        private readonly List<ModelPresetEditorItem> _presets = new();
        private bool _isInitializing;
        private int _currentPresetIndex = -1;

        public event EventHandler? SettingsChanged;

        public LlmSettingsControl()
        {
            InitializeComponent();
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
                PresetSelectComboBox.Items.Clear();

                if (presets == null || presets.Count == 0)
                {
                    _currentPresetIndex = -1;
                    ClearUi();
                    return;
                }

                foreach (var preset in presets)
                {
                    _presets.Add(ToEditorItem(preset));
                    PresetSelectComboBox.Items.Add(preset.DisplayName);
                }

                _currentPresetIndex = ResolveActiveIndex(_presets.Select(p => p.ModelPresetId).ToList(), activePresetId);
                PresetSelectComboBox.SelectedIndex = _currentPresetIndex;
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
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                return null;
            }

            return _presets[_currentPresetIndex].ModelPresetId;
        }

        public string GetPreferredApiKeyForEmbeddingPaste()
        {
            SyncCurrentPresetFromUi();
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                return string.Empty;
            }

            var current = _presets[_currentPresetIndex];
            foreach (var apiKey in new[]
            {
                current.ExpressionRole.ApiKey,
                current.DecisionRole.ApiKey,
                current.ObservationRole.ApiKey,
                current.MemoryRole.ApiKey,
            })
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    return apiKey.Trim();
                }
            }

            return string.Empty;
        }

        private void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            SyncCurrentPresetFromUi();

            var item = new ModelPresetEditorItem
            {
                ModelPresetId = $"model_preset:{Guid.NewGuid():N}",
                DisplayName = GenerateUniqueName(_presets.Select(p => p.DisplayName), "新規モデルプリセット"),
                PromptWindow = new PromptWindowEditorItem(),
                ObservationRole = CreateBlankRole(),
                DecisionRole = CreateBlankRole(),
                ExpressionRole = CreateBlankRole(),
                MemoryRole = CreateBlankRole(),
            };

            _isInitializing = true;
            try
            {
                _presets.Add(item);
                PresetSelectComboBox.Items.Add(item.DisplayName);
                _currentPresetIndex = _presets.Count - 1;
                PresetSelectComboBox.SelectedIndex = _currentPresetIndex;
                LoadPresetToUi(item);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DuplicatePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                MessageBox.Show("複製するモデルプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SyncCurrentPresetFromUi();
            var source = _presets[_currentPresetIndex];
            var item = new ModelPresetEditorItem
            {
                ModelPresetId = $"model_preset:{Guid.NewGuid():N}",
                DisplayName = GenerateUniqueName(_presets.Select(p => p.DisplayName), $"{source.DisplayName} (コピー)"),
                PromptWindow = ClonePromptWindow(source.PromptWindow),
                ObservationRole = CloneRole(source.ObservationRole),
                DecisionRole = CloneRole(source.DecisionRole),
                ExpressionRole = CloneRole(source.ExpressionRole),
                MemoryRole = CloneRole(source.MemoryRole),
            };

            _isInitializing = true;
            try
            {
                _presets.Add(item);
                PresetSelectComboBox.Items.Add(item.DisplayName);
                _currentPresetIndex = _presets.Count - 1;
                PresetSelectComboBox.SelectedIndex = _currentPresetIndex;
                LoadPresetToUi(item);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                MessageBox.Show("削除するモデルプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_presets.Count <= 1)
            {
                MessageBox.Show("最後のモデルプリセットは削除できません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isInitializing = true;
            try
            {
                _presets.RemoveAt(_currentPresetIndex);
                PresetSelectComboBox.Items.RemoveAt(_currentPresetIndex);
                _currentPresetIndex = Math.Min(_currentPresetIndex, _presets.Count - 1);
                PresetSelectComboBox.SelectedIndex = _currentPresetIndex;
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

            if (sender == PresetNameTextBox && _currentPresetIndex >= 0 && _currentPresetIndex < _presets.Count)
            {
                _presets[_currentPresetIndex].DisplayName = PresetNameTextBox.Text;
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

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void IsUseLLMCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            OnCheckBoxChanged(sender, e);
        }

        private void ObservationApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(ObservationApiKeyTextBox);
        }

        private void ObservationApiKeyPasteButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(ObservationApiKeyTextBox);
        }

        private void DecisionApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(DecisionApiKeyTextBox);
        }

        private void DecisionApiKeyPasteButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(DecisionApiKeyTextBox);
        }

        private void ExpressionApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(ExpressionApiKeyTextBox);
        }

        private void ExpressionApiKeyPasteButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(ExpressionApiKeyTextBox);
        }

        private void MemoryApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(MemoryApiKeyTextBox);
        }

        private void MemoryApiKeyPasteButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(MemoryApiKeyTextBox);
        }

        private void SyncCurrentPresetFromUi()
        {
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                return;
            }

            var current = _presets[_currentPresetIndex];
            current.DisplayName = PresetNameTextBox.Text;
            current.PromptWindow.RecentTurnLimitText = RecentTurnLimitTextBox.Text;
            current.PromptWindow.RecentTurnMinutesText = RecentTurnMinutesTextBox.Text;
            SyncRoleFromUi(
                current.ObservationRole,
                ObservationModelTextBox,
                ObservationApiBaseTextBox,
                ObservationApiKeyTextBox,
                ObservationReasoningEffortTextBox,
                ObservationMaxOutputTokensTextBox,
                ObservationWebSearchCheckBox);
            SyncRoleFromUi(
                current.DecisionRole,
                DecisionModelTextBox,
                DecisionApiBaseTextBox,
                DecisionApiKeyTextBox,
                DecisionReasoningEffortTextBox,
                DecisionMaxOutputTokensTextBox,
                DecisionWebSearchCheckBox);
            SyncRoleFromUi(
                current.ExpressionRole,
                ExpressionModelTextBox,
                ExpressionApiBaseTextBox,
                ExpressionApiKeyTextBox,
                ExpressionReasoningEffortTextBox,
                ExpressionMaxOutputTokensTextBox,
                ExpressionWebSearchCheckBox);
            SyncRoleFromUi(
                current.MemoryRole,
                MemoryModelTextBox,
                MemoryApiBaseTextBox,
                MemoryApiKeyTextBox,
                MemoryReasoningEffortTextBox,
                MemoryMaxOutputTokensTextBox,
                MemoryWebSearchCheckBox);
        }

        private void LoadPresetToUi(ModelPresetEditorItem item)
        {
            PresetNameTextBox.Text = item.DisplayName;
            RecentTurnLimitTextBox.Text = item.PromptWindow.RecentTurnLimitText;
            RecentTurnMinutesTextBox.Text = item.PromptWindow.RecentTurnMinutesText;
            LoadRoleToUi(
                item.ObservationRole,
                ObservationModelTextBox,
                ObservationApiBaseTextBox,
                ObservationApiKeyTextBox,
                ObservationReasoningEffortTextBox,
                ObservationMaxOutputTokensTextBox,
                ObservationWebSearchCheckBox);
            LoadRoleToUi(
                item.DecisionRole,
                DecisionModelTextBox,
                DecisionApiBaseTextBox,
                DecisionApiKeyTextBox,
                DecisionReasoningEffortTextBox,
                DecisionMaxOutputTokensTextBox,
                DecisionWebSearchCheckBox);
            LoadRoleToUi(
                item.ExpressionRole,
                ExpressionModelTextBox,
                ExpressionApiBaseTextBox,
                ExpressionApiKeyTextBox,
                ExpressionReasoningEffortTextBox,
                ExpressionMaxOutputTokensTextBox,
                ExpressionWebSearchCheckBox);
            LoadRoleToUi(
                item.MemoryRole,
                MemoryModelTextBox,
                MemoryApiBaseTextBox,
                MemoryApiKeyTextBox,
                MemoryReasoningEffortTextBox,
                MemoryMaxOutputTokensTextBox,
                MemoryWebSearchCheckBox);
        }

        private void ClearUi()
        {
            PresetNameTextBox.Text = string.Empty;
            RecentTurnLimitTextBox.Text = string.Empty;
            RecentTurnMinutesTextBox.Text = string.Empty;
            LoadRoleToUi(
                CreateBlankRole(),
                ObservationModelTextBox,
                ObservationApiBaseTextBox,
                ObservationApiKeyTextBox,
                ObservationReasoningEffortTextBox,
                ObservationMaxOutputTokensTextBox,
                ObservationWebSearchCheckBox);
            LoadRoleToUi(
                CreateBlankRole(),
                DecisionModelTextBox,
                DecisionApiBaseTextBox,
                DecisionApiKeyTextBox,
                DecisionReasoningEffortTextBox,
                DecisionMaxOutputTokensTextBox,
                DecisionWebSearchCheckBox);
            LoadRoleToUi(
                CreateBlankRole(),
                ExpressionModelTextBox,
                ExpressionApiBaseTextBox,
                ExpressionApiKeyTextBox,
                ExpressionReasoningEffortTextBox,
                ExpressionMaxOutputTokensTextBox,
                ExpressionWebSearchCheckBox);
            LoadRoleToUi(
                CreateBlankRole(),
                MemoryModelTextBox,
                MemoryApiBaseTextBox,
                MemoryApiKeyTextBox,
                MemoryReasoningEffortTextBox,
                MemoryMaxOutputTokensTextBox,
                MemoryWebSearchCheckBox);
        }

        private void RefreshComboBoxItems()
        {
            var currentIndex = _currentPresetIndex;
            PresetSelectComboBox.SelectionChanged -= PresetSelectComboBox_SelectionChanged;
            PresetSelectComboBox.Items.Clear();
            foreach (var preset in _presets)
            {
                PresetSelectComboBox.Items.Add(preset.DisplayName);
            }
            PresetSelectComboBox.SelectedIndex = currentIndex;
            PresetSelectComboBox.SelectionChanged += PresetSelectComboBox_SelectionChanged;
        }

        private static ModelPresetEditorItem ToEditorItem(OtomeKairoModelPresetDefinition preset)
        {
            return new ModelPresetEditorItem
            {
                ModelPresetId = preset.ModelPresetId,
                DisplayName = preset.DisplayName,
                PromptWindow = new PromptWindowEditorItem
                {
                    RecentTurnLimitText = preset.PromptWindow.RecentTurnLimit > 0 ? preset.PromptWindow.RecentTurnLimit.ToString() : string.Empty,
                    RecentTurnMinutesText = preset.PromptWindow.RecentTurnMinutes > 0 ? preset.PromptWindow.RecentTurnMinutes.ToString() : string.Empty,
                },
                ObservationRole = ToRoleEditorItem(GetRole(preset, "observation_interpretation")),
                DecisionRole = ToRoleEditorItem(GetRole(preset, "decision_generation")),
                ExpressionRole = ToRoleEditorItem(GetRole(preset, "expression_generation")),
                MemoryRole = ToRoleEditorItem(GetRole(preset, "memory_interpretation")),
            };
        }

        private static OtomeKairoModelPresetDefinition ToDefinition(ModelPresetEditorItem item)
        {
            return new OtomeKairoModelPresetDefinition
            {
                ModelPresetId = item.ModelPresetId,
                DisplayName = item.DisplayName,
                PromptWindow = new OtomeKairoPromptWindowDefinition
                {
                    RecentTurnLimit = ParseRequiredPositiveIntOrZero(item.PromptWindow.RecentTurnLimitText),
                    RecentTurnMinutes = ParseRequiredPositiveIntOrZero(item.PromptWindow.RecentTurnMinutesText),
                },
                Roles = new Dictionary<string, Dictionary<string, object?>>
                {
                    ["observation_interpretation"] = ToRoleDefinition(item.ObservationRole),
                    ["decision_generation"] = ToRoleDefinition(item.DecisionRole),
                    ["expression_generation"] = ToRoleDefinition(item.ExpressionRole),
                    ["memory_interpretation"] = ToRoleDefinition(item.MemoryRole),
                },
            };
        }

        private static RoleEditorItem ToRoleEditorItem(Dictionary<string, object?> role)
        {
            return new RoleEditorItem
            {
                Model = ReadString(role, "model") ?? string.Empty,
                ApiBase = ReadString(role, "api_base") ?? string.Empty,
                ApiKey = ReadString(role, "api_key") ?? string.Empty,
                ReasoningEffort = ReadString(role, "reasoning_effort") ?? string.Empty,
                MaxOutputTokensText = ReadInt(role, "max_output_tokens"),
                WebSearchEnabled = ReadBool(role, "web_search_enabled"),
            };
        }

        private static Dictionary<string, object?> ToRoleDefinition(RoleEditorItem item)
        {
            var definition = new Dictionary<string, object?>
            {
                ["model"] = item.Model?.Trim() ?? string.Empty,
                ["api_key"] = item.ApiKey ?? string.Empty,
                ["web_search_enabled"] = item.WebSearchEnabled,
            };

            var apiBase = item.ApiBase?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(apiBase))
            {
                definition["api_base"] = apiBase;
            }

            var reasoningEffort = item.ReasoningEffort?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(reasoningEffort))
            {
                definition["reasoning_effort"] = reasoningEffort;
            }

            var maxOutputTokens = ParseOptionalPositiveInt(item.MaxOutputTokensText);
            if (maxOutputTokens.HasValue)
            {
                definition["max_output_tokens"] = maxOutputTokens.Value;
            }

            return definition;
        }

        private static Dictionary<string, object?> GetRole(OtomeKairoModelPresetDefinition preset, string roleName)
        {
            if (preset.Roles != null && preset.Roles.TryGetValue(roleName, out var role) && role != null)
            {
                return role;
            }

            return new Dictionary<string, object?>();
        }

        private static void SyncRoleFromUi(
            RoleEditorItem role,
            TextBox modelTextBox,
            TextBox apiBaseTextBox,
            TextBox apiKeyTextBox,
            TextBox reasoningEffortTextBox,
            TextBox maxOutputTokensTextBox,
            CheckBox webSearchCheckBox)
        {
            role.Model = modelTextBox.Text;
            role.ApiBase = apiBaseTextBox.Text;
            role.ApiKey = apiKeyTextBox.Text;
            role.ReasoningEffort = reasoningEffortTextBox.Text;
            role.MaxOutputTokensText = maxOutputTokensTextBox.Text;
            role.WebSearchEnabled = webSearchCheckBox.IsChecked ?? false;
        }

        private static void LoadRoleToUi(
            RoleEditorItem role,
            TextBox modelTextBox,
            TextBox apiBaseTextBox,
            TextBox apiKeyTextBox,
            TextBox reasoningEffortTextBox,
            TextBox maxOutputTokensTextBox,
            CheckBox webSearchCheckBox)
        {
            modelTextBox.Text = role.Model;
            apiBaseTextBox.Text = role.ApiBase;
            apiKeyTextBox.Text = role.ApiKey;
            reasoningEffortTextBox.Text = role.ReasoningEffort;
            maxOutputTokensTextBox.Text = role.MaxOutputTokensText;
            webSearchCheckBox.IsChecked = role.WebSearchEnabled;
        }

        private static PromptWindowEditorItem ClonePromptWindow(PromptWindowEditorItem promptWindow)
        {
            return new PromptWindowEditorItem
            {
                RecentTurnLimitText = promptWindow.RecentTurnLimitText,
                RecentTurnMinutesText = promptWindow.RecentTurnMinutesText,
            };
        }

        private static RoleEditorItem CloneRole(RoleEditorItem role)
        {
            return new RoleEditorItem
            {
                Model = role.Model,
                ApiBase = role.ApiBase,
                ApiKey = role.ApiKey,
                ReasoningEffort = role.ReasoningEffort,
                MaxOutputTokensText = role.MaxOutputTokensText,
                WebSearchEnabled = role.WebSearchEnabled,
            };
        }

        private static RoleEditorItem CreateBlankRole()
        {
            return new RoleEditorItem();
        }

        private static string? ReadString(IDictionary<string, object?> values, string key)
        {
            if (!values.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            if (value is string text)
            {
                return text;
            }

            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    return element.GetString();
                }

                return element.ToString();
            }

            return value.ToString();
        }

        private static string ReadInt(IDictionary<string, object?> values, string key)
        {
            if (!values.TryGetValue(key, out var value) || value == null)
            {
                return string.Empty;
            }

            if (value is int intValue)
            {
                return intValue.ToString();
            }

            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var jsonInt))
                {
                    return jsonInt.ToString();
                }

                return element.ToString();
            }

            return value.ToString() ?? string.Empty;
        }

        private static bool ReadBool(IDictionary<string, object?> values, string key)
        {
            if (!values.TryGetValue(key, out var value) || value == null)
            {
                return false;
            }

            if (value is bool boolValue)
            {
                return boolValue;
            }

            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
                {
                    return element.GetBoolean();
                }
            }

            return false;
        }

        private static int ParseRequiredPositiveIntOrZero(string value)
        {
            if (int.TryParse(value?.Trim(), out var parsed) && parsed >= 1)
            {
                return parsed;
            }

            return 0;
        }

        private static int? ParseOptionalPositiveInt(string value)
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }

            if (int.TryParse(trimmed, out var parsed) && parsed >= 1)
            {
                return parsed;
            }

            return 0;
        }

        private static int ResolveActiveIndex(IReadOnlyList<string?> ids, string? activeId)
        {
            if (ids.Count == 0)
            {
                return -1;
            }

            if (!string.IsNullOrWhiteSpace(activeId))
            {
                for (var i = 0; i < ids.Count; i++)
                {
                    if (string.Equals(ids[i], activeId, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            return 0;
        }

        private static string GenerateUniqueName(IEnumerable<string> existingNames, string baseName)
        {
            var existing = new HashSet<string>(existingNames.Where(name => !string.IsNullOrWhiteSpace(name)));
            var name = baseName;
            var counter = 1;
            while (existing.Contains(name))
            {
                counter += 1;
                name = $"{baseName} {counter}";
            }
            return name;
        }
    }
}
