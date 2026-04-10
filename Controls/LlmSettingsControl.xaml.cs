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
        private sealed class ModelPresetEditorItem
        {
            public string ModelPresetId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string ObservationRoleJson { get; set; } = "{}";
            public string DecisionRoleJson { get; set; } = "{}";
            public string ExpressionRoleJson { get; set; } = "{}";
            public string MemoryRoleJson { get; set; } = "{}";
            public string EmbeddingRoleJson { get; set; } = "{}";
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

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
            try
            {
                SyncCurrentPresetFromUi();
                if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
                {
                    return string.Empty;
                }

                var role = ParseObject(_presets[_currentPresetIndex].ExpressionRoleJson, "expression_generation");
                return ReadString(role, "api_key") ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            SyncCurrentPresetFromUi();

            var item = new ModelPresetEditorItem
            {
                ModelPresetId = $"model_preset:{Guid.NewGuid():N}",
                DisplayName = GenerateUniqueName(_presets.Select(p => p.DisplayName), "新規モデルプリセット"),
                ObservationRoleJson = SerializeObject(CreateGenerationRoleTemplate(includeReasoningEffort: true)),
                DecisionRoleJson = SerializeObject(CreateGenerationRoleTemplate(includeReasoningEffort: false)),
                ExpressionRoleJson = SerializeObject(CreateGenerationRoleTemplate(includeReasoningEffort: false)),
                MemoryRoleJson = SerializeObject(CreateGenerationRoleTemplate(includeReasoningEffort: false)),
                EmbeddingRoleJson = SerializeObject(CreateEmbeddingRoleTemplate()),
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
                ObservationRoleJson = source.ObservationRoleJson,
                DecisionRoleJson = source.DecisionRoleJson,
                ExpressionRoleJson = source.ExpressionRoleJson,
                MemoryRoleJson = source.MemoryRoleJson,
                EmbeddingRoleJson = source.EmbeddingRoleJson,
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
            current.ObservationRoleJson = NormalizeJsonText(ObservationRoleJsonTextBox.Text);
            current.DecisionRoleJson = NormalizeJsonText(DecisionRoleJsonTextBox.Text);
            current.ExpressionRoleJson = NormalizeJsonText(ExpressionRoleJsonTextBox.Text);
            current.MemoryRoleJson = NormalizeJsonText(MemoryRoleJsonTextBox.Text);
            current.EmbeddingRoleJson = NormalizeJsonText(EmbeddingRoleJsonTextBox.Text);
        }

        private void LoadPresetToUi(ModelPresetEditorItem item)
        {
            PresetNameTextBox.Text = item.DisplayName;
            ObservationRoleJsonTextBox.Text = NormalizeJsonText(item.ObservationRoleJson);
            DecisionRoleJsonTextBox.Text = NormalizeJsonText(item.DecisionRoleJson);
            ExpressionRoleJsonTextBox.Text = NormalizeJsonText(item.ExpressionRoleJson);
            MemoryRoleJsonTextBox.Text = NormalizeJsonText(item.MemoryRoleJson);
            EmbeddingRoleJsonTextBox.Text = NormalizeJsonText(item.EmbeddingRoleJson);
        }

        private void ClearUi()
        {
            PresetNameTextBox.Text = string.Empty;
            ObservationRoleJsonTextBox.Text = "{}";
            DecisionRoleJsonTextBox.Text = "{}";
            ExpressionRoleJsonTextBox.Text = "{}";
            MemoryRoleJsonTextBox.Text = "{}";
            EmbeddingRoleJsonTextBox.Text = "{}";
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
                ObservationRoleJson = SerializeObject(GetRole(preset, "observation_interpretation")),
                DecisionRoleJson = SerializeObject(GetRole(preset, "decision_generation")),
                ExpressionRoleJson = SerializeObject(GetRole(preset, "expression_generation")),
                MemoryRoleJson = SerializeObject(GetRole(preset, "memory_interpretation")),
                EmbeddingRoleJson = SerializeObject(GetRole(preset, "embedding")),
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
                    ["observation_interpretation"] = ParseObject(item.ObservationRoleJson, $"{item.DisplayName} の observation_interpretation"),
                    ["decision_generation"] = ParseObject(item.DecisionRoleJson, $"{item.DisplayName} の decision_generation"),
                    ["expression_generation"] = ParseObject(item.ExpressionRoleJson, $"{item.DisplayName} の expression_generation"),
                    ["memory_interpretation"] = ParseObject(item.MemoryRoleJson, $"{item.DisplayName} の memory_interpretation"),
                    ["embedding"] = ParseObject(item.EmbeddingRoleJson, $"{item.DisplayName} の embedding"),
                },
            };
        }

        private static Dictionary<string, object?> GetRole(OtomeKairoModelPresetDefinition preset, string roleName)
        {
            if (preset.Roles != null && preset.Roles.TryGetValue(roleName, out var role) && role != null)
            {
                return role;
            }

            return roleName == "embedding"
                ? CreateEmbeddingRoleTemplate()
                : CreateGenerationRoleTemplate(includeReasoningEffort: roleName == "observation_interpretation");
        }

        private static Dictionary<string, object?> ParseObject(string jsonText, string fieldName)
        {
            var normalized = NormalizeJsonText(jsonText);
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(normalized, JsonOptions)
                    ?? new Dictionary<string, object?>();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{fieldName} の JSON が不正です: {ex.Message}", ex);
            }
        }

        private static string SerializeObject(Dictionary<string, object?> values)
        {
            return JsonSerializer.Serialize(values, JsonOptions);
        }

        private static string NormalizeJsonText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
        }

        private static Dictionary<string, object?> CreateGenerationRoleTemplate(bool includeReasoningEffort)
        {
            var role = new Dictionary<string, object?>
            {
                ["kind"] = "generation",
                ["provider"] = "openrouter",
                ["model"] = "openrouter/google/gemini-3.1-flash-lite-preview",
                ["endpoint_ref"] = "endpoint:openrouter_primary",
                ["api_key"] = "",
            };
            if (includeReasoningEffort)
            {
                role["reasoning_effort"] = "low";
            }
            return role;
        }

        private static Dictionary<string, object?> CreateEmbeddingRoleTemplate()
        {
            return new Dictionary<string, object?>
            {
                ["kind"] = "embedding",
                ["provider"] = "openrouter",
                ["model"] = "openrouter/google/gemini-embedding-001",
                ["endpoint_ref"] = "endpoint:openrouter_primary",
                ["api_key"] = "",
            };
        }

        private static string? ReadString(Dictionary<string, object?> values, string key)
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
