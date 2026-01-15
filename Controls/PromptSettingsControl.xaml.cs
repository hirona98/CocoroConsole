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

        private readonly List<PersonaPreset> _personaPresets = new();
        private readonly List<AddonPreset> _addonPresets = new();

        private int _currentPersonaPresetIndex = -1;
        private int _currentAddonPresetIndex = -1;

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
            List<PersonaPreset>? personaPresets,
            string? activePersonaPresetId,
            List<AddonPreset>? addonPresets,
            string? activeAddonPresetId
        )
        {
            _isInitializing = true;
            try
            {
                LoadPersonaPresets(personaPresets, activePersonaPresetId);
                LoadAddonPresets(addonPresets, activeAddonPresetId);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public List<PersonaPreset> GetAllPersonaPresets()
        {
            SaveCurrentPersonaUiToPreset();
            return _personaPresets.ToList();
        }

        public List<AddonPreset> GetAllAddonPresets()
        {
            SaveCurrentAddonUiToPreset();
            return _addonPresets.ToList();
        }

        public string? GetActivePersonaPresetId()
        {
            if (_currentPersonaPresetIndex < 0 || _currentPersonaPresetIndex >= _personaPresets.Count)
            {
                return null;
            }

            return _personaPresets[_currentPersonaPresetIndex].PersonaPresetId;
        }

        public string? GetActiveAddonPresetId()
        {
            if (_currentAddonPresetIndex < 0 || _currentAddonPresetIndex >= _addonPresets.Count)
            {
                return null;
            }

            return _addonPresets[_currentAddonPresetIndex].AddonPresetId;
        }

        private void LoadPersonaPresets(List<PersonaPreset>? presets, string? activePresetId)
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

        private void LoadAddonPresets(List<AddonPreset>? presets, string? activePresetId)
        {
            _addonPresets.Clear();
            AddonPresetSelectComboBox.Items.Clear();

            if (presets == null || presets.Count == 0)
            {
                _currentAddonPresetIndex = -1;
                ClearAddonUi();
                return;
            }

            _addonPresets.AddRange(presets);
            foreach (var preset in _addonPresets)
            {
                AddonPresetSelectComboBox.Items.Add(preset.AddonPresetName);
            }

            var activeIndex = ResolveActiveIndex(_addonPresets.Select(p => p.AddonPresetId).ToList(), activePresetId);
            _currentAddonPresetIndex = activeIndex;
            AddonPresetSelectComboBox.SelectedIndex = activeIndex;
            LoadAddonPresetToUi(_addonPresets[activeIndex]);
        }

        private static int ResolveActiveIndex(IReadOnlyList<string?> presetIds, string? activePresetId)
        {
            if (presetIds.Count == 0)
            {
                return -1;
            }

            if (string.IsNullOrWhiteSpace(activePresetId))
            {
                return 0;
            }

            for (var i = 0; i < presetIds.Count; i++)
            {
                if (string.Equals(presetIds[i], activePresetId, StringComparison.OrdinalIgnoreCase))
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

        private void SaveCurrentPersonaUiToPreset()
        {
            if (_currentPersonaPresetIndex < 0 || _currentPersonaPresetIndex >= _personaPresets.Count)
            {
                return;
            }

            var preset = _personaPresets[_currentPersonaPresetIndex];
            preset.PersonaPresetName = PersonaPresetNameTextBox.Text;
            preset.PersonaText = PersonaTextBox.Text;
            preset.SecondPersonLabel = SecondPersonLabelTextBox.Text;
        }

        private void SaveCurrentAddonUiToPreset()
        {
            if (_currentAddonPresetIndex < 0 || _currentAddonPresetIndex >= _addonPresets.Count)
            {
                return;
            }

            var preset = _addonPresets[_currentAddonPresetIndex];
            preset.AddonPresetName = AddonPresetNameTextBox.Text;
            preset.AddonText = AddonTextBox.Text;
        }

        private void AddPersonaPresetButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentPersonaUiToPreset();

            var newPreset = new PersonaPreset
            {
                PersonaPresetId = Guid.NewGuid().ToString(),
                PersonaPresetName = GenerateUniqueName(_personaPresets.Select(p => p.PersonaPresetName), "新規プリセット"),
                PersonaText = string.Empty,
                SecondPersonLabel = string.Empty
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

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DuplicatePersonaPresetButton_Click(object sender, RoutedEventArgs e)
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
                PersonaPresetId = Guid.NewGuid().ToString(),
                PersonaPresetName = GenerateDuplicateName(_personaPresets.Select(p => p.PersonaPresetName), source.PersonaPresetName),
                PersonaText = source.PersonaText,
                SecondPersonLabel = source.SecondPersonLabel
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

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DeletePersonaPresetButton_Click(object sender, RoutedEventArgs e)
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

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void AddAddonPresetButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentAddonUiToPreset();

            var newPreset = new AddonPreset
            {
                AddonPresetId = Guid.NewGuid().ToString(),
                AddonPresetName = GenerateUniqueName(_addonPresets.Select(p => p.AddonPresetName), "新規プリセット"),
                AddonText = string.Empty
            };

            _isInitializing = true;
            try
            {
                _addonPresets.Add(newPreset);
                AddonPresetSelectComboBox.Items.Add(newPreset.AddonPresetName);
                _currentAddonPresetIndex = _addonPresets.Count - 1;
                AddonPresetSelectComboBox.SelectedIndex = _currentAddonPresetIndex;
                LoadAddonPresetToUi(newPreset);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DuplicateAddonPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAddonPresetIndex < 0 || _currentAddonPresetIndex >= _addonPresets.Count)
            {
                MessageBox.Show("複製するプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveCurrentAddonUiToPreset();

            var source = _addonPresets[_currentAddonPresetIndex];
            var duplicate = new AddonPreset
            {
                AddonPresetId = Guid.NewGuid().ToString(),
                AddonPresetName = GenerateDuplicateName(_addonPresets.Select(p => p.AddonPresetName), source.AddonPresetName),
                AddonText = source.AddonText
            };

            _isInitializing = true;
            try
            {
                _addonPresets.Add(duplicate);
                AddonPresetSelectComboBox.Items.Add(duplicate.AddonPresetName);
                _currentAddonPresetIndex = _addonPresets.Count - 1;
                AddonPresetSelectComboBox.SelectedIndex = _currentAddonPresetIndex;
                LoadAddonPresetToUi(duplicate);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DeleteAddonPresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAddonPresetIndex < 0 || _currentAddonPresetIndex >= _addonPresets.Count)
            {
                MessageBox.Show("削除するプリセットを選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_addonPresets.Count <= 1)
            {
                MessageBox.Show("最後のプリセットは削除できません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isInitializing = true;
            try
            {
                _addonPresets.RemoveAt(_currentAddonPresetIndex);
                AddonPresetSelectComboBox.Items.RemoveAt(_currentAddonPresetIndex);

                _currentAddonPresetIndex = Math.Min(_currentAddonPresetIndex, _addonPresets.Count - 1);
                AddonPresetSelectComboBox.SelectedIndex = _currentAddonPresetIndex;
                LoadAddonPresetToUi(_addonPresets[_currentAddonPresetIndex]);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void LoadPersonaPresetToUi(PersonaPreset preset)
        {
            PersonaPresetNameTextBox.Text = preset.PersonaPresetName;
            PersonaTextBox.Text = preset.PersonaText;
            SecondPersonLabelTextBox.Text = preset.SecondPersonLabel;
        }

        private void LoadAddonPresetToUi(AddonPreset preset)
        {
            AddonPresetNameTextBox.Text = preset.AddonPresetName;
            AddonTextBox.Text = preset.AddonText;
        }

        private void ClearPersonaUi()
        {
            PersonaPresetNameTextBox.Text = string.Empty;
            PersonaTextBox.Text = string.Empty;
            SecondPersonLabelTextBox.Text = string.Empty;
        }

        private void ClearAddonUi()
        {
            AddonPresetNameTextBox.Text = string.Empty;
            AddonTextBox.Text = string.Empty;
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

        private void AddonPresetSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SaveCurrentAddonUiToPreset();

            var selectedIndex = AddonPresetSelectComboBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _addonPresets.Count)
            {
                return;
            }

            _currentAddonPresetIndex = selectedIndex;
            _isInitializing = true;
            try
            {
                LoadAddonPresetToUi(_addonPresets[selectedIndex]);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPersonaSettingChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            if (_currentPersonaPresetIndex >= 0 && _currentPersonaPresetIndex < _personaPresets.Count)
            {
                if (sender == PersonaPresetNameTextBox)
                {
                    _personaPresets[_currentPersonaPresetIndex].PersonaPresetName = PersonaPresetNameTextBox.Text;
                    RefreshComboBoxItems(PersonaPresetSelectComboBox, _personaPresets.Select(p => p.PersonaPresetName).ToList(), _currentPersonaPresetIndex);
                }
                else if (sender == PersonaTextBox)
                {
                    _personaPresets[_currentPersonaPresetIndex].PersonaText = PersonaTextBox.Text;
                }
                else if (sender == SecondPersonLabelTextBox)
                {
                    _personaPresets[_currentPersonaPresetIndex].SecondPersonLabel = SecondPersonLabelTextBox.Text;
                }
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnAddonSettingChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            if (sender == AddonPresetNameTextBox && _currentAddonPresetIndex >= 0 && _currentAddonPresetIndex < _addonPresets.Count)
            {
                _addonPresets[_currentAddonPresetIndex].AddonPresetName = AddonPresetNameTextBox.Text;
                RefreshComboBoxItems(AddonPresetSelectComboBox, _addonPresets.Select(p => p.AddonPresetName).ToList(), _currentAddonPresetIndex);
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
