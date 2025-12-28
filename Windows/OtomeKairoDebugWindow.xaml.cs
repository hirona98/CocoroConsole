using CocoroConsole.Models.CocoroGhostApi;
using CocoroConsole.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CocoroConsole.Windows
{
    public partial class OtomeKairoDebugWindow : Window
    {
        private readonly ICommunicationService _communicationService;
        private readonly DispatcherTimer _pollingTimer;
        private bool _isInitialLoad = true;

        public bool IsClosed { get; private set; }

        public OtomeKairoDebugWindow(ICommunicationService communicationService)
        {
            _communicationService = communicationService ?? throw new ArgumentNullException(nameof(communicationService));

            InitializeComponent();

            _pollingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _pollingTimer.Tick += async (_, __) => await PollAsync();

            Loaded += async (_, __) =>
            {
                await RefreshAsync();
                _pollingTimer.Start();
            };
            Closed += (_, __) =>
            {
                _pollingTimer.Stop();
                IsClosed = true;
            };
        }

        private async void ApplyOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var request = BuildOverrideRequestFromUi();
                SetStatus("override 適用中...");
                var state = await _communicationService.UpdateOtomeKairoOverrideAsync(request);
                ApplyCurrentValuesToUi(state);
                SetStatus("override を適用しました");
            }
            catch (Exception)
            {
                SetStatus("override 適用に失敗しました");
            }
        }

        private async Task RefreshAsync()
        {
            try
            {
                SetStatus("取得中...");
                var state = await _communicationService.GetOtomeKairoAsync();
                ApplyCurrentValuesToUi(state);
                if (_isInitialLoad)
                {
                    ApplyOverrideInputsFromState(state);
                    _isInitialLoad = false;
                }
                SetStatus("取得しました");
            }
            catch (Exception)
            {
                SetStatus("取得に失敗しました");
            }
        }

        private async Task PollAsync()
        {
            try
            {
                var state = await _communicationService.GetOtomeKairoAsync();
                ApplyCurrentValuesToUi(state);
            }
            catch
            {
                // ポーリング中のエラーは無視
            }
        }

        private void ApplyCurrentValuesToUi(OtomeKairoState? state)
        {
            LabelCurrentText.Text = state?.Label ?? "-";
            IntensityCurrentText.Text = FormatDouble(state?.Intensity);

            JoyCurrentText.Text = GetNumericComponentForDisplay(state, "joy");
            SadnessCurrentText.Text = GetNumericComponentForDisplay(state, "sadness");
            AngerCurrentText.Text = GetNumericComponentForDisplay(state, "anger");
            FearCurrentText.Text = GetNumericComponentForDisplay(state, "fear");

            CooperationCurrentText.Text = FormatDouble(state?.Policy?.Cooperation);
            RefusalBiasCurrentText.Text = FormatDouble(state?.Policy?.RefusalBias);
            RefusalAllowedCurrentText.Text = state?.Policy?.RefusalAllowed?.ToString() ?? "-";
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
                : "-";
        }

        private static string GetNumericComponentForDisplay(OtomeKairoState? state, string key)
        {
            if (state?.Components == null || !state.Components.TryGetValue(key, out var value))
            {
                return "-";
            }

            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void ApplyOverrideInputsFromState(OtomeKairoState? state)
        {
            if (state == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(state.Label))
            {
                foreach (var obj in LabelComboBox.Items)
                {
                    if (obj is ComboBoxItem item && item.Content is string content && content == state.Label)
                    {
                        LabelComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            if (state.Intensity.HasValue)
            {
                IntensityTextBox.Text = state.Intensity.Value.ToString("0.###", CultureInfo.InvariantCulture);
            }

            JoyTextBox.Text = GetNumericComponentForEdit(state, "joy");
            SadnessTextBox.Text = GetNumericComponentForEdit(state, "sadness");
            AngerTextBox.Text = GetNumericComponentForEdit(state, "anger");
            FearTextBox.Text = GetNumericComponentForEdit(state, "fear");

            if (state.Policy != null)
            {
                if (state.Policy.Cooperation.HasValue)
                {
                    CooperationTextBox.Text = state.Policy.Cooperation.Value.ToString("0.###", CultureInfo.InvariantCulture);
                }

                if (state.Policy.RefusalBias.HasValue)
                {
                    RefusalBiasTextBox.Text = state.Policy.RefusalBias.Value.ToString("0.###", CultureInfo.InvariantCulture);
                }

                if (state.Policy.RefusalAllowed.HasValue)
                {
                    RefusalAllowedCheckBox.IsChecked = state.Policy.RefusalAllowed.Value;
                }
            }
        }

        private static string GetNumericComponentForEdit(OtomeKairoState state, string key)
        {
            if (state.Components == null)
            {
                return string.Empty;
            }

            if (!state.Components.TryGetValue(key, out var value))
            {
                return string.Empty;
            }

            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private OtomeKairoOverrideRequest BuildOverrideRequestFromUi()
        {
            // API仕様: PUT /api/otome_kairo は「完全上書きのみ」
            // label/intensity/components(4種)/policy(3種) をすべて指定する。

            if (LabelComboBox.SelectedItem is not ComboBoxItem item || item.Content is not string label || string.IsNullOrWhiteSpace(label))
            {
                throw new FormatException("label を選択してください");
            }

            var intensity = ParseRequiredDouble(IntensityTextBox.Text, "intensity");
            ValidateRange01(intensity, "intensity");

            var joy = ParseRequiredDouble(JoyTextBox.Text, "joy");
            var sadness = ParseRequiredDouble(SadnessTextBox.Text, "sadness");
            var anger = ParseRequiredDouble(AngerTextBox.Text, "anger");
            var fear = ParseRequiredDouble(FearTextBox.Text, "fear");
            ValidateRange01(joy, "joy");
            ValidateRange01(sadness, "sadness");
            ValidateRange01(anger, "anger");
            ValidateRange01(fear, "fear");

            var cooperation = ParseRequiredDouble(CooperationTextBox.Text, "cooperation");
            var refusalBias = ParseRequiredDouble(RefusalBiasTextBox.Text, "refusal_bias");
            ValidateRange01(cooperation, "cooperation");
            ValidateRange01(refusalBias, "refusal_bias");

            return new OtomeKairoOverrideRequest
            {
                Label = label,
                Intensity = intensity,
                Components = new Dictionary<string, double>
                {
                    ["joy"] = joy,
                    ["sadness"] = sadness,
                    ["anger"] = anger,
                    ["fear"] = fear
                },
                Policy = new OtomeKairoPolicy
                {
                    Cooperation = cooperation,
                    RefusalBias = refusalBias,
                    RefusalAllowed = RefusalAllowedCheckBox.IsChecked == true
                }
            };
        }

        private static double? TryParseNullableDouble(string? text, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            throw new FormatException($"{fieldName} は数値（例: 0.8）で入力してください");
        }

        private static double ParseRequiredDouble(string? text, string fieldName)
        {
            var value = TryParseNullableDouble(text, fieldName);
            if (!value.HasValue)
            {
                throw new FormatException($"{fieldName} を入力してください");
            }

            return value.Value;
        }

        private static void ValidateRange01(double value, string fieldName)
        {
            if (value < 0.0 || value > 1.0)
            {
                throw new FormatException($"{fieldName} は 0..1 の範囲で入力してください");
            }
        }

        private void SetStatus(string message)
        {
            StatusText.Text = message;
        }
    }
}
