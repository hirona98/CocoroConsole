using CocoroConsole.Models.OtomeKairoApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    public partial class EmbeddingSettingsControl : UserControl
    {
        private static readonly string[] EmbeddingFieldKeys =
        {
            "model",
            "api_base",
            "api_key",
        };

        private sealed class MemorySetEditorItem
        {
            public string MemorySetId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string EmbeddingModel { get; set; } = string.Empty;
            public string EmbeddingApiBase { get; set; } = string.Empty;
            public string EmbeddingApiKey { get; set; } = string.Empty;
            public Dictionary<string, object?> AdditionalEmbeddingFields { get; set; } = new Dictionary<string, object?>();
            public string? ServerBackedMemorySetId { get; set; }
            public string? CloneSourceMemorySetId { get; set; }
        }

        private readonly List<MemorySetEditorItem> _memorySets = new();
        private bool _isInitializing;
        private int _currentMemorySetIndex = -1;

        public event EventHandler? SettingsChanged;

        public EmbeddingSettingsControl()
        {
            InitializeComponent();
        }

        public void LoadSettings(
            List<OtomeKairoMemorySetDefinition>? memorySets,
            string? activeMemorySetId)
        {
            _isInitializing = true;
            try
            {
                _memorySets.Clear();
                MemorySetSelectComboBox.Items.Clear();

                if (memorySets == null || memorySets.Count == 0)
                {
                    _currentMemorySetIndex = -1;
                    ClearMemorySetUi();
                    return;
                }

                foreach (var memorySet in memorySets)
                {
                    _memorySets.Add(ToEditorItem(memorySet));
                    MemorySetSelectComboBox.Items.Add(memorySet.DisplayName);
                }

                _currentMemorySetIndex = ResolveActiveIndex(_memorySets.Select(item => item.MemorySetId).ToList(), activeMemorySetId);
                MemorySetSelectComboBox.SelectedIndex = _currentMemorySetIndex;
                LoadMemorySetToUi(_memorySets[_currentMemorySetIndex]);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public List<OtomeKairoMemorySetDefinition> GetAllMemorySets()
        {
            SyncCurrentMemorySetFromUi();
            return _memorySets.Select(ToDefinition).ToList();
        }

        public List<(string SourceMemorySetId, OtomeKairoMemorySetDefinition Definition)> GetPendingCloneRequests()
        {
            SyncCurrentMemorySetFromUi();
            return _memorySets
                .Where(item => string.IsNullOrWhiteSpace(item.ServerBackedMemorySetId)
                    && !string.IsNullOrWhiteSpace(item.CloneSourceMemorySetId))
                .Select(item => (
                    SourceMemorySetId: item.CloneSourceMemorySetId!,
                    Definition: ToDefinition(item)))
                .ToList();
        }

        public string? GetActiveMemorySetId()
        {
            if (_currentMemorySetIndex < 0 || _currentMemorySetIndex >= _memorySets.Count)
            {
                return null;
            }

            return _memorySets[_currentMemorySetIndex].MemorySetId;
        }

        private void AddMemorySetButton_Click(object sender, RoutedEventArgs e)
        {
            SyncCurrentMemorySetFromUi();

            var item = CreateMemorySetTemplate();
            item.MemorySetId = $"memory_set:{Guid.NewGuid():N}";
            item.DisplayName = GenerateUniqueName(_memorySets.Select(p => p.DisplayName), "新規記憶集合");

            _isInitializing = true;
            try
            {
                _memorySets.Add(item);
                MemorySetSelectComboBox.Items.Add(item.DisplayName);
                _currentMemorySetIndex = _memorySets.Count - 1;
                MemorySetSelectComboBox.SelectedIndex = _currentMemorySetIndex;
                LoadMemorySetToUi(item);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DuplicateMemorySetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMemorySetIndex < 0 || _currentMemorySetIndex >= _memorySets.Count)
            {
                MessageBox.Show("複製する記憶集合を選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SyncCurrentMemorySetFromUi();
            var source = _memorySets[_currentMemorySetIndex];
            var item = CloneMemorySet(source);
            item.MemorySetId = $"memory_set:{Guid.NewGuid():N}";
            item.DisplayName = GenerateUniqueName(_memorySets.Select(p => p.DisplayName), $"{source.DisplayName} (コピー)");
            item.ServerBackedMemorySetId = null;
            item.CloneSourceMemorySetId = ResolveCloneSourceMemorySetId(source);

            _isInitializing = true;
            try
            {
                _memorySets.Add(item);
                MemorySetSelectComboBox.Items.Add(item.DisplayName);
                _currentMemorySetIndex = _memorySets.Count - 1;
                MemorySetSelectComboBox.SelectedIndex = _currentMemorySetIndex;
                LoadMemorySetToUi(item);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DeleteMemorySetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMemorySetIndex < 0 || _currentMemorySetIndex >= _memorySets.Count)
            {
                MessageBox.Show("削除する記憶集合を選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_memorySets.Count <= 1)
            {
                MessageBox.Show("最後の記憶集合は削除できません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isInitializing = true;
            try
            {
                _memorySets.RemoveAt(_currentMemorySetIndex);
                MemorySetSelectComboBox.Items.RemoveAt(_currentMemorySetIndex);
                _currentMemorySetIndex = Math.Min(_currentMemorySetIndex, _memorySets.Count - 1);
                MemorySetSelectComboBox.SelectedIndex = _currentMemorySetIndex;
                LoadMemorySetToUi(_memorySets[_currentMemorySetIndex]);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void MemorySetSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SyncCurrentMemorySetFromUi();

            var selectedIndex = MemorySetSelectComboBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _memorySets.Count)
            {
                return;
            }

            _isInitializing = true;
            try
            {
                _currentMemorySetIndex = selectedIndex;
                LoadMemorySetToUi(_memorySets[selectedIndex]);
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

            if (sender == MemorySetDisplayNameTextBox && _currentMemorySetIndex >= 0 && _currentMemorySetIndex < _memorySets.Count)
            {
                _memorySets[_currentMemorySetIndex].DisplayName = MemorySetDisplayNameTextBox.Text;
                RefreshComboBoxItems();
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SyncCurrentMemorySetFromUi()
        {
            if (_currentMemorySetIndex < 0 || _currentMemorySetIndex >= _memorySets.Count)
            {
                return;
            }

            var current = _memorySets[_currentMemorySetIndex];
            current.DisplayName = MemorySetDisplayNameTextBox.Text;
            current.EmbeddingModel = EmbeddingModelTextBox.Text;
            current.EmbeddingApiBase = EmbeddingApiBaseTextBox.Text;
            current.EmbeddingApiKey = EmbeddingApiKeyTextBox.Text;
        }

        private void LoadMemorySetToUi(MemorySetEditorItem item)
        {
            MemorySetDisplayNameTextBox.Text = item.DisplayName;
            EmbeddingModelTextBox.Text = item.EmbeddingModel;
            EmbeddingApiBaseTextBox.Text = item.EmbeddingApiBase;
            EmbeddingApiKeyTextBox.Text = item.EmbeddingApiKey;
        }

        private void ClearMemorySetUi()
        {
            MemorySetDisplayNameTextBox.Text = string.Empty;
            EmbeddingModelTextBox.Text = string.Empty;
            EmbeddingApiBaseTextBox.Text = string.Empty;
            EmbeddingApiKeyTextBox.Text = string.Empty;
        }

        private void RefreshComboBoxItems()
        {
            var currentIndex = _currentMemorySetIndex;
            MemorySetSelectComboBox.SelectionChanged -= MemorySetSelectComboBox_SelectionChanged;
            MemorySetSelectComboBox.Items.Clear();
            foreach (var memorySet in _memorySets)
            {
                MemorySetSelectComboBox.Items.Add(memorySet.DisplayName);
            }
            MemorySetSelectComboBox.SelectedIndex = currentIndex;
            MemorySetSelectComboBox.SelectionChanged += MemorySetSelectComboBox_SelectionChanged;
        }

        private static MemorySetEditorItem ToEditorItem(OtomeKairoMemorySetDefinition memorySet)
        {
            return new MemorySetEditorItem
            {
                MemorySetId = memorySet.MemorySetId,
                DisplayName = memorySet.DisplayName,
                EmbeddingModel = ReadString(memorySet.Embedding, "model") ?? string.Empty,
                EmbeddingApiBase = ReadString(memorySet.Embedding, "api_base") ?? string.Empty,
                EmbeddingApiKey = ReadString(memorySet.Embedding, "api_key") ?? string.Empty,
                AdditionalEmbeddingFields = CopyAdditionalFields(memorySet.Embedding, EmbeddingFieldKeys),
                ServerBackedMemorySetId = memorySet.MemorySetId,
                CloneSourceMemorySetId = null,
            };
        }

        private static OtomeKairoMemorySetDefinition ToDefinition(MemorySetEditorItem item)
        {
            var embedding = new Dictionary<string, object?>(item.AdditionalEmbeddingFields, StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = item.EmbeddingModel?.Trim() ?? string.Empty,
                ["api_key"] = item.EmbeddingApiKey ?? string.Empty,
            };

            var apiBase = item.EmbeddingApiBase?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(apiBase))
            {
                embedding.Remove("api_base");
            }
            else
            {
                embedding["api_base"] = apiBase;
            }

            return new OtomeKairoMemorySetDefinition
            {
                MemorySetId = item.MemorySetId,
                DisplayName = item.DisplayName,
                Embedding = embedding,
            };
        }

        private static MemorySetEditorItem CreateMemorySetTemplate()
        {
            return new MemorySetEditorItem
            {
                EmbeddingApiBase = string.Empty,
                EmbeddingApiKey = string.Empty,
            };
        }

        private static MemorySetEditorItem CloneMemorySet(MemorySetEditorItem item)
        {
            return new MemorySetEditorItem
            {
                MemorySetId = item.MemorySetId,
                DisplayName = item.DisplayName,
                EmbeddingModel = item.EmbeddingModel,
                EmbeddingApiBase = item.EmbeddingApiBase,
                EmbeddingApiKey = item.EmbeddingApiKey,
                AdditionalEmbeddingFields = new Dictionary<string, object?>(item.AdditionalEmbeddingFields),
                ServerBackedMemorySetId = item.ServerBackedMemorySetId,
                CloneSourceMemorySetId = item.CloneSourceMemorySetId,
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

        private static string? ResolveCloneSourceMemorySetId(MemorySetEditorItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.ServerBackedMemorySetId))
            {
                return item.ServerBackedMemorySetId;
            }

            if (!string.IsNullOrWhiteSpace(item.CloneSourceMemorySetId))
            {
                return item.CloneSourceMemorySetId;
            }

            return null;
        }
    }
}
