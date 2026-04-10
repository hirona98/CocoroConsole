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
        private static readonly string[] CorePersonaKeys =
        {
            "self_image",
            "core_values",
            "judgement_tendencies",
            "relation_baseline",
            "initiative_baseline",
            "judgement_style",
        };

        private static readonly string[] ExpressionStyleKeys =
        {
            "tone",
            "sentence_length",
            "emotional_expressiveness",
            "directness",
            "cadence",
            "initiative_expression",
        };

        private sealed class PersonaEditorItem
        {
            public string PersonaId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string PersonaText { get; set; } = string.Empty;
            public string SecondPersonLabel { get; set; } = string.Empty;
            public string AddonText { get; set; } = string.Empty;
            public string SelfImage { get; set; } = string.Empty;
            public string CoreValuesText { get; set; } = string.Empty;
            public string JudgementTendenciesText { get; set; } = string.Empty;
            public string RelationBaseline { get; set; } = string.Empty;
            public string InitiativeBaseline { get; set; } = string.Empty;
            public string Tone { get; set; } = "gentle";
            public string SentenceLength { get; set; } = string.Empty;
            public string EmotionalExpressiveness { get; set; } = string.Empty;
            public string Directness { get; set; } = string.Empty;
            public string Cadence { get; set; } = string.Empty;
            public string InitiativeExpression { get; set; } = string.Empty;
            public Dictionary<string, object?> AdditionalCorePersonaFields { get; set; } = new Dictionary<string, object?>();
            public Dictionary<string, object?> AdditionalExpressionStyleFields { get; set; } = new Dictionary<string, object?>();
        }

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
                SecondPersonLabel = "あなた",
                SelfImage = "long-term companion",
                Tone = "gentle",
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
                SelfImage = source.SelfImage,
                CoreValuesText = source.CoreValuesText,
                JudgementTendenciesText = source.JudgementTendenciesText,
                RelationBaseline = source.RelationBaseline,
                InitiativeBaseline = source.InitiativeBaseline,
                Tone = source.Tone,
                SentenceLength = source.SentenceLength,
                EmotionalExpressiveness = source.EmotionalExpressiveness,
                Directness = source.Directness,
                Cadence = source.Cadence,
                InitiativeExpression = source.InitiativeExpression,
                AdditionalCorePersonaFields = new Dictionary<string, object?>(source.AdditionalCorePersonaFields),
                AdditionalExpressionStyleFields = new Dictionary<string, object?>(source.AdditionalExpressionStyleFields),
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
            current.SelfImage = SelfImageTextBox.Text;
            current.CoreValuesText = CoreValuesTextBox.Text;
            current.JudgementTendenciesText = JudgementTendenciesTextBox.Text;
            current.RelationBaseline = RelationBaselineTextBox.Text;
            current.InitiativeBaseline = InitiativeBaselineTextBox.Text;
            current.Tone = ToneTextBox.Text;
            current.SentenceLength = SentenceLengthTextBox.Text;
            current.EmotionalExpressiveness = EmotionalExpressivenessTextBox.Text;
            current.Directness = DirectnessTextBox.Text;
            current.Cadence = CadenceTextBox.Text;
            current.InitiativeExpression = InitiativeExpressionTextBox.Text;
        }

        private void LoadPersonaToUi(PersonaEditorItem item)
        {
            DisplayNameTextBox.Text = item.DisplayName;
            SecondPersonLabelTextBox.Text = item.SecondPersonLabel;
            PersonaTextBox.Text = item.PersonaText;
            AddonTextBox.Text = item.AddonText;
            SelfImageTextBox.Text = item.SelfImage;
            CoreValuesTextBox.Text = item.CoreValuesText;
            JudgementTendenciesTextBox.Text = item.JudgementTendenciesText;
            RelationBaselineTextBox.Text = item.RelationBaseline;
            InitiativeBaselineTextBox.Text = item.InitiativeBaseline;
            ToneTextBox.Text = item.Tone;
            SentenceLengthTextBox.Text = item.SentenceLength;
            EmotionalExpressivenessTextBox.Text = item.EmotionalExpressiveness;
            DirectnessTextBox.Text = item.Directness;
            CadenceTextBox.Text = item.Cadence;
            InitiativeExpressionTextBox.Text = item.InitiativeExpression;
        }

        private void ClearPersonaUi()
        {
            DisplayNameTextBox.Text = string.Empty;
            SecondPersonLabelTextBox.Text = string.Empty;
            PersonaTextBox.Text = string.Empty;
            AddonTextBox.Text = string.Empty;
            SelfImageTextBox.Text = string.Empty;
            CoreValuesTextBox.Text = string.Empty;
            JudgementTendenciesTextBox.Text = string.Empty;
            RelationBaselineTextBox.Text = string.Empty;
            InitiativeBaselineTextBox.Text = string.Empty;
            ToneTextBox.Text = "gentle";
            SentenceLengthTextBox.Text = string.Empty;
            EmotionalExpressivenessTextBox.Text = string.Empty;
            DirectnessTextBox.Text = string.Empty;
            CadenceTextBox.Text = string.Empty;
            InitiativeExpressionTextBox.Text = string.Empty;
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
            var corePersona = persona.CorePersona ?? new Dictionary<string, object?>();
            var expressionStyle = persona.ExpressionStyle ?? new Dictionary<string, object?>();

            return new PersonaEditorItem
            {
                PersonaId = persona.PersonaId,
                DisplayName = persona.DisplayName,
                PersonaText = persona.PersonaText ?? string.Empty,
                SecondPersonLabel = persona.SecondPersonLabel ?? string.Empty,
                AddonText = persona.AddonText ?? string.Empty,
                SelfImage = ReadString(corePersona, "self_image") ?? string.Empty,
                CoreValuesText = JoinLines(ReadStringList(corePersona, "core_values")),
                JudgementTendenciesText = JoinLines(
                    ReadStringList(corePersona, "judgement_tendencies", "judgement_style")),
                RelationBaseline = ReadString(corePersona, "relation_baseline") ?? string.Empty,
                InitiativeBaseline = ReadString(corePersona, "initiative_baseline") ?? string.Empty,
                Tone = ReadString(expressionStyle, "tone") ?? "gentle",
                SentenceLength = ReadString(expressionStyle, "sentence_length") ?? string.Empty,
                EmotionalExpressiveness = ReadString(expressionStyle, "emotional_expressiveness") ?? string.Empty,
                Directness = ReadString(expressionStyle, "directness") ?? string.Empty,
                Cadence = ReadString(expressionStyle, "cadence") ?? string.Empty,
                InitiativeExpression = ReadString(expressionStyle, "initiative_expression") ?? string.Empty,
                AdditionalCorePersonaFields = CopyAdditionalFields(corePersona, CorePersonaKeys),
                AdditionalExpressionStyleFields = CopyAdditionalFields(expressionStyle, ExpressionStyleKeys),
            };
        }

        private static OtomeKairoPersonaDefinition ToDefinition(PersonaEditorItem item)
        {
            // 未知キーは落とさず保持しつつ、UIで編集する主要項目だけ明示的に組み直す。
            var corePersona = new Dictionary<string, object?>(item.AdditionalCorePersonaFields, StringComparer.OrdinalIgnoreCase);
            corePersona.Remove("judgement_style");
            SetString(corePersona, "self_image", item.SelfImage);
            SetStringList(corePersona, "core_values", SplitLines(item.CoreValuesText));
            SetStringList(corePersona, "judgement_tendencies", SplitLines(item.JudgementTendenciesText));
            SetString(corePersona, "relation_baseline", item.RelationBaseline);
            SetString(corePersona, "initiative_baseline", item.InitiativeBaseline);

            var expressionStyle = new Dictionary<string, object?>(item.AdditionalExpressionStyleFields, StringComparer.OrdinalIgnoreCase);
            SetString(expressionStyle, "tone", item.Tone);
            SetString(expressionStyle, "sentence_length", item.SentenceLength);
            SetString(expressionStyle, "emotional_expressiveness", item.EmotionalExpressiveness);
            SetString(expressionStyle, "directness", item.Directness);
            SetString(expressionStyle, "cadence", item.Cadence);
            SetString(expressionStyle, "initiative_expression", item.InitiativeExpression);

            return new OtomeKairoPersonaDefinition
            {
                PersonaId = item.PersonaId,
                DisplayName = item.DisplayName,
                PersonaText = item.PersonaText,
                SecondPersonLabel = item.SecondPersonLabel,
                AddonText = item.AddonText,
                CorePersona = corePersona,
                ExpressionStyle = expressionStyle,
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

        private static List<string> ReadStringList(
            IDictionary<string, object?> values,
            string key,
            string? aliasKey = null)
        {
            if (TryReadStringList(values, key, out var lines))
            {
                return lines;
            }

            if (!string.IsNullOrWhiteSpace(aliasKey) && TryReadStringList(values, aliasKey, out lines))
            {
                return lines;
            }

            return new List<string>();
        }

        private static bool TryReadStringList(
            IDictionary<string, object?> values,
            string key,
            out List<string> lines)
        {
            lines = new List<string>();
            if (!values.TryGetValue(key, out var value) || value == null)
            {
                return false;
            }

            if (value is string text)
            {
                lines = SplitLines(text);
                return true;
            }

            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    lines = element.EnumerateArray()
                        .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => item!.Trim())
                        .ToList();
                    return true;
                }

                if (element.ValueKind == JsonValueKind.String)
                {
                    lines = SplitLines(element.GetString() ?? string.Empty);
                    return true;
                }
            }

            return false;
        }

        private static void SetString(IDictionary<string, object?> values, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                values.Remove(key);
                return;
            }

            values[key] = value.Trim();
        }

        private static void SetStringList(IDictionary<string, object?> values, string key, List<string> items)
        {
            if (items.Count == 0)
            {
                values.Remove(key);
                return;
            }

            values[key] = items;
        }

        private static List<string> SplitLines(string value)
        {
            return value
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        private static string JoinLines(IEnumerable<string> values)
        {
            return string.Join(Environment.NewLine, values);
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
