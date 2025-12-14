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
    public partial class PromptSettingsControl : UserControl
    {
        private bool _isInitializing;

        private CocoroGhostApiClient? _apiClient;
        private Func<Task>? _onPresetListChanged;

        private readonly List<SystemPromptPreset> _systemPromptPresets = new();
        private readonly List<PersonaPreset> _personaPresets = new();
        private readonly List<ContractPreset> _contractPresets = new();

        private int _currentSystemPromptPresetIndex = -1;
        private int _currentPersonaPresetIndex = -1;
        private int _currentContractPresetIndex = -1;

        public event EventHandler? SettingsChanged;

        public PromptSettingsControl()
        {
            InitializeComponent();
        }

        public void SetApiClient(CocoroGhostApiClient apiClient, Func<Task> onPresetListChanged)
        {
            _apiClient = apiClient;
            _onPresetListChanged = onPresetListChanged;
        }

        public void LoadSettings(
            List<SystemPromptPreset>? systemPromptPresets,
            int? activeSystemPromptPresetId,
            List<PersonaPreset>? personaPresets,
            int? activePersonaPresetId,
            List<ContractPreset>? contractPresets,
            int? activeContractPresetId
        )
        {
            _isInitializing = true;
            try
            {
                LoadSystemPromptPresets(systemPromptPresets, activeSystemPromptPresetId);
                LoadPersonaPresets(personaPresets, activePersonaPresetId);
                LoadContractPresets(contractPresets, activeContractPresetId);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public List<SystemPromptPreset> GetAllSystemPromptPresets()
        {
            SaveCurrentSystemPromptUiToPreset();
            return _systemPromptPresets.ToList();
        }

        public List<PersonaPreset> GetAllPersonaPresets()
        {
            SaveCurrentPersonaUiToPreset();
            return _personaPresets.ToList();
        }

        public List<ContractPreset> GetAllContractPresets()
        {
            SaveCurrentContractUiToPreset();
            return _contractPresets.ToList();
        }

        public int? GetActiveSystemPromptPresetId()
        {
            if (_currentSystemPromptPresetIndex < 0 || _currentSystemPromptPresetIndex >= _systemPromptPresets.Count)
            {
                return null;
            }

            return _systemPromptPresets[_currentSystemPromptPresetIndex].SystemPromptPresetId;
        }

        public int? GetActivePersonaPresetId()
        {
            if (_currentPersonaPresetIndex < 0 || _currentPersonaPresetIndex >= _personaPresets.Count)
            {
                return null;
            }

            return _personaPresets[_currentPersonaPresetIndex].PersonaPresetId;
        }

        public int? GetActiveContractPresetId()
        {
            if (_currentContractPresetIndex < 0 || _currentContractPresetIndex >= _contractPresets.Count)
            {
                return null;
            }

            return _contractPresets[_currentContractPresetIndex].ContractPresetId;
        }

        private async Task SavePresetsToApiAsync()
        {
            if (_apiClient == null || _onPresetListChanged == null)
            {
                return;
            }

            try
            {
                await _onPresetListChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プリセットの保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSystemPromptPresets(List<SystemPromptPreset>? presets, int? activePresetId)
        {
            _systemPromptPresets.Clear();
            SystemPromptPresetSelectComboBox.Items.Clear();

            if (presets == null || presets.Count == 0)
            {
                _currentSystemPromptPresetIndex = -1;
                ClearSystemPromptUi();
                return;
            }

            _systemPromptPresets.AddRange(presets);
            foreach (var preset in _systemPromptPresets)
            {
                SystemPromptPresetSelectComboBox.Items.Add(preset.SystemPromptPresetName);
            }

            var activeIndex = ResolveActiveIndex(_systemPromptPresets.Select(p => p.SystemPromptPresetId).ToList(), activePresetId);
            _currentSystemPromptPresetIndex = activeIndex;
            SystemPromptPresetSelectComboBox.SelectedIndex = activeIndex;
            LoadSystemPromptPresetToUi(_systemPromptPresets[activeIndex]);
        }

        private void LoadPersonaPresets(List<PersonaPreset>? presets, int? activePresetId)
        {
            _personaPresets.Clear();
            PersonaPresetSelectComboBox.Items.Clear();

            if (presets == null || presets.Count == 0)
            {
                _currentPersonaPresetIndex = -1;
                ClearPersonaUi();
                return;
            }

            _personaPresets.AddRange(presets);
            foreach (var preset in _personaPresets)
            {
                PersonaPresetSelectComboBox.Items.Add(preset.PersonaPresetName);
            }

            var activeIndex = ResolveActiveIndex(_personaPresets.Select(p => p.PersonaPresetId).ToList(), activePresetId);
            _currentPersonaPresetIndex = activeIndex;
            PersonaPresetSelectComboBox.SelectedIndex = activeIndex;
            LoadPersonaPresetToUi(_personaPresets[activeIndex]);
        }

        private void LoadContractPresets(List<ContractPreset>? presets, int? activePresetId)
        {
            _contractPresets.Clear();
            ContractPresetSelectComboBox.Items.Clear();

            if (presets == null || presets.Count == 0)
            {
                _currentContractPresetIndex = -1;
                ClearContractUi();
                return;
            }

            _contractPresets.AddRange(presets);
            foreach (var preset in _contractPresets)
            {
                ContractPresetSelectComboBox.Items.Add(preset.ContractPresetName);
            }

            var activeIndex = ResolveActiveIndex(_contractPresets.Select(p => p.ContractPresetId).ToList(), activePresetId);
            _currentContractPresetIndex = activeIndex;
            ContractPresetSelectComboBox.SelectedIndex = activeIndex;
            LoadContractPresetToUi(_contractPresets[activeIndex]);
        }

        private static int ResolveActiveIndex(IReadOnlyList<int> presetIds, int? activePresetId)
        {
            if (presetIds.Count == 0)
            {
                return -1;
            }

            if (!activePresetId.HasValue)
            {
                return 0;
            }

            for (var i = 0; i < presetIds.Count; i++)
            {
                if (presetIds[i] == activePresetId.Value)
                {
                    return i;
                }
            }

            return 0;
        }

        private static string GenerateUniqueName(IEnumerable<string> existingNames, string baseName)
        {
            var existing = new HashSet<string>(existingNames);

            int counter = 1;
            string name = baseName;

            while (existing.Contains(name))
            {
                counter++;
                name = $"{baseName} {counter}";
            }

            return name;
        }

        private static string GenerateDuplicateName(IEnumerable<string> existingNames, string sourceName)
        {
            string baseName = $"{sourceName} (コピー)";
            return GenerateUniqueName(existingNames, baseName);
        }

        private void SaveCurrentSystemPromptUiToPreset()
        {
            if (_currentSystemPromptPresetIndex < 0 || _currentSystemPromptPresetIndex >= _systemPromptPresets.Count)
            {
                return;
            }

            var preset = _systemPromptPresets[_currentSystemPromptPresetIndex];
            preset.SystemPromptPresetName = SystemPromptPresetNameTextBox.Text;
            preset.SystemPrompt = SystemPromptTextBox.Text;
        }

        private void SaveCurrentPersonaUiToPreset()
        {
            if (_currentPersonaPresetIndex < 0 || _currentPersonaPresetIndex >= _personaPresets.Count)
            {
                return;
            }

            var preset = _personaPresets[_currentPersonaPresetIndex];
            preset.PersonaPresetName = PersonaPresetNameTextBox.Text;
            preset.PersonaText = PersonaTextBox.Text;
        }

        private void SaveCurrentContractUiToPreset()
        {
            if (_currentContractPresetIndex < 0 || _currentContractPresetIndex >= _contractPresets.Count)
            {
                return;
            }

            var preset = _contractPresets[_currentContractPresetIndex];
            preset.ContractPresetName = ContractPresetNameTextBox.Text;
            preset.ContractText = ContractTextBox.Text;
        }

        private async void AddSystemPromptPresetButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentSystemPromptUiToPreset();

            var newPreset = new SystemPromptPreset
            {
                SystemPromptPresetId = 0,
                SystemPromptPresetName = GenerateUniqueName(_systemPromptPresets.Select(p => p.SystemPromptPresetName), "新規プリセット"),
                SystemPrompt = string.Empty
            };

            _isInitializing = true;
            try
            {
                _systemPromptPresets.Add(newPreset);
                SystemPromptPresetSelectComboBox.Items.Add(newPreset.SystemPromptPresetName);
                _currentSystemPromptPresetIndex = _systemPromptPresets.Count - 1;
                SystemPromptPresetSelectComboBox.SelectedIndex = _currentSystemPromptPresetIndex;
                LoadSystemPromptPresetToUi(newPreset);
            }
            finally
            {
                _isInitializing = false;
            }

            await SavePresetsToApiAsync();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void DuplicateSystemPromptPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSystemPromptPresetIndex < 0 || _currentSystemPromptPresetIndex >= _systemPromptPresets.Count)
            {
                MessageBox.Show("複製するプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveCurrentSystemPromptUiToPreset();

            var source = _systemPromptPresets[_currentSystemPromptPresetIndex];
            var duplicate = new SystemPromptPreset
            {
                SystemPromptPresetId = 0,
                SystemPromptPresetName = GenerateDuplicateName(_systemPromptPresets.Select(p => p.SystemPromptPresetName), source.SystemPromptPresetName),
                SystemPrompt = source.SystemPrompt
            };

            _isInitializing = true;
            try
            {
                _systemPromptPresets.Add(duplicate);
                SystemPromptPresetSelectComboBox.Items.Add(duplicate.SystemPromptPresetName);
                _currentSystemPromptPresetIndex = _systemPromptPresets.Count - 1;
                SystemPromptPresetSelectComboBox.SelectedIndex = _currentSystemPromptPresetIndex;
                LoadSystemPromptPresetToUi(duplicate);
            }
            finally
            {
                _isInitializing = false;
            }

            await SavePresetsToApiAsync();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void DeleteSystemPromptPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSystemPromptPresetIndex < 0 || _currentSystemPromptPresetIndex >= _systemPromptPresets.Count)
            {
                MessageBox.Show("削除するプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_systemPromptPresets.Count <= 1)
            {
                MessageBox.Show("最後のプリセットは削除できません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string presetName = _systemPromptPresets[_currentSystemPromptPresetIndex].SystemPromptPresetName;
            var result = MessageBox.Show(
                $"プリセット「{presetName}」を削除しますか？",
                "削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _isInitializing = true;
            try
            {
                _systemPromptPresets.RemoveAt(_currentSystemPromptPresetIndex);
                SystemPromptPresetSelectComboBox.Items.RemoveAt(_currentSystemPromptPresetIndex);

                _currentSystemPromptPresetIndex = Math.Min(_currentSystemPromptPresetIndex, _systemPromptPresets.Count - 1);
                SystemPromptPresetSelectComboBox.SelectedIndex = _currentSystemPromptPresetIndex;
                LoadSystemPromptPresetToUi(_systemPromptPresets[_currentSystemPromptPresetIndex]);
            }
            finally
            {
                _isInitializing = false;
            }

            await SavePresetsToApiAsync();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void AddPersonaPresetButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentPersonaUiToPreset();

            var newPreset = new PersonaPreset
            {
                PersonaPresetId = 0,
                PersonaPresetName = GenerateUniqueName(_personaPresets.Select(p => p.PersonaPresetName), "新規プリセット"),
                PersonaText = string.Empty
            };

            _isInitializing = true;
            try
            {
                _personaPresets.Add(newPreset);
                PersonaPresetSelectComboBox.Items.Add(newPreset.PersonaPresetName);
                _currentPersonaPresetIndex = _personaPresets.Count - 1;
                PersonaPresetSelectComboBox.SelectedIndex = _currentPersonaPresetIndex;
                LoadPersonaPresetToUi(newPreset);
            }
            finally
            {
                _isInitializing = false;
            }

            await SavePresetsToApiAsync();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void DuplicatePersonaPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPersonaPresetIndex < 0 || _currentPersonaPresetIndex >= _personaPresets.Count)
            {
                MessageBox.Show("複製するプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveCurrentPersonaUiToPreset();

            var source = _personaPresets[_currentPersonaPresetIndex];
            var duplicate = new PersonaPreset
            {
                PersonaPresetId = 0,
                PersonaPresetName = GenerateDuplicateName(_personaPresets.Select(p => p.PersonaPresetName), source.PersonaPresetName),
                PersonaText = source.PersonaText
            };

            _isInitializing = true;
            try
            {
                _personaPresets.Add(duplicate);
                PersonaPresetSelectComboBox.Items.Add(duplicate.PersonaPresetName);
                _currentPersonaPresetIndex = _personaPresets.Count - 1;
                PersonaPresetSelectComboBox.SelectedIndex = _currentPersonaPresetIndex;
                LoadPersonaPresetToUi(duplicate);
            }
            finally
            {
                _isInitializing = false;
            }

            await SavePresetsToApiAsync();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void DeletePersonaPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPersonaPresetIndex < 0 || _currentPersonaPresetIndex >= _personaPresets.Count)
            {
                MessageBox.Show("削除するプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_personaPresets.Count <= 1)
            {
                MessageBox.Show("最後のプリセットは削除できません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string presetName = _personaPresets[_currentPersonaPresetIndex].PersonaPresetName;
            var result = MessageBox.Show(
                $"プリセット「{presetName}」を削除しますか？",
                "削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _isInitializing = true;
            try
            {
                _personaPresets.RemoveAt(_currentPersonaPresetIndex);
                PersonaPresetSelectComboBox.Items.RemoveAt(_currentPersonaPresetIndex);

                _currentPersonaPresetIndex = Math.Min(_currentPersonaPresetIndex, _personaPresets.Count - 1);
                PersonaPresetSelectComboBox.SelectedIndex = _currentPersonaPresetIndex;
                LoadPersonaPresetToUi(_personaPresets[_currentPersonaPresetIndex]);
            }
            finally
            {
                _isInitializing = false;
            }

            await SavePresetsToApiAsync();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void AddContractPresetButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentContractUiToPreset();

            var newPreset = new ContractPreset
            {
                ContractPresetId = 0,
                ContractPresetName = GenerateUniqueName(_contractPresets.Select(p => p.ContractPresetName), "新規プリセット"),
                ContractText = string.Empty
            };

            _isInitializing = true;
            try
            {
                _contractPresets.Add(newPreset);
                ContractPresetSelectComboBox.Items.Add(newPreset.ContractPresetName);
                _currentContractPresetIndex = _contractPresets.Count - 1;
                ContractPresetSelectComboBox.SelectedIndex = _currentContractPresetIndex;
                LoadContractPresetToUi(newPreset);
            }
            finally
            {
                _isInitializing = false;
            }

            await SavePresetsToApiAsync();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void DuplicateContractPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentContractPresetIndex < 0 || _currentContractPresetIndex >= _contractPresets.Count)
            {
                MessageBox.Show("複製するプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveCurrentContractUiToPreset();

            var source = _contractPresets[_currentContractPresetIndex];
            var duplicate = new ContractPreset
            {
                ContractPresetId = 0,
                ContractPresetName = GenerateDuplicateName(_contractPresets.Select(p => p.ContractPresetName), source.ContractPresetName),
                ContractText = source.ContractText
            };

            _isInitializing = true;
            try
            {
                _contractPresets.Add(duplicate);
                ContractPresetSelectComboBox.Items.Add(duplicate.ContractPresetName);
                _currentContractPresetIndex = _contractPresets.Count - 1;
                ContractPresetSelectComboBox.SelectedIndex = _currentContractPresetIndex;
                LoadContractPresetToUi(duplicate);
            }
            finally
            {
                _isInitializing = false;
            }

            await SavePresetsToApiAsync();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async void DeleteContractPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentContractPresetIndex < 0 || _currentContractPresetIndex >= _contractPresets.Count)
            {
                MessageBox.Show("削除するプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_contractPresets.Count <= 1)
            {
                MessageBox.Show("最後のプリセットは削除できません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string presetName = _contractPresets[_currentContractPresetIndex].ContractPresetName;
            var result = MessageBox.Show(
                $"プリセット「{presetName}」を削除しますか？",
                "削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _isInitializing = true;
            try
            {
                _contractPresets.RemoveAt(_currentContractPresetIndex);
                ContractPresetSelectComboBox.Items.RemoveAt(_currentContractPresetIndex);

                _currentContractPresetIndex = Math.Min(_currentContractPresetIndex, _contractPresets.Count - 1);
                ContractPresetSelectComboBox.SelectedIndex = _currentContractPresetIndex;
                LoadContractPresetToUi(_contractPresets[_currentContractPresetIndex]);
            }
            finally
            {
                _isInitializing = false;
            }

            await SavePresetsToApiAsync();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void LoadSystemPromptPresetToUi(SystemPromptPreset preset)
        {
            SystemPromptPresetNameTextBox.Text = preset.SystemPromptPresetName;
            SystemPromptTextBox.Text = preset.SystemPrompt;
        }

        private void LoadPersonaPresetToUi(PersonaPreset preset)
        {
            PersonaPresetNameTextBox.Text = preset.PersonaPresetName;
            PersonaTextBox.Text = preset.PersonaText;
        }

        private void LoadContractPresetToUi(ContractPreset preset)
        {
            ContractPresetNameTextBox.Text = preset.ContractPresetName;
            ContractTextBox.Text = preset.ContractText;
        }

        private void ClearSystemPromptUi()
        {
            SystemPromptPresetNameTextBox.Text = string.Empty;
            SystemPromptTextBox.Text = string.Empty;
        }

        private void ClearPersonaUi()
        {
            PersonaPresetNameTextBox.Text = string.Empty;
            PersonaTextBox.Text = string.Empty;
        }

        private void ClearContractUi()
        {
            ContractPresetNameTextBox.Text = string.Empty;
            ContractTextBox.Text = string.Empty;
        }

        private void SystemPromptPresetSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SaveCurrentSystemPromptUiToPreset();

            var selectedIndex = SystemPromptPresetSelectComboBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _systemPromptPresets.Count)
            {
                return;
            }

            _currentSystemPromptPresetIndex = selectedIndex;
            _isInitializing = true;
            try
            {
                LoadSystemPromptPresetToUi(_systemPromptPresets[selectedIndex]);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void PersonaPresetSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SaveCurrentPersonaUiToPreset();

            var selectedIndex = PersonaPresetSelectComboBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _personaPresets.Count)
            {
                return;
            }

            _currentPersonaPresetIndex = selectedIndex;
            _isInitializing = true;
            try
            {
                LoadPersonaPresetToUi(_personaPresets[selectedIndex]);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ContractPresetSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SaveCurrentContractUiToPreset();

            var selectedIndex = ContractPresetSelectComboBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _contractPresets.Count)
            {
                return;
            }

            _currentContractPresetIndex = selectedIndex;
            _isInitializing = true;
            try
            {
                LoadContractPresetToUi(_contractPresets[selectedIndex]);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnSystemPromptSettingChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            if (sender == SystemPromptPresetNameTextBox && _currentSystemPromptPresetIndex >= 0 && _currentSystemPromptPresetIndex < _systemPromptPresets.Count)
            {
                _systemPromptPresets[_currentSystemPromptPresetIndex].SystemPromptPresetName = SystemPromptPresetNameTextBox.Text;
                RefreshComboBoxItems(SystemPromptPresetSelectComboBox, _systemPromptPresets.Select(p => p.SystemPromptPresetName).ToList(), _currentSystemPromptPresetIndex);
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPersonaSettingChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            if (sender == PersonaPresetNameTextBox && _currentPersonaPresetIndex >= 0 && _currentPersonaPresetIndex < _personaPresets.Count)
            {
                _personaPresets[_currentPersonaPresetIndex].PersonaPresetName = PersonaPresetNameTextBox.Text;
                RefreshComboBoxItems(PersonaPresetSelectComboBox, _personaPresets.Select(p => p.PersonaPresetName).ToList(), _currentPersonaPresetIndex);
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnContractSettingChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            if (sender == ContractPresetNameTextBox && _currentContractPresetIndex >= 0 && _currentContractPresetIndex < _contractPresets.Count)
            {
                _contractPresets[_currentContractPresetIndex].ContractPresetName = ContractPresetNameTextBox.Text;
                RefreshComboBoxItems(ContractPresetSelectComboBox, _contractPresets.Select(p => p.ContractPresetName).ToList(), _currentContractPresetIndex);
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RefreshComboBoxItems(ComboBox comboBox, IReadOnlyList<string> items, int selectedIndex)
        {
            _isInitializing = true;
            try
            {
                comboBox.Items.Clear();
                foreach (var item in items)
                {
                    comboBox.Items.Add(item);
                }

                comboBox.SelectedIndex = selectedIndex;
            }
            finally
            {
                _isInitializing = false;
            }
        }
    }
}
