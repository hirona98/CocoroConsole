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
    public partial class PartnerMoodDebugWindow : Window
    {
        private readonly ICommunicationService _communicationService;
        private readonly DispatcherTimer _pollingTimer;
        private bool _isInitialLoad = true;

        public bool IsClosed { get; private set; }

        public PartnerMoodDebugWindow(ICommunicationService communicationService)
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
                await _communicationService.UpdatePartnerMoodOverrideAsync(request);
                SetStatus("override を設定しました（前回使用値はチャット後に更新）");
            }
            catch (Exception)
            {
                SetStatus("override 適用に失敗しました");
            }
        }

        private async void ClearOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetStatus("override 解除中...");
                await _communicationService.ClearPartnerMoodOverrideAsync();
                SetStatus("override を解除しました（前回使用値は変わりません）");
            }
            catch (Exception)
            {
                SetStatus("override 解除に失敗しました");
            }
        }

        private async Task RefreshAsync()
        {
            try
            {
                SetStatus("取得中...");
                var state = await _communicationService.GetPartnerMoodAsync();
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
                var state = await _communicationService.GetPartnerMoodAsync();
                ApplyCurrentValuesToUi(state);
            }
            catch
            {
                // ポーリング中のエラーは無視
            }
        }

        private void ApplyCurrentValuesToUi(PartnerMoodState? state)
        {
            LabelCurrentText.Text = state?.Label ?? "-";
            IntensityCurrentText.Text = FormatDouble(state?.Intensity);

            JoyCurrentText.Text = GetNumericComponentForDisplay(state, "joy");
            SadnessCurrentText.Text = GetNumericComponentForDisplay(state, "sadness");
            AngerCurrentText.Text = GetNumericComponentForDisplay(state, "anger");
            FearCurrentText.Text = GetNumericComponentForDisplay(state, "fear");

            CooperationCurrentText.Text = FormatDouble(state?.ResponsePolicy?.Cooperation);
            RefusalBiasCurrentText.Text = FormatDouble(state?.ResponsePolicy?.RefusalBias);
            RefusalAllowedCurrentText.Text = state?.ResponsePolicy?.RefusalAllowed?.ToString() ?? "-";
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.00", CultureInfo.InvariantCulture)
                : "-";
        }

        private static string GetNumericComponentForDisplay(PartnerMoodState? state, string key)
        {
            if (state?.Components == null || !state.Components.TryGetValue(key, out var value))
            {
                return "-";
            }

            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void ApplyOverrideInputsFromState(PartnerMoodState? state)
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
                IntensitySlider.Value = state.Intensity.Value;
            }

            ApplySliderComponentFromState(state, "joy", JoySlider);
            ApplySliderComponentFromState(state, "sadness", SadnessSlider);
            ApplySliderComponentFromState(state, "anger", AngerSlider);
            ApplySliderComponentFromState(state, "fear", FearSlider);

            if (state.ResponsePolicy != null)
            {
                if (state.ResponsePolicy.Cooperation.HasValue)
                {
                    CooperationSlider.Value = state.ResponsePolicy.Cooperation.Value;
                }

                if (state.ResponsePolicy.RefusalBias.HasValue)
                {
                    RefusalBiasSlider.Value = state.ResponsePolicy.RefusalBias.Value;
                }

                if (state.ResponsePolicy.RefusalAllowed.HasValue)
                {
                    RefusalAllowedCheckBox.IsChecked = state.ResponsePolicy.RefusalAllowed.Value;
                }
            }
        }

        private static void ApplySliderComponentFromState(PartnerMoodState state, string key, Slider slider)
        {
            if (state.Components == null)
            {
                return;
            }

            if (!state.Components.TryGetValue(key, out var value))
            {
                return;
            }

            slider.Value = value;
        }

        private PartnerMoodOverrideRequest BuildOverrideRequestFromUi()
        {
            // API仕様: PUT /api/persona_mood は「完全上書きのみ」
            // label/intensity/components(4種)/response_policy(3種) をすべて指定する。

            if (LabelComboBox.SelectedItem is not ComboBoxItem item || item.Content is not string label || string.IsNullOrWhiteSpace(label))
            {
                throw new FormatException("label を選択してください");
            }

            var intensity = IntensitySlider.Value;
            var joy = JoySlider.Value;
            var sadness = SadnessSlider.Value;
            var anger = AngerSlider.Value;
            var fear = FearSlider.Value;
            var cooperation = CooperationSlider.Value;
            var refusalBias = RefusalBiasSlider.Value;

            ValidateRange01(intensity, "intensity");
            ValidateRange01(joy, "joy");
            ValidateRange01(sadness, "sadness");
            ValidateRange01(anger, "anger");
            ValidateRange01(fear, "fear");
            ValidateRange01(cooperation, "cooperation");
            ValidateRange01(refusalBias, "refusal_bias");

            return new PartnerMoodOverrideRequest
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
                ResponsePolicy = new PartnerMoodResponsePolicy
                {
                    Cooperation = cooperation,
                    RefusalBias = refusalBias,
                    RefusalAllowed = RefusalAllowedCheckBox.IsChecked == true
                }
            };
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
