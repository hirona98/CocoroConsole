using CocoroConsole.Models.OtomeKairoApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    public partial class PromptSettingsControl : UserControl
    {
        private sealed class PersonaEditorItem
        {
            public string PersonaId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string PersonaText { get; set; } = string.Empty;
            public string SecondPersonLabel { get; set; } = string.Empty;
            public string AddonText { get; set; } = string.Empty;
            public string CorePersonaJson { get; set; } = "{}";
            public string ExpressionStyleJson { get; set; } = "{\n  \"tone\": \"gentle\"\n}";
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        private readonly List<PersonaEditorItem> _personas = new();
        private bool _isInitializing;
        private int _currentPersonaIndex = -1;

        public event EventHandler? SettingsChanged;

        public PromptSettingsControl()
        {
            InitializeComponent();
        }

        public void LoadSettings(List<OtomeKairoPersonaDefinition>? personas, string? activePersonaId)
        {
            _isInitializing = true;
            try
            {
                _personas.Clear();
                PersonaSelectComboBox.Items.Clear();

                if (personas == null || personas.Count == 0)
                {
                    _currentPersonaIndex = -1;
                    ClearPersonaUi();
                    return;
                }

                foreach (var persona in personas)
                {
                    _personas.Add(ToEditorItem(persona));
                    PersonaSelectComboBox.Items.Add(persona.DisplayName);
                }

                _currentPersonaIndex = ResolveActiveIndex(_personas.Select(p => p.PersonaId).ToList(), activePersonaId);
                PersonaSelectComboBox.SelectedIndex = _currentPersonaIndex;
                LoadPersonaToUi(_personas[_currentPersonaIndex]);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public List<OtomeKairoPersonaDefinition> GetAllPersonas()
        {
            SyncCurrentPersonaFromUi();
            return _personas.Select(ToDefinition).ToList();
        }

        public string? GetActivePersonaId()
        {
            if (_currentPersonaIndex < 0 || _currentPersonaIndex >= _personas.Count)
            {
                return null;
            }

            return _personas[_currentPersonaIndex].PersonaId;
        }

        private void AddPersonaButton_Click(object sender, RoutedEventArgs e)
        {
            SyncCurrentPersonaFromUi();

            var item = new PersonaEditorItem
            {
                PersonaId = $"persona:{Guid.NewGuid():N}",
                DisplayName = GenerateUniqueName(_personas.Select(p => p.DisplayName), "新規人格設定"),
                PersonaText = string.Empty,
                SecondPersonLabel = "あなた",
                AddonText = string.Empty,
                CorePersonaJson = SerializeObject(new Dictionary<string, object?> { ["self_image"] = "long-term companion" }),
                ExpressionStyleJson = SerializeObject(new Dictionary<string, object?> { ["tone"] = "gentle" }),
            };

            _isInitializing = true;
            try
            {
                _personas.Add(item);
                PersonaSelectComboBox.Items.Add(item.DisplayName);
                _currentPersonaIndex = _personas.Count - 1;
                PersonaSelectComboBox.SelectedIndex = _currentPersonaIndex;
                LoadPersonaToUi(item);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DuplicatePersonaButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPersonaIndex < 0 || _currentPersonaIndex >= _personas.Count)
            {
                MessageBox.Show("複製する人格設定を選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SyncCurrentPersonaFromUi();
            var source = _personas[_currentPersonaIndex];
            var item = new PersonaEditorItem
            {
                PersonaId = $"persona:{Guid.NewGuid():N}",
                DisplayName = GenerateUniqueName(_personas.Select(p => p.DisplayName), $"{source.DisplayName} (コピー)"),
                PersonaText = source.PersonaText,
                SecondPersonLabel = source.SecondPersonLabel,
                AddonText = source.AddonText,
                CorePersonaJson = source.CorePersonaJson,
                ExpressionStyleJson = source.ExpressionStyleJson,
            };

            _isInitializing = true;
            try
            {
                _personas.Add(item);
                PersonaSelectComboBox.Items.Add(item.DisplayName);
                _currentPersonaIndex = _personas.Count - 1;
                PersonaSelectComboBox.SelectedIndex = _currentPersonaIndex;
                LoadPersonaToUi(item);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void DeletePersonaButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPersonaIndex < 0 || _currentPersonaIndex >= _personas.Count)
            {
                MessageBox.Show("削除する人格設定を選択してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_personas.Count <= 1)
            {
                MessageBox.Show("最後の人格設定は削除できません。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isInitializing = true;
            try
            {
                _personas.RemoveAt(_currentPersonaIndex);
                PersonaSelectComboBox.Items.RemoveAt(_currentPersonaIndex);
                _currentPersonaIndex = Math.Min(_currentPersonaIndex, _personas.Count - 1);
                PersonaSelectComboBox.SelectedIndex = _currentPersonaIndex;
                LoadPersonaToUi(_personas[_currentPersonaIndex]);
            }
            finally
            {
                _isInitializing = false;
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void PersonaSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SyncCurrentPersonaFromUi();

            var selectedIndex = PersonaSelectComboBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _personas.Count)
            {
                return;
            }

            _isInitializing = true;
            try
            {
                _currentPersonaIndex = selectedIndex;
                LoadPersonaToUi(_personas[selectedIndex]);
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

            if (sender == DisplayNameTextBox && _currentPersonaIndex >= 0 && _currentPersonaIndex < _personas.Count)
            {
                _personas[_currentPersonaIndex].DisplayName = DisplayNameTextBox.Text;
                RefreshComboBoxItems();
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SyncCurrentPersonaFromUi()
        {
            if (_currentPersonaIndex < 0 || _currentPersonaIndex >= _personas.Count)
            {
                return;
            }

            var current = _personas[_currentPersonaIndex];
            current.DisplayName = DisplayNameTextBox.Text;
            current.SecondPersonLabel = SecondPersonLabelTextBox.Text;
            current.PersonaText = PersonaTextBox.Text;
            current.AddonText = AddonTextBox.Text;
            current.CorePersonaJson = NormalizeJsonText(CorePersonaJsonTextBox.Text);
            current.ExpressionStyleJson = NormalizeJsonText(ExpressionStyleJsonTextBox.Text);
        }

        private void LoadPersonaToUi(PersonaEditorItem item)
        {
            DisplayNameTextBox.Text = item.DisplayName;
            SecondPersonLabelTextBox.Text = item.SecondPersonLabel;
            PersonaTextBox.Text = item.PersonaText;
            AddonTextBox.Text = item.AddonText;
            CorePersonaJsonTextBox.Text = NormalizeJsonText(item.CorePersonaJson);
            ExpressionStyleJsonTextBox.Text = NormalizeJsonText(item.ExpressionStyleJson);
        }

        private void ClearPersonaUi()
        {
            DisplayNameTextBox.Text = string.Empty;
            SecondPersonLabelTextBox.Text = string.Empty;
            PersonaTextBox.Text = string.Empty;
            AddonTextBox.Text = string.Empty;
            CorePersonaJsonTextBox.Text = "{}";
            ExpressionStyleJsonTextBox.Text = "{\n  \"tone\": \"gentle\"\n}";
        }

        private void RefreshComboBoxItems()
        {
            var currentIndex = _currentPersonaIndex;
            PersonaSelectComboBox.SelectionChanged -= PersonaSelectComboBox_SelectionChanged;
            PersonaSelectComboBox.Items.Clear();
            foreach (var persona in _personas)
            {
                PersonaSelectComboBox.Items.Add(persona.DisplayName);
            }
            PersonaSelectComboBox.SelectedIndex = currentIndex;
            PersonaSelectComboBox.SelectionChanged += PersonaSelectComboBox_SelectionChanged;
        }

        private static PersonaEditorItem ToEditorItem(OtomeKairoPersonaDefinition persona)
        {
            return new PersonaEditorItem
            {
                PersonaId = persona.PersonaId,
                DisplayName = persona.DisplayName,
                PersonaText = persona.PersonaText ?? string.Empty,
                SecondPersonLabel = persona.SecondPersonLabel ?? string.Empty,
                AddonText = persona.AddonText ?? string.Empty,
                CorePersonaJson = SerializeObject(persona.CorePersona ?? new Dictionary<string, object?>()),
                ExpressionStyleJson = SerializeObject(persona.ExpressionStyle ?? new Dictionary<string, object?>()),
            };
        }

        private static OtomeKairoPersonaDefinition ToDefinition(PersonaEditorItem item)
        {
            return new OtomeKairoPersonaDefinition
            {
                PersonaId = item.PersonaId,
                DisplayName = item.DisplayName,
                PersonaText = item.PersonaText,
                SecondPersonLabel = item.SecondPersonLabel,
                AddonText = item.AddonText,
                CorePersona = ParseObject(item.CorePersonaJson, $"{item.DisplayName} の core_persona"),
                ExpressionStyle = ParseObject(item.ExpressionStyleJson, $"{item.DisplayName} の expression_style"),
            };
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
