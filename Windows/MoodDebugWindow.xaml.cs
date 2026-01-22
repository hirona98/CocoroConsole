using CocoroConsole.Models.CocoroGhostApi;
using CocoroConsole.Services;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;

namespace CocoroConsole.Windows
{
    /// <summary>
    /// cocoro_ghost の /api/mood/debug を表示するデバッグウィンドウ。
    ///
    /// - ウィンドウが開いている間だけ 1 秒ポーリングする
    /// - 取得処理自体は ICommunicationService に委譲し、UI は表示に専念する
    /// </summary>
    public partial class MoodDebugWindow : Window
    {
        private readonly ICommunicationService _communicationService;
        private readonly MoodDebugViewModel _viewModel;
        private bool _handlersAttached;

        public bool IsClosed { get; private set; }

        public MoodDebugWindow(ICommunicationService communicationService)
        {
            InitializeComponent();

            // --- 依存の注入 ---
            _communicationService = communicationService ?? throw new ArgumentNullException(nameof(communicationService));

            // --- 画面用VM（バインディング） ---
            _viewModel = new MoodDebugViewModel();
            DataContext = _viewModel;

            // --- ウィンドウライフサイクル ---
            Loaded += MoodDebugWindow_Loaded;
            Closed += MoodDebugWindow_Closed;
        }

        private void MoodDebugWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // --- イベント購読（多重付与防止） ---
            if (!_handlersAttached)
            {
                _communicationService.MoodDebugUpdated += OnMoodDebugUpdated;
                _communicationService.MoodDebugError += OnMoodDebugError;
                _handlersAttached = true;
            }

            // --- ポーリング開始（UIスレッドはブロックしない） ---
            _viewModel.SetStatus("感情デバッグ取得中...", null);
            _ = _communicationService.StartMoodDebugPollingAsync();
        }

        private async void MoodDebugWindow_Closed(object? sender, EventArgs e)
        {
            IsClosed = true;

            // --- イベント購読解除 ---
            if (_handlersAttached)
            {
                _communicationService.MoodDebugUpdated -= OnMoodDebugUpdated;
                _communicationService.MoodDebugError -= OnMoodDebugError;
                _handlersAttached = false;
            }

            // --- ポーリング停止（閉じる動作を阻害しない） ---
            try
            {
                await _communicationService.StopMoodDebugPollingAsync();
            }
            catch
            {
                // --- 破棄中の例外は無視 ---
            }
        }

        private void OnMoodDebugUpdated(object? sender, MoodDebugUpdatedEventArgs e)
        {
            // --- UIスレッドへ反映 ---
            Dispatcher.Invoke(() =>
            {
                _viewModel.UpdateFrom(e.Response, e.ReceivedAt);
            });
        }

        private void OnMoodDebugError(object? sender, string error)
        {
            // --- UIスレッドへ反映（エラーはステータスバーのみ更新） ---
            Dispatcher.Invoke(() =>
            {
                _viewModel.SetStatus("エラー", error);
            });
        }
    }

    /// <summary>
    /// MoodDebugWindow 用のViewModel。
    /// </summary>
    public class MoodDebugViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        // --- 基本情報 ---
        private string _stateId = "-";
        private string _bodyText = "（未取得）";
        private string _confidence = "-";
        private string _salience = "-";
        private string _dtSeconds = "-";
        private string _lastConfirmedAt = "-";
        private string _now = "-";

        // --- VAD ---
        private string _baselineV = "-";
        private string _baselineA = "-";
        private string _baselineD = "-";
        private string _shockV = "-";
        private string _shockA = "-";
        private string _shockD = "-";
        private string _vadV = "-";
        private string _vadA = "-";
        private string _vadD = "-";

        // --- ステータス ---
        private string _statusMessage = "待機中";
        private string _lastUpdatedText = string.Empty;

        public string StateId { get => _stateId; set => SetProperty(ref _stateId, value); }
        public string BodyText { get => _bodyText; set => SetProperty(ref _bodyText, value); }
        public string Confidence { get => _confidence; set => SetProperty(ref _confidence, value); }
        public string Salience { get => _salience; set => SetProperty(ref _salience, value); }
        public string DtSeconds { get => _dtSeconds; set => SetProperty(ref _dtSeconds, value); }
        public string LastConfirmedAt { get => _lastConfirmedAt; set => SetProperty(ref _lastConfirmedAt, value); }
        public string Now { get => _now; set => SetProperty(ref _now, value); }

        public string BaselineV { get => _baselineV; set => SetProperty(ref _baselineV, value); }
        public string BaselineA { get => _baselineA; set => SetProperty(ref _baselineA, value); }
        public string BaselineD { get => _baselineD; set => SetProperty(ref _baselineD, value); }
        public string ShockV { get => _shockV; set => SetProperty(ref _shockV, value); }
        public string ShockA { get => _shockA; set => SetProperty(ref _shockA, value); }
        public string ShockD { get => _shockD; set => SetProperty(ref _shockD, value); }
        public string VadV { get => _vadV; set => SetProperty(ref _vadV, value); }
        public string VadA { get => _vadA; set => SetProperty(ref _vadA, value); }
        public string VadD { get => _vadD; set => SetProperty(ref _vadD, value); }

        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
        public string LastUpdatedText { get => _lastUpdatedText; set => SetProperty(ref _lastUpdatedText, value); }

        /// <summary>
        /// 受信した /api/mood/debug を表示用に反映する。
        /// </summary>
        public void UpdateFrom(MoodDebugResponse response, DateTimeOffset receivedAt)
        {
            // --- 表示の既定値 ---
            var mood = response?.Mood;
            if (mood == null)
            {
                StateId = "-";
                BodyText = "（未作成: long_mood_state が存在しません）";
                Confidence = "-";
                Salience = "-";
                DtSeconds = "-";
                LastConfirmedAt = "-";
                Now = "-";

                BaselineV = "-";
                BaselineA = "-";
                BaselineD = "-";
                ShockV = "-";
                ShockA = "-";
                ShockD = "-";
                VadV = "-";
                VadA = "-";
                VadD = "-";
                SetStatus("取得成功", null, receivedAt);
                return;
            }

            // --- 基本情報 ---
            StateId = mood.StateId.ToString(CultureInfo.InvariantCulture);
            BodyText = mood.BodyText ?? string.Empty;
            Confidence = FormatNumber(mood.Confidence);
            Salience = FormatNumber(mood.Salience);
            DtSeconds = mood.DtSeconds.ToString(CultureInfo.InvariantCulture);
            LastConfirmedAt = FormatDateTimeOffset(mood.LastConfirmedAt);
            Now = FormatDateTimeOffset(mood.Now);

            // --- VAD ---
            BaselineV = FormatVadValue(mood.BaselineVad?.V);
            BaselineA = FormatVadValue(mood.BaselineVad?.A);
            BaselineD = FormatVadValue(mood.BaselineVad?.D);

            ShockV = FormatVadValue(mood.ShockVad?.V);
            ShockA = FormatVadValue(mood.ShockVad?.A);
            ShockD = FormatVadValue(mood.ShockVad?.D);

            VadV = FormatVadValue(mood.Vad?.V);
            VadA = FormatVadValue(mood.Vad?.A);
            VadD = FormatVadValue(mood.Vad?.D);

            // --- ステータス ---
            SetStatus("取得成功", null, receivedAt);
        }

        /// <summary>
        /// ステータスバーに表示する文言を更新する。
        /// </summary>
        public void SetStatus(string status, string? detail, DateTimeOffset? receivedAt = null)
        {
            // --- ステータス本文 ---
            StatusMessage = string.IsNullOrWhiteSpace(detail) ? status : $"{status}: {detail}";

            // --- 最終更新 ---
            if (receivedAt.HasValue)
            {
                LastUpdatedText = $"最終更新: {receivedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
            }
        }

        private static string FormatVadValue(double? value)
        {
            // --- null の場合は "-" ---
            if (!value.HasValue)
            {
                return "-";
            }

            return FormatNumber(value.Value);
        }

        private static string FormatNumber(double value)
        {
            // --- UIは日本語だが、数値はデバッグ用途のため小数点は "." を固定する ---
            return value.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static string FormatDateTimeOffset(DateTimeOffset value)
        {
            // --- 例: 2026-01-10 14:06:59 +09:00 ---
            return value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            // --- 同値なら通知しない ---
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
