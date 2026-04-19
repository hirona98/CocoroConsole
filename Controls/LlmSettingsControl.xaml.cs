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
        private static readonly string[] KnownRoleNames =
        {
            "input_interpretation",
            "decision_generation",
            "expression_generation",
            "memory_interpretation",
            "memory_reflection_summary",
            "event_evidence_generation",
            "recall_pack_selection",
            "pending_intent_selection",
        };

        private static readonly string[] KnownRoleFieldKeys =
        {
            "model",
            "api_base",
            "api_key",
            "reasoning_effort",
            "max_output_tokens",
            "web_search_enabled",
        };

        private const string DefaultGenerationModel = "openrouter/google/gemini-3.1-flash-lite-preview";
        private const int DefaultRecentTurnLimit = 30;
        private const int DefaultRecentTurnMinutes = 3;
        private const int DefaultObservationMaxOutputTokens = 3000;
        private const int DefaultDecisionMaxOutputTokens = 3000;
        private const int DefaultExpressionMaxOutputTokens = 4000;
        private const int DefaultMemoryMaxOutputTokens = 3000;
        private const int DefaultReflectionSummaryMaxOutputTokens = 2000;
        private const int DefaultEventEvidenceMaxOutputTokens = 1500;
        private const int DefaultRecallSelectionMaxOutputTokens = 2000;
        private const int DefaultPendingIntentMaxOutputTokens = 1000;

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
            public Dictionary<string, object?> AdditionalFields { get; set; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
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
            public RoleEditorItem ReflectionSummaryRole { get; set; } = new RoleEditorItem();
            public RoleEditorItem EventEvidenceRole { get; set; } = new RoleEditorItem();
            public RoleEditorItem RecallSelectionRole { get; set; } = new RoleEditorItem();
            public RoleEditorItem PendingIntentRole { get; set; } = new RoleEditorItem();
            public bool ObservationUsesExpressionModel { get; set; } = true;
            public bool DecisionUsesExpressionModel { get; set; } = true;
            public bool MemoryUsesExpressionModel { get; set; } = true;
            public bool ReflectionSummaryUsesExpressionModel { get; set; } = true;
            public bool EventEvidenceUsesExpressionModel { get; set; } = true;
            public bool RecallSelectionUsesExpressionModel { get; set; } = true;
            public bool PendingIntentUsesExpressionModel { get; set; } = true;
            public Dictionary<string, Dictionary<string, object?>> AdditionalRoles { get; set; } =
                new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
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
                // XAML の読み込み中に共有モデル用チェックボックスのイベントが先に飛ぶため、
                // すべてのコントロール生成後にまとめて状態を反映する。
                InitializeComponent();
                ApplyExpressionModelUsageToUi(null, refreshAllSharedTexts: true);
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
                current.ReflectionSummaryRole.ApiKey,
                current.EventEvidenceRole.ApiKey,
                current.RecallSelectionRole.ApiKey,
                current.PendingIntentRole.ApiKey,
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

            var item = CreateDefaultPreset(
                $"model_preset:{Guid.NewGuid():N}",
                GenerateUniqueName(_presets.Select(p => p.DisplayName), "新規モデルプリセット"));

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
                ReflectionSummaryRole = CloneRole(source.ReflectionSummaryRole),
                EventEvidenceRole = CloneRole(source.EventEvidenceRole),
                RecallSelectionRole = CloneRole(source.RecallSelectionRole),
                PendingIntentRole = CloneRole(source.PendingIntentRole),
                ObservationUsesExpressionModel = source.ObservationUsesExpressionModel,
                DecisionUsesExpressionModel = source.DecisionUsesExpressionModel,
                MemoryUsesExpressionModel = source.MemoryUsesExpressionModel,
                ReflectionSummaryUsesExpressionModel = source.ReflectionSummaryUsesExpressionModel,
                EventEvidenceUsesExpressionModel = source.EventEvidenceUsesExpressionModel,
                RecallSelectionUsesExpressionModel = source.RecallSelectionUsesExpressionModel,
                PendingIntentUsesExpressionModel = source.PendingIntentUsesExpressionModel,
                AdditionalRoles = CloneRoleDefinitions(source.AdditionalRoles),
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

            var current = GetCurrentPreset();
            if (current != null)
            {
                SyncEditedLinkedFieldToCurrentItem(current, sender);
            }

            if (sender == PresetNameTextBox && current != null)
            {
                current.DisplayName = PresetNameTextBox.Text;
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

        private void UseExpressionModelCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            var current = GetCurrentPreset();
            if (current != null)
            {
                PreserveCustomSharedFieldsBeforeLinking(current, sender);
                UpdateExpressionModelUsageFromUi(current);
                ApplyExpressionModelUsageToUi(current, refreshAllSharedTexts: true);
            }
            else
            {
                ApplyExpressionModelUsageToUi(null, refreshAllSharedTexts: true);
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
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

        private void ReflectionSummaryApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(ReflectionSummaryApiKeyTextBox);
        }

        private void ReflectionSummaryApiKeyPasteButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(ReflectionSummaryApiKeyTextBox);
        }

        private void EventEvidenceApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(EventEvidenceApiKeyTextBox);
        }

        private void EventEvidenceApiKeyPasteButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(EventEvidenceApiKeyTextBox);
        }

        private void RecallSelectionApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(RecallSelectionApiKeyTextBox);
        }

        private void RecallSelectionApiKeyPasteButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(RecallSelectionApiKeyTextBox);
        }

        private void PendingIntentApiKeyCopyButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.CopyToClipboard(PendingIntentApiKeyTextBox);
        }

        private void PendingIntentApiKeyPasteButton_Click(object sender, RoutedEventArgs e)
        {
            ClipboardPasteOverride.PasteOverwrite(PendingIntentApiKeyTextBox);
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
            UpdateExpressionModelUsageFromUi(current);
            SyncRoleFromUi(
                current.ExpressionRole,
                ExpressionModelTextBox,
                ExpressionApiBaseTextBox,
                ExpressionApiKeyTextBox,
                ExpressionReasoningEffortTextBox,
                ExpressionMaxOutputTokensTextBox,
                ExpressionWebSearchCheckBox);
            SyncRoleFromUi(
                current.ObservationRole,
                ObservationModelTextBox,
                ObservationApiBaseTextBox,
                ObservationApiKeyTextBox,
                ObservationReasoningEffortTextBox,
                ObservationMaxOutputTokensTextBox,
                ObservationWebSearchCheckBox,
                syncSharedFieldsFromUi: !current.ObservationUsesExpressionModel);
            SyncRoleFromUi(
                current.DecisionRole,
                DecisionModelTextBox,
                DecisionApiBaseTextBox,
                DecisionApiKeyTextBox,
                DecisionReasoningEffortTextBox,
                DecisionMaxOutputTokensTextBox,
                DecisionWebSearchCheckBox,
                syncSharedFieldsFromUi: !current.DecisionUsesExpressionModel);
            SyncRoleFromUi(
                current.MemoryRole,
                MemoryModelTextBox,
                MemoryApiBaseTextBox,
                MemoryApiKeyTextBox,
                MemoryReasoningEffortTextBox,
                MemoryMaxOutputTokensTextBox,
                MemoryWebSearchCheckBox,
                syncSharedFieldsFromUi: !current.MemoryUsesExpressionModel);
            SyncRoleFromUi(
                current.ReflectionSummaryRole,
                ReflectionSummaryModelTextBox,
                ReflectionSummaryApiBaseTextBox,
                ReflectionSummaryApiKeyTextBox,
                ReflectionSummaryReasoningEffortTextBox,
                ReflectionSummaryMaxOutputTokensTextBox,
                ReflectionSummaryWebSearchCheckBox,
                syncSharedFieldsFromUi: !current.ReflectionSummaryUsesExpressionModel);
            SyncRoleFromUi(
                current.EventEvidenceRole,
                EventEvidenceModelTextBox,
                EventEvidenceApiBaseTextBox,
                EventEvidenceApiKeyTextBox,
                EventEvidenceReasoningEffortTextBox,
                EventEvidenceMaxOutputTokensTextBox,
                EventEvidenceWebSearchCheckBox,
                syncSharedFieldsFromUi: !current.EventEvidenceUsesExpressionModel);
            SyncRoleFromUi(
                current.RecallSelectionRole,
                RecallSelectionModelTextBox,
                RecallSelectionApiBaseTextBox,
                RecallSelectionApiKeyTextBox,
                RecallSelectionReasoningEffortTextBox,
                RecallSelectionMaxOutputTokensTextBox,
                RecallSelectionWebSearchCheckBox,
                syncSharedFieldsFromUi: !current.RecallSelectionUsesExpressionModel);
            SyncRoleFromUi(
                current.PendingIntentRole,
                PendingIntentModelTextBox,
                PendingIntentApiBaseTextBox,
                PendingIntentApiKeyTextBox,
                PendingIntentReasoningEffortTextBox,
                PendingIntentMaxOutputTokensTextBox,
                PendingIntentWebSearchCheckBox,
                syncSharedFieldsFromUi: !current.PendingIntentUsesExpressionModel);
        }

        private void LoadPresetToUi(ModelPresetEditorItem item)
        {
            PresetNameTextBox.Text = item.DisplayName;
            RecentTurnLimitTextBox.Text = item.PromptWindow.RecentTurnLimitText;
            RecentTurnMinutesTextBox.Text = item.PromptWindow.RecentTurnMinutesText;
            LoadRoleToUi(
                item.ExpressionRole,
                ExpressionModelTextBox,
                ExpressionApiBaseTextBox,
                ExpressionApiKeyTextBox,
                ExpressionReasoningEffortTextBox,
                ExpressionMaxOutputTokensTextBox,
                ExpressionWebSearchCheckBox);
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
                item.MemoryRole,
                MemoryModelTextBox,
                MemoryApiBaseTextBox,
                MemoryApiKeyTextBox,
                MemoryReasoningEffortTextBox,
                MemoryMaxOutputTokensTextBox,
                MemoryWebSearchCheckBox);
            LoadRoleToUi(
                item.ReflectionSummaryRole,
                ReflectionSummaryModelTextBox,
                ReflectionSummaryApiBaseTextBox,
                ReflectionSummaryApiKeyTextBox,
                ReflectionSummaryReasoningEffortTextBox,
                ReflectionSummaryMaxOutputTokensTextBox,
                ReflectionSummaryWebSearchCheckBox);
            LoadRoleToUi(
                item.EventEvidenceRole,
                EventEvidenceModelTextBox,
                EventEvidenceApiBaseTextBox,
                EventEvidenceApiKeyTextBox,
                EventEvidenceReasoningEffortTextBox,
                EventEvidenceMaxOutputTokensTextBox,
                EventEvidenceWebSearchCheckBox);
            LoadRoleToUi(
                item.RecallSelectionRole,
                RecallSelectionModelTextBox,
                RecallSelectionApiBaseTextBox,
                RecallSelectionApiKeyTextBox,
                RecallSelectionReasoningEffortTextBox,
                RecallSelectionMaxOutputTokensTextBox,
                RecallSelectionWebSearchCheckBox);
            LoadRoleToUi(
                item.PendingIntentRole,
                PendingIntentModelTextBox,
                PendingIntentApiBaseTextBox,
                PendingIntentApiKeyTextBox,
                PendingIntentReasoningEffortTextBox,
                PendingIntentMaxOutputTokensTextBox,
                PendingIntentWebSearchCheckBox);
            LoadExpressionModelUsageToUi(item);
            ApplyExpressionModelUsageToUi(item, refreshAllSharedTexts: true);
        }

        private void ClearUi()
        {
            PresetNameTextBox.Text = string.Empty;
            RecentTurnLimitTextBox.Text = string.Empty;
            RecentTurnMinutesTextBox.Text = string.Empty;
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
                MemoryModelTextBox,
                MemoryApiBaseTextBox,
                MemoryApiKeyTextBox,
                MemoryReasoningEffortTextBox,
                MemoryMaxOutputTokensTextBox,
                MemoryWebSearchCheckBox);
            LoadRoleToUi(
                CreateBlankRole(),
                ReflectionSummaryModelTextBox,
                ReflectionSummaryApiBaseTextBox,
                ReflectionSummaryApiKeyTextBox,
                ReflectionSummaryReasoningEffortTextBox,
                ReflectionSummaryMaxOutputTokensTextBox,
                ReflectionSummaryWebSearchCheckBox);
            LoadRoleToUi(
                CreateBlankRole(),
                EventEvidenceModelTextBox,
                EventEvidenceApiBaseTextBox,
                EventEvidenceApiKeyTextBox,
                EventEvidenceReasoningEffortTextBox,
                EventEvidenceMaxOutputTokensTextBox,
                EventEvidenceWebSearchCheckBox);
            LoadRoleToUi(
                CreateBlankRole(),
                RecallSelectionModelTextBox,
                RecallSelectionApiBaseTextBox,
                RecallSelectionApiKeyTextBox,
                RecallSelectionReasoningEffortTextBox,
                RecallSelectionMaxOutputTokensTextBox,
                RecallSelectionWebSearchCheckBox);
            LoadRoleToUi(
                CreateBlankRole(),
                PendingIntentModelTextBox,
                PendingIntentApiBaseTextBox,
                PendingIntentApiKeyTextBox,
                PendingIntentReasoningEffortTextBox,
                PendingIntentMaxOutputTokensTextBox,
                PendingIntentWebSearchCheckBox);
            ObservationUseExpressionModelCheckBox.IsChecked = true;
            DecisionUseExpressionModelCheckBox.IsChecked = true;
            MemoryUseExpressionModelCheckBox.IsChecked = true;
            ReflectionSummaryUseExpressionModelCheckBox.IsChecked = true;
            EventEvidenceUseExpressionModelCheckBox.IsChecked = true;
            RecallSelectionUseExpressionModelCheckBox.IsChecked = true;
            PendingIntentUseExpressionModelCheckBox.IsChecked = true;
            ApplyExpressionModelUsageToUi(null, refreshAllSharedTexts: true);
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
            var expressionRole = ToRoleEditorItem(GetRole(preset, "expression_generation"));
            var observationRole = ToRoleEditorItem(GetRole(preset, "input_interpretation"));
            var decisionRole = ToRoleEditorItem(GetRole(preset, "decision_generation"));
            var memoryRole = ToRoleEditorItem(GetRole(preset, "memory_interpretation"));
            var reflectionSummaryRole = ToRoleEditorItem(GetRole(preset, "memory_reflection_summary"));
            var eventEvidenceRole = ToRoleEditorItem(GetRole(preset, "event_evidence_generation"));
            var recallSelectionRole = ToRoleEditorItem(GetRole(preset, "recall_pack_selection"));
            var pendingIntentRole = ToRoleEditorItem(GetRole(preset, "pending_intent_selection"));

            return new ModelPresetEditorItem
            {
                ModelPresetId = preset.ModelPresetId,
                DisplayName = preset.DisplayName,
                PromptWindow = new PromptWindowEditorItem
                {
                    RecentTurnLimitText = preset.PromptWindow.RecentTurnLimit > 0 ? preset.PromptWindow.RecentTurnLimit.ToString() : string.Empty,
                    RecentTurnMinutesText = preset.PromptWindow.RecentTurnMinutes > 0 ? preset.PromptWindow.RecentTurnMinutes.ToString() : string.Empty,
                },
                ObservationRole = observationRole,
                DecisionRole = decisionRole,
                ExpressionRole = expressionRole,
                MemoryRole = memoryRole,
                ReflectionSummaryRole = reflectionSummaryRole,
                EventEvidenceRole = eventEvidenceRole,
                RecallSelectionRole = recallSelectionRole,
                PendingIntentRole = pendingIntentRole,
                ObservationUsesExpressionModel = ShouldUseExpressionModel(observationRole, expressionRole),
                DecisionUsesExpressionModel = ShouldUseExpressionModel(decisionRole, expressionRole),
                MemoryUsesExpressionModel = ShouldUseExpressionModel(memoryRole, expressionRole),
                ReflectionSummaryUsesExpressionModel = ShouldUseExpressionModel(reflectionSummaryRole, expressionRole),
                EventEvidenceUsesExpressionModel = ShouldUseExpressionModel(eventEvidenceRole, expressionRole),
                RecallSelectionUsesExpressionModel = ShouldUseExpressionModel(recallSelectionRole, expressionRole),
                PendingIntentUsesExpressionModel = ShouldUseExpressionModel(pendingIntentRole, expressionRole),
                AdditionalRoles = CopyAdditionalRoles(preset.Roles),
            };
        }

        private static OtomeKairoModelPresetDefinition ToDefinition(ModelPresetEditorItem item)
        {
            var roles = CloneRoleDefinitions(item.AdditionalRoles);
            roles["expression_generation"] = ToRoleDefinition(item.ExpressionRole);
            roles["input_interpretation"] = ToRoleDefinition(item.ObservationRole, item.ObservationUsesExpressionModel ? item.ExpressionRole : null);
            roles["decision_generation"] = ToRoleDefinition(item.DecisionRole, item.DecisionUsesExpressionModel ? item.ExpressionRole : null);
            roles["memory_interpretation"] = ToRoleDefinition(item.MemoryRole, item.MemoryUsesExpressionModel ? item.ExpressionRole : null);
            roles["memory_reflection_summary"] = ToRoleDefinition(item.ReflectionSummaryRole, item.ReflectionSummaryUsesExpressionModel ? item.ExpressionRole : null);
            roles["event_evidence_generation"] = ToRoleDefinition(item.EventEvidenceRole, item.EventEvidenceUsesExpressionModel ? item.ExpressionRole : null);
            roles["recall_pack_selection"] = ToRoleDefinition(item.RecallSelectionRole, item.RecallSelectionUsesExpressionModel ? item.ExpressionRole : null);
            roles["pending_intent_selection"] = ToRoleDefinition(item.PendingIntentRole, item.PendingIntentUsesExpressionModel ? item.ExpressionRole : null);

            return new OtomeKairoModelPresetDefinition
            {
                ModelPresetId = item.ModelPresetId,
                DisplayName = item.DisplayName,
                PromptWindow = new OtomeKairoPromptWindowDefinition
                {
                    RecentTurnLimit = ParseRequiredPositiveIntOrZero(item.PromptWindow.RecentTurnLimitText),
                    RecentTurnMinutes = ParseRequiredPositiveIntOrZero(item.PromptWindow.RecentTurnMinutesText),
                },
                Roles = roles,
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
                AdditionalFields = CopyAdditionalFields(role, KnownRoleFieldKeys),
            };
        }

        private static Dictionary<string, object?> ToRoleDefinition(RoleEditorItem item, RoleEditorItem? sharedRole = null)
        {
            var effectiveModel = NormalizeComparableField(sharedRole?.Model ?? item.Model);
            var effectiveApiBase = NormalizeComparableField(sharedRole?.ApiBase ?? item.ApiBase);
            var effectiveApiKey = sharedRole?.ApiKey ?? item.ApiKey ?? string.Empty;
            var definition = new Dictionary<string, object?>(item.AdditionalFields, StringComparer.OrdinalIgnoreCase)
            {
                ["model"] = effectiveModel,
                ["api_key"] = effectiveApiKey,
                ["web_search_enabled"] = item.WebSearchEnabled,
            };

            if (!string.IsNullOrWhiteSpace(effectiveApiBase))
            {
                definition["api_base"] = effectiveApiBase;
            }
            else
            {
                definition.Remove("api_base");
            }

            var reasoningEffort = item.ReasoningEffort?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(reasoningEffort))
            {
                definition["reasoning_effort"] = reasoningEffort;
            }
            else
            {
                definition.Remove("reasoning_effort");
            }

            var maxOutputTokens = ParseOptionalPositiveInt(item.MaxOutputTokensText);
            if (maxOutputTokens.HasValue)
            {
                definition["max_output_tokens"] = maxOutputTokens.Value;
            }
            else
            {
                definition.Remove("max_output_tokens");
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
            CheckBox webSearchCheckBox,
            bool syncSharedFieldsFromUi = true)
        {
            if (syncSharedFieldsFromUi)
            {
                role.Model = modelTextBox.Text;
                role.ApiBase = apiBaseTextBox.Text;
                role.ApiKey = apiKeyTextBox.Text;
            }
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
                AdditionalFields = CloneRoleDefinition(role.AdditionalFields),
            };
        }

        private static RoleEditorItem CreateBlankRole()
        {
            return new RoleEditorItem();
        }

        private static ModelPresetEditorItem CreateDefaultPreset(string modelPresetId, string displayName)
        {
            // OtomeKairo の既定モデルプリセットと同じ初期値で、新規追加直後でも保存可能な状態を作る。
            return new ModelPresetEditorItem
            {
                ModelPresetId = modelPresetId,
                DisplayName = displayName,
                PromptWindow = CreateDefaultPromptWindow(),
                ObservationRole = CreateDefaultRole(DefaultObservationMaxOutputTokens, reasoningEffort: "low"),
                DecisionRole = CreateDefaultRole(DefaultDecisionMaxOutputTokens),
                ExpressionRole = CreateDefaultRole(DefaultExpressionMaxOutputTokens),
                MemoryRole = CreateDefaultRole(DefaultMemoryMaxOutputTokens),
                ReflectionSummaryRole = CreateDefaultRole(DefaultReflectionSummaryMaxOutputTokens),
                EventEvidenceRole = CreateDefaultRole(DefaultEventEvidenceMaxOutputTokens),
                RecallSelectionRole = CreateDefaultRole(DefaultRecallSelectionMaxOutputTokens),
                PendingIntentRole = CreateDefaultRole(DefaultPendingIntentMaxOutputTokens),
            };
        }

        private static PromptWindowEditorItem CreateDefaultPromptWindow()
        {
            return new PromptWindowEditorItem
            {
                RecentTurnLimitText = DefaultRecentTurnLimit.ToString(),
                RecentTurnMinutesText = DefaultRecentTurnMinutes.ToString(),
            };
        }

        private static RoleEditorItem CreateDefaultRole(int maxOutputTokens, string reasoningEffort = "")
        {
            return new RoleEditorItem
            {
                Model = DefaultGenerationModel,
                ApiBase = string.Empty,
                ApiKey = string.Empty,
                ReasoningEffort = reasoningEffort,
                MaxOutputTokensText = maxOutputTokens.ToString(),
                WebSearchEnabled = false,
            };
        }

        private ModelPresetEditorItem? GetCurrentPreset()
        {
            if (_currentPresetIndex < 0 || _currentPresetIndex >= _presets.Count)
            {
                return null;
            }

            return _presets[_currentPresetIndex];
        }

        private void SyncEditedLinkedFieldToCurrentItem(ModelPresetEditorItem current, object sender)
        {
            if (sender == ExpressionModelTextBox)
            {
                current.ExpressionRole.Model = ExpressionModelTextBox.Text;
                ApplyExpressionModelUsageToUi(current, refreshAllSharedTexts: false);
                return;
            }

            if (sender == ExpressionApiBaseTextBox)
            {
                current.ExpressionRole.ApiBase = ExpressionApiBaseTextBox.Text;
                ApplyExpressionModelUsageToUi(current, refreshAllSharedTexts: false);
                return;
            }

            if (sender == ExpressionApiKeyTextBox)
            {
                current.ExpressionRole.ApiKey = ExpressionApiKeyTextBox.Text;
                ApplyExpressionModelUsageToUi(current, refreshAllSharedTexts: false);
                return;
            }

            if (TrySyncLinkedRoleField(
                sender,
                current.ObservationRole,
                current.ObservationUsesExpressionModel,
                ObservationModelTextBox,
                ObservationApiBaseTextBox,
                ObservationApiKeyTextBox))
            {
                return;
            }

            if (TrySyncLinkedRoleField(
                sender,
                current.DecisionRole,
                current.DecisionUsesExpressionModel,
                DecisionModelTextBox,
                DecisionApiBaseTextBox,
                DecisionApiKeyTextBox))
            {
                return;
            }

            if (TrySyncLinkedRoleField(
                sender,
                current.MemoryRole,
                current.MemoryUsesExpressionModel,
                MemoryModelTextBox,
                MemoryApiBaseTextBox,
                MemoryApiKeyTextBox))
            {
                return;
            }

            if (TrySyncLinkedRoleField(
                sender,
                current.ReflectionSummaryRole,
                current.ReflectionSummaryUsesExpressionModel,
                ReflectionSummaryModelTextBox,
                ReflectionSummaryApiBaseTextBox,
                ReflectionSummaryApiKeyTextBox))
            {
                return;
            }

            if (TrySyncLinkedRoleField(
                sender,
                current.EventEvidenceRole,
                current.EventEvidenceUsesExpressionModel,
                EventEvidenceModelTextBox,
                EventEvidenceApiBaseTextBox,
                EventEvidenceApiKeyTextBox))
            {
                return;
            }

            if (TrySyncLinkedRoleField(
                sender,
                current.RecallSelectionRole,
                current.RecallSelectionUsesExpressionModel,
                RecallSelectionModelTextBox,
                RecallSelectionApiBaseTextBox,
                RecallSelectionApiKeyTextBox))
            {
                return;
            }

            TrySyncLinkedRoleField(
                sender,
                current.PendingIntentRole,
                current.PendingIntentUsesExpressionModel,
                PendingIntentModelTextBox,
                PendingIntentApiBaseTextBox,
                PendingIntentApiKeyTextBox);
        }

        private void PreserveCustomSharedFieldsBeforeLinking(ModelPresetEditorItem current, object sender)
        {
            if (TryPreserveCustomSharedFieldsBeforeLinking(
                sender,
                current.ObservationRole,
                ObservationUseExpressionModelCheckBox,
                ObservationModelTextBox,
                ObservationApiBaseTextBox,
                ObservationApiKeyTextBox))
            {
                return;
            }

            if (TryPreserveCustomSharedFieldsBeforeLinking(
                sender,
                current.DecisionRole,
                DecisionUseExpressionModelCheckBox,
                DecisionModelTextBox,
                DecisionApiBaseTextBox,
                DecisionApiKeyTextBox))
            {
                return;
            }

            if (TryPreserveCustomSharedFieldsBeforeLinking(
                sender,
                current.MemoryRole,
                MemoryUseExpressionModelCheckBox,
                MemoryModelTextBox,
                MemoryApiBaseTextBox,
                MemoryApiKeyTextBox))
            {
                return;
            }

            if (TryPreserveCustomSharedFieldsBeforeLinking(
                sender,
                current.ReflectionSummaryRole,
                ReflectionSummaryUseExpressionModelCheckBox,
                ReflectionSummaryModelTextBox,
                ReflectionSummaryApiBaseTextBox,
                ReflectionSummaryApiKeyTextBox))
            {
                return;
            }

            if (TryPreserveCustomSharedFieldsBeforeLinking(
                sender,
                current.EventEvidenceRole,
                EventEvidenceUseExpressionModelCheckBox,
                EventEvidenceModelTextBox,
                EventEvidenceApiBaseTextBox,
                EventEvidenceApiKeyTextBox))
            {
                return;
            }

            if (TryPreserveCustomSharedFieldsBeforeLinking(
                sender,
                current.RecallSelectionRole,
                RecallSelectionUseExpressionModelCheckBox,
                RecallSelectionModelTextBox,
                RecallSelectionApiBaseTextBox,
                RecallSelectionApiKeyTextBox))
            {
                return;
            }

            TryPreserveCustomSharedFieldsBeforeLinking(
                sender,
                current.PendingIntentRole,
                PendingIntentUseExpressionModelCheckBox,
                PendingIntentModelTextBox,
                PendingIntentApiBaseTextBox,
                PendingIntentApiKeyTextBox);
        }

        private void LoadExpressionModelUsageToUi(ModelPresetEditorItem item)
        {
            ObservationUseExpressionModelCheckBox.IsChecked = item.ObservationUsesExpressionModel;
            DecisionUseExpressionModelCheckBox.IsChecked = item.DecisionUsesExpressionModel;
            MemoryUseExpressionModelCheckBox.IsChecked = item.MemoryUsesExpressionModel;
            ReflectionSummaryUseExpressionModelCheckBox.IsChecked = item.ReflectionSummaryUsesExpressionModel;
            EventEvidenceUseExpressionModelCheckBox.IsChecked = item.EventEvidenceUsesExpressionModel;
            RecallSelectionUseExpressionModelCheckBox.IsChecked = item.RecallSelectionUsesExpressionModel;
            PendingIntentUseExpressionModelCheckBox.IsChecked = item.PendingIntentUsesExpressionModel;
        }

        private void UpdateExpressionModelUsageFromUi(ModelPresetEditorItem item)
        {
            item.ObservationUsesExpressionModel = ObservationUseExpressionModelCheckBox.IsChecked ?? true;
            item.DecisionUsesExpressionModel = DecisionUseExpressionModelCheckBox.IsChecked ?? true;
            item.MemoryUsesExpressionModel = MemoryUseExpressionModelCheckBox.IsChecked ?? true;
            item.ReflectionSummaryUsesExpressionModel = ReflectionSummaryUseExpressionModelCheckBox.IsChecked ?? true;
            item.EventEvidenceUsesExpressionModel = EventEvidenceUseExpressionModelCheckBox.IsChecked ?? true;
            item.RecallSelectionUsesExpressionModel = RecallSelectionUseExpressionModelCheckBox.IsChecked ?? true;
            item.PendingIntentUsesExpressionModel = PendingIntentUseExpressionModelCheckBox.IsChecked ?? true;
        }

        private void ApplyExpressionModelUsageToUi(ModelPresetEditorItem? item, bool refreshAllSharedTexts)
        {
            if (!AreExpressionModelControlsReady())
            {
                return;
            }

            var expressionModel = ExpressionModelTextBox.Text;
            var expressionApiBase = ExpressionApiBaseTextBox.Text;
            var expressionApiKey = ExpressionApiKeyTextBox.Text;
            var wasInitializing = _isInitializing;
            _isInitializing = true;
            try
            {
                ApplyRoleModelUsageToUi(
                    ObservationModelTextBox,
                    ObservationApiBaseTextBox,
                    ObservationApiKeyTextBox,
                    item?.ObservationRole.Model ?? string.Empty,
                    item?.ObservationRole.ApiBase ?? string.Empty,
                    item?.ObservationRole.ApiKey ?? string.Empty,
                    expressionModel,
                    expressionApiBase,
                    expressionApiKey,
                    item?.ObservationUsesExpressionModel ?? (ObservationUseExpressionModelCheckBox.IsChecked ?? true),
                    refreshAllSharedTexts);
                ApplyRoleModelUsageToUi(
                    DecisionModelTextBox,
                    DecisionApiBaseTextBox,
                    DecisionApiKeyTextBox,
                    item?.DecisionRole.Model ?? string.Empty,
                    item?.DecisionRole.ApiBase ?? string.Empty,
                    item?.DecisionRole.ApiKey ?? string.Empty,
                    expressionModel,
                    expressionApiBase,
                    expressionApiKey,
                    item?.DecisionUsesExpressionModel ?? (DecisionUseExpressionModelCheckBox.IsChecked ?? true),
                    refreshAllSharedTexts);
                ApplyRoleModelUsageToUi(
                    MemoryModelTextBox,
                    MemoryApiBaseTextBox,
                    MemoryApiKeyTextBox,
                    item?.MemoryRole.Model ?? string.Empty,
                    item?.MemoryRole.ApiBase ?? string.Empty,
                    item?.MemoryRole.ApiKey ?? string.Empty,
                    expressionModel,
                    expressionApiBase,
                    expressionApiKey,
                    item?.MemoryUsesExpressionModel ?? (MemoryUseExpressionModelCheckBox.IsChecked ?? true),
                    refreshAllSharedTexts);
                ApplyRoleModelUsageToUi(
                    ReflectionSummaryModelTextBox,
                    ReflectionSummaryApiBaseTextBox,
                    ReflectionSummaryApiKeyTextBox,
                    item?.ReflectionSummaryRole.Model ?? string.Empty,
                    item?.ReflectionSummaryRole.ApiBase ?? string.Empty,
                    item?.ReflectionSummaryRole.ApiKey ?? string.Empty,
                    expressionModel,
                    expressionApiBase,
                    expressionApiKey,
                    item?.ReflectionSummaryUsesExpressionModel ?? (ReflectionSummaryUseExpressionModelCheckBox.IsChecked ?? true),
                    refreshAllSharedTexts);
                ApplyRoleModelUsageToUi(
                    EventEvidenceModelTextBox,
                    EventEvidenceApiBaseTextBox,
                    EventEvidenceApiKeyTextBox,
                    item?.EventEvidenceRole.Model ?? string.Empty,
                    item?.EventEvidenceRole.ApiBase ?? string.Empty,
                    item?.EventEvidenceRole.ApiKey ?? string.Empty,
                    expressionModel,
                    expressionApiBase,
                    expressionApiKey,
                    item?.EventEvidenceUsesExpressionModel ?? (EventEvidenceUseExpressionModelCheckBox.IsChecked ?? true),
                    refreshAllSharedTexts);
                ApplyRoleModelUsageToUi(
                    RecallSelectionModelTextBox,
                    RecallSelectionApiBaseTextBox,
                    RecallSelectionApiKeyTextBox,
                    item?.RecallSelectionRole.Model ?? string.Empty,
                    item?.RecallSelectionRole.ApiBase ?? string.Empty,
                    item?.RecallSelectionRole.ApiKey ?? string.Empty,
                    expressionModel,
                    expressionApiBase,
                    expressionApiKey,
                    item?.RecallSelectionUsesExpressionModel ?? (RecallSelectionUseExpressionModelCheckBox.IsChecked ?? true),
                    refreshAllSharedTexts);
                ApplyRoleModelUsageToUi(
                    PendingIntentModelTextBox,
                    PendingIntentApiBaseTextBox,
                    PendingIntentApiKeyTextBox,
                    item?.PendingIntentRole.Model ?? string.Empty,
                    item?.PendingIntentRole.ApiBase ?? string.Empty,
                    item?.PendingIntentRole.ApiKey ?? string.Empty,
                    expressionModel,
                    expressionApiBase,
                    expressionApiKey,
                    item?.PendingIntentUsesExpressionModel ?? (PendingIntentUseExpressionModelCheckBox.IsChecked ?? true),
                    refreshAllSharedTexts);
            }
            finally
            {
                _isInitializing = wasInitializing;
            }
        }

        private bool AreExpressionModelControlsReady()
        {
            return ExpressionModelTextBox != null &&
                ExpressionApiBaseTextBox != null &&
                ExpressionApiKeyTextBox != null &&
                ObservationModelTextBox != null &&
                ObservationApiBaseTextBox != null &&
                ObservationApiKeyTextBox != null &&
                DecisionModelTextBox != null &&
                DecisionApiBaseTextBox != null &&
                DecisionApiKeyTextBox != null &&
                MemoryModelTextBox != null &&
                MemoryApiBaseTextBox != null &&
                MemoryApiKeyTextBox != null &&
                ReflectionSummaryModelTextBox != null &&
                ReflectionSummaryApiBaseTextBox != null &&
                ReflectionSummaryApiKeyTextBox != null &&
                EventEvidenceModelTextBox != null &&
                EventEvidenceApiBaseTextBox != null &&
                EventEvidenceApiKeyTextBox != null &&
                RecallSelectionModelTextBox != null &&
                RecallSelectionApiBaseTextBox != null &&
                RecallSelectionApiKeyTextBox != null &&
                PendingIntentModelTextBox != null &&
                PendingIntentApiBaseTextBox != null &&
                PendingIntentApiKeyTextBox != null &&
                ObservationUseExpressionModelCheckBox != null &&
                DecisionUseExpressionModelCheckBox != null &&
                MemoryUseExpressionModelCheckBox != null &&
                ReflectionSummaryUseExpressionModelCheckBox != null &&
                EventEvidenceUseExpressionModelCheckBox != null &&
                RecallSelectionUseExpressionModelCheckBox != null &&
                PendingIntentUseExpressionModelCheckBox != null;
        }

        private static void ApplyRoleModelUsageToUi(
            TextBox modelTextBox,
            TextBox apiBaseTextBox,
            TextBox apiKeyTextBox,
            string customModel,
            string customApiBase,
            string customApiKey,
            string expressionModel,
            string expressionApiBase,
            string expressionApiKey,
            bool useExpressionModel,
            bool refreshAllSharedTexts)
        {
            modelTextBox.IsEnabled = !useExpressionModel;
            apiBaseTextBox.IsEnabled = !useExpressionModel;
            apiKeyTextBox.IsEnabled = !useExpressionModel;
            if (apiKeyTextBox.Parent is UIElement apiKeyContainer)
            {
                apiKeyContainer.IsEnabled = !useExpressionModel;
            }

            if (useExpressionModel)
            {
                modelTextBox.Text = expressionModel;
                apiBaseTextBox.Text = expressionApiBase;
                apiKeyTextBox.Text = expressionApiKey;
            }
            else if (refreshAllSharedTexts)
            {
                modelTextBox.Text = customModel;
                apiBaseTextBox.Text = customApiBase;
                apiKeyTextBox.Text = customApiKey;
            }
        }

        private static bool ShouldUseExpressionModel(RoleEditorItem role, RoleEditorItem expressionRole)
        {
            return string.Equals(
                NormalizeComparableField(role.Model),
                NormalizeComparableField(expressionRole.Model),
                StringComparison.Ordinal) &&
                string.Equals(
                    NormalizeComparableField(role.ApiBase),
                    NormalizeComparableField(expressionRole.ApiBase),
                    StringComparison.Ordinal) &&
                string.Equals(
                    role.ApiKey ?? string.Empty,
                    expressionRole.ApiKey ?? string.Empty,
                    StringComparison.Ordinal);
        }

        private static bool TrySyncLinkedRoleField(
            object sender,
            RoleEditorItem role,
            bool usesExpressionModel,
            TextBox modelTextBox,
            TextBox apiBaseTextBox,
            TextBox apiKeyTextBox)
        {
            if (usesExpressionModel)
            {
                return false;
            }

            if (sender == modelTextBox)
            {
                role.Model = modelTextBox.Text;
                return true;
            }

            if (sender == apiBaseTextBox)
            {
                role.ApiBase = apiBaseTextBox.Text;
                return true;
            }

            if (sender == apiKeyTextBox)
            {
                role.ApiKey = apiKeyTextBox.Text;
                return true;
            }

            return false;
        }

        private static bool TryPreserveCustomSharedFieldsBeforeLinking(
            object sender,
            RoleEditorItem role,
            CheckBox useExpressionModelCheckBox,
            TextBox modelTextBox,
            TextBox apiBaseTextBox,
            TextBox apiKeyTextBox)
        {
            if (sender != useExpressionModelCheckBox || !(useExpressionModelCheckBox.IsChecked ?? false))
            {
                return false;
            }

            role.Model = modelTextBox.Text;
            role.ApiBase = apiBaseTextBox.Text;
            role.ApiKey = apiKeyTextBox.Text;
            return true;
        }

        private static string NormalizeComparableField(string? value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private static Dictionary<string, object?> CopyAdditionalFields(
            IDictionary<string, object?> values,
            IEnumerable<string> excludedKeys)
        {
            var excluded = new HashSet<string>(excludedKeys, StringComparer.OrdinalIgnoreCase);
            return values
                .Where(pair => !excluded.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, Dictionary<string, object?>> CopyAdditionalRoles(
            IDictionary<string, Dictionary<string, object?>>? roles)
        {
            var additionalRoles = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
            if (roles == null)
            {
                return additionalRoles;
            }

            foreach (var role in roles)
            {
                if (KnownRoleNames.Contains(role.Key, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                additionalRoles[role.Key] = CloneRoleDefinition(role.Value);
            }

            return additionalRoles;
        }

        private static Dictionary<string, Dictionary<string, object?>> CloneRoleDefinitions(
            IDictionary<string, Dictionary<string, object?>>? roles)
        {
            var cloned = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
            if (roles == null)
            {
                return cloned;
            }

            foreach (var role in roles)
            {
                cloned[role.Key] = CloneRoleDefinition(role.Value);
            }

            return cloned;
        }

        private static Dictionary<string, object?> CloneRoleDefinition(IDictionary<string, object?>? role)
        {
            var cloned = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (role == null)
            {
                return cloned;
            }

            foreach (var field in role)
            {
                cloned[field.Key] = field.Value;
            }

            return cloned;
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
