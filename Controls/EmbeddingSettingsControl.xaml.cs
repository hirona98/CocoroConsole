using CocoroConsole.Models.OtomeKairoApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    public partial class EmbeddingSettingsControl : UserControl
    {
        private sealed class MemorySetEditorItem
        {
            public string MemorySetId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
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

        public bool IsMemoryEnabled
        {
            get => MemoryEnabledCheckBox.IsChecked ?? false;
            set
            {
                _isInitializing = true;
                try
                {
                    MemoryEnabledCheckBox.IsChecked = value;
                }
                finally
                {
                    _isInitializing = false;
                }
            }
        }

        public void LoadSettings(List<OtomeKairoMemorySetDefinition>? memorySets, string? activeMemorySetId, bool memoryEnabled)
        {
            _isInitializing = true;
            try
            {
                MemoryEnabledCheckBox.IsChecked = memoryEnabled;
                _memorySets.Clear();
                MemorySetSelectComboBox.Items.Clear();

                if (memorySets == null || memorySets.Count == 0)
                {
                    _currentMemorySetIndex = -1;
                    ClearUi();
                    return;
                }

                foreach (var memorySet in memorySets)
                {
                    _memorySets.Add(new MemorySetEditorItem
                    {
                        MemorySetId = memorySet.MemorySetId,
                        DisplayName = memorySet.DisplayName,
                        Description = memorySet.Description ?? string.Empty,
                        ServerBackedMemorySetId = memorySet.MemorySetId,
                        CloneSourceMemorySetId = null,
                    });
                    MemorySetSelectComboBox.Items.Add(memorySet.DisplayName);
                }

                _currentMemorySetIndex = ResolveActiveIndex(_memorySets.Select(p => p.MemorySetId).ToList(), activeMemorySetId);
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
            return _memorySets.Select(item => new OtomeKairoMemorySetDefinition
            {
                MemorySetId = item.MemorySetId,
                DisplayName = item.DisplayName,
                Description = string.IsNullOrWhiteSpace(item.Description) ? null : item.Description,
            }).ToList();
        }

        public List<(string SourceMemorySetId, OtomeKairoMemorySetDefinition Definition)> GetPendingCloneRequests()
        {
            SyncCurrentMemorySetFromUi();
            return _memorySets
                .Where(item => string.IsNullOrWhiteSpace(item.ServerBackedMemorySetId)
                    && !string.IsNullOrWhiteSpace(item.CloneSourceMemorySetId))
                .Select(item => (
                    SourceMemorySetId: item.CloneSourceMemorySetId!,
                    Definition: new OtomeKairoMemorySetDefinition
                    {
                        MemorySetId = item.MemorySetId,
                        DisplayName = item.DisplayName,
                        Description = string.IsNullOrWhiteSpace(item.Description) ? null : item.Description,
                    }))
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

            var item = new MemorySetEditorItem
            {
                MemorySetId = $"memory_set:{Guid.NewGuid():N}",
                DisplayName = GenerateUniqueName(_memorySets.Select(p => p.DisplayName), "新規記憶集合"),
                Description = string.Empty,
                ServerBackedMemorySetId = null,
                CloneSourceMemorySetId = null,
            };

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
            var cloneSourceMemorySetId = ResolveCloneSourceMemorySetId(source);
            var item = new MemorySetEditorItem
            {
                MemorySetId = $"memory_set:{Guid.NewGuid():N}",
                DisplayName = GenerateUniqueName(_memorySets.Select(p => p.DisplayName), $"{source.DisplayName} (コピー)"),
                Description = source.Description,
                ServerBackedMemorySetId = null,
                CloneSourceMemorySetId = cloneSourceMemorySetId,
            };

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

        private void MemoryEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
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
            current.Description = MemorySetDescriptionTextBox.Text;
        }

        private void LoadMemorySetToUi(MemorySetEditorItem item)
        {
            MemorySetDisplayNameTextBox.Text = item.DisplayName;
            MemorySetDescriptionTextBox.Text = item.Description;
        }

        private void ClearUi()
        {
            MemorySetDisplayNameTextBox.Text = string.Empty;
            MemorySetDescriptionTextBox.Text = string.Empty;
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
