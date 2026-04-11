using CocoroConsole.Models.OtomeKairoApi;
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
        private static readonly string[] RoleFieldKeys =
        {
            "model",
            "api_base",
            "api_key",
            "reasoning_effort",
        };

        private sealed class RoleEditorItem
        {
            public string Model { get; set; } = string.Empty;
            public string ApiBase { get; set; } = string.Empty;
            public string ApiKey { get; set; } = string.Empty;
            public string ReasoningEffort { get; set; } = string.Empty;
            public Dictionary<string, object?> AdditionalFields { get; set; } = new Dictionary<string, object?>();
        }

        private sealed class ModelPresetEditorItem
        {
            public string ModelPresetId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
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

        public string GetCurrentLlmApiKey()
        {
            SyncCurrentPresetFromUi();
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                return string.Empty;
            }

            return _presets[_currentPresetIndex].ExpressionRole.ApiKey;
        }

        private void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            SyncCurrentPresetFromUi();

            var item = new ModelPresetEditorItem
            {
                ModelPresetId = $"model_preset:{Guid.NewGuid():N}",
                DisplayName = GenerateUniqueName(_presets.Select(p => p.DisplayName), "新規モデルプリセット"),
                ObservationRole = CreateGenerationRoleTemplate(includeReasoningEffort: true),
                DecisionRole = CreateGenerationRoleTemplate(includeReasoningEffort: false),
                ExpressionRole = CreateGenerationRoleTemplate(includeReasoningEffort: false),
                MemoryRole = CreateGenerationRoleTemplate(includeReasoningEffort: false),
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

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void IsUseLLMCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SyncCurrentPresetFromUi()
        {
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                return;
            }

            var current = _presets[_currentPresetIndex];
            current.DisplayName = PresetNameTextBox.Text;
            SyncRoleFromUi(
                current.ObservationRole,
                ObservationModelTextBox,
                ObservationApiBaseTextBox,
                ObservationApiKeyPasswordBox,
                ObservationReasoningEffortTextBox);
            SyncRoleFromUi(
                current.DecisionRole,
                DecisionModelTextBox,
                DecisionApiBaseTextBox,
                DecisionApiKeyPasswordBox,
                DecisionReasoningEffortTextBox);
            SyncRoleFromUi(
                current.ExpressionRole,
                ExpressionModelTextBox,
                ExpressionApiBaseTextBox,
                ExpressionApiKeyPasswordBox,
                ExpressionReasoningEffortTextBox);
            SyncRoleFromUi(
                current.MemoryRole,
                MemoryModelTextBox,
                MemoryApiBaseTextBox,
                MemoryApiKeyPasswordBox,
                MemoryReasoningEffortTextBox);
        }

        private void LoadPresetToUi(ModelPresetEditorItem item)
        {
            PresetNameTextBox.Text = item.DisplayName;
            LoadRoleToUi(
                item.ObservationRole,
                ObservationModelTextBox,
                ObservationApiBaseTextBox,
                ObservationApiKeyPasswordBox,
                ObservationReasoningEffortTextBox);
            LoadRoleToUi(
                item.DecisionRole,
                DecisionModelTextBox,
                DecisionApiBaseTextBox,
                DecisionApiKeyPasswordBox,
                DecisionReasoningEffortTextBox);
            LoadRoleToUi(
                item.ExpressionRole,
                ExpressionModelTextBox,
                ExpressionApiBaseTextBox,
                ExpressionApiKeyPasswordBox,
                ExpressionReasoningEffortTextBox);
            LoadRoleToUi(
                item.MemoryRole,
                MemoryModelTextBox,
                MemoryApiBaseTextBox,
                MemoryApiKeyPasswordBox,
                MemoryReasoningEffortTextBox);
        }

        private void ClearUi()
        {
            PresetNameTextBox.Text = string.Empty;
            LoadRoleToUi(
                CreateGenerationRoleTemplate(includeReasoningEffort: true),
                ObservationModelTextBox,
                ObservationApiBaseTextBox,
                ObservationApiKeyPasswordBox,
                ObservationReasoningEffortTextBox);
            LoadRoleToUi(
                CreateGenerationRoleTemplate(includeReasoningEffort: false),
                DecisionModelTextBox,
                DecisionApiBaseTextBox,
                DecisionApiKeyPasswordBox,
                DecisionReasoningEffortTextBox);
            LoadRoleToUi(
                CreateGenerationRoleTemplate(includeReasoningEffort: false),
                ExpressionModelTextBox,
                ExpressionApiBaseTextBox,
                ExpressionApiKeyPasswordBox,
                ExpressionReasoningEffortTextBox);
            LoadRoleToUi(
                CreateGenerationRoleTemplate(includeReasoningEffort: false),
                MemoryModelTextBox,
                MemoryApiBaseTextBox,
                MemoryApiKeyPasswordBox,
                MemoryReasoningEffortTextBox);
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
                ObservationRole = ToRoleEditorItem(GetRole(preset, "observation_interpretation"), "low"),
                DecisionRole = ToRoleEditorItem(GetRole(preset, "decision_generation"), string.Empty),
                ExpressionRole = ToRoleEditorItem(GetRole(preset, "expression_generation"), string.Empty),
                MemoryRole = ToRoleEditorItem(GetRole(preset, "memory_interpretation"), string.Empty),
            };
        }

        private static OtomeKairoModelPresetDefinition ToDefinition(ModelPresetEditorItem item)
        {
            return new OtomeKairoModelPresetDefinition
            {
                ModelPresetId = item.ModelPresetId,
                DisplayName = item.DisplayName,
                Roles = new Dictionary<string, Dictionary<string, object?>>
                {
                    ["observation_interpretation"] = ToRoleDefinition(item.ObservationRole, includeReasoningEffort: true),
                    ["decision_generation"] = ToRoleDefinition(item.DecisionRole, includeReasoningEffort: true),
                    ["expression_generation"] = ToRoleDefinition(item.ExpressionRole, includeReasoningEffort: true),
                    ["memory_interpretation"] = ToRoleDefinition(item.MemoryRole, includeReasoningEffort: true),
                },
            };
        }

        private static RoleEditorItem ToRoleEditorItem(
            Dictionary<string, object?> role,
            string defaultReasoningEffort)
        {
            return new RoleEditorItem
            {
                Model = ReadString(role, "model") ?? string.Empty,
                ApiBase = ReadString(role, "api_base") ?? string.Empty,
                ApiKey = ReadString(role, "api_key") ?? string.Empty,
                ReasoningEffort = ReadString(role, "reasoning_effort") ?? defaultReasoningEffort,
                AdditionalFields = CopyAdditionalFields(role, RoleFieldKeys),
            };
        }

        private static Dictionary<string, object?> ToRoleDefinition(RoleEditorItem item, bool includeReasoningEffort)
        {
            var definition = new Dictionary<string, object?>(item.AdditionalFields, StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = item.Model?.Trim() ?? string.Empty,
                ["api_key"] = item.ApiKey ?? string.Empty,
            };

            var apiBase = item.ApiBase?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiBase))
            {
                definition.Remove("api_base");
            }
            else
            {
                definition["api_base"] = apiBase;
            }

            if (includeReasoningEffort)
            {
                var reasoningEffort = item.ReasoningEffort?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(reasoningEffort))
                {
                    definition.Remove("reasoning_effort");
                }
                else
                {
                    definition["reasoning_effort"] = reasoningEffort;
                }
            }
            else
            {
                definition.Remove("reasoning_effort");
            }

            return definition;
        }

        private static Dictionary<string, object?> GetRole(OtomeKairoModelPresetDefinition preset, string roleName)
        {
            if (preset.Roles != null && preset.Roles.TryGetValue(roleName, out var role) && role != null)
            {
                return role;
            }

            return CreateGenerationRoleTemplateDefinition(includeReasoningEffort: roleName == "observation_interpretation");
        }

        private static void SyncRoleFromUi(
            RoleEditorItem role,
            TextBox modelTextBox,
            TextBox apiBaseTextBox,
            PasswordBox apiKeyPasswordBox,
            TextBox reasoningEffortTextBox)
        {
            role.Model = modelTextBox.Text;
            role.ApiBase = apiBaseTextBox.Text;
            role.ApiKey = apiKeyPasswordBox.Password;
            role.ReasoningEffort = reasoningEffortTextBox.Text;
        }

        private static void LoadRoleToUi(
            RoleEditorItem role,
            TextBox modelTextBox,
            TextBox apiBaseTextBox,
            PasswordBox apiKeyPasswordBox,
            TextBox reasoningEffortTextBox)
        {
            modelTextBox.Text = role.Model;
            apiBaseTextBox.Text = role.ApiBase;
            apiKeyPasswordBox.Password = role.ApiKey;
            reasoningEffortTextBox.Text = role.ReasoningEffort;
        }

        private static RoleEditorItem CloneRole(RoleEditorItem role)
        {
            return new RoleEditorItem
            {
                Model = role.Model,
                ApiBase = role.ApiBase,
                ApiKey = role.ApiKey,
                ReasoningEffort = role.ReasoningEffort,
                AdditionalFields = new Dictionary<string, object?>(role.AdditionalFields),
            };
        }

        private static Dictionary<string, object?> CopyAdditionalFields(
            IDictionary<string, object?> values,
            IEnumerable<string> excludedKeys)
        {
            var excluded = new HashSet<string>(excludedKeys, StringComparer.OrdinalIgnoreCase);
            return values
                .Where(pair => !excluded.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
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

        private static RoleEditorItem CreateGenerationRoleTemplate(bool includeReasoningEffort)
        {
            return ToRoleEditorItem(
                CreateGenerationRoleTemplateDefinition(includeReasoningEffort),
                includeReasoningEffort ? "low" : string.Empty);
        }

        private static Dictionary<string, object?> CreateGenerationRoleTemplateDefinition(bool includeReasoningEffort)
        {
            var role = new Dictionary<string, object?>
            {
                ["model"] = "openrouter/google/gemini-3.1-flash-lite-preview",
                ["api_key"] = "",
            };
            if (includeReasoningEffort)
            {
                role["reasoning_effort"] = "low";
            }
            return role;
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
