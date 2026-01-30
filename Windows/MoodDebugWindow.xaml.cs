using CocoroConsole.Models.CocoroGhostApi;
using CocoroConsole.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

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

        private readonly object _uiUpdateLock = new object();
        private MoodDebugDisplayData? _pendingDisplayData;
        private DateTimeOffset _pendingReceivedAt;
        private bool _isUiUpdateScheduled;

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
            // --- 破棄済みなら反映しない ---
            if (IsClosed)
            {
                return;
            }

            // --- 表示用データへ変換（UIスレッドを重くしないため、ここで整形まで済ませる） ---
            var displayData = MoodDebugDisplayData.Create(e.Response);

            // --- UI更新は間引く（多重BeginInvokeを避けて、最後の1件だけ反映する） ---
            lock (_uiUpdateLock)
            {
                _pendingDisplayData = displayData;
                _pendingReceivedAt = e.ReceivedAt;
                if (_isUiUpdateScheduled)
                {
                    return;
                }

                _isUiUpdateScheduled = true;
            }

            Dispatcher.BeginInvoke(new Action(ApplyPendingUiUpdate), DispatcherPriority.Background);
        }

        private void OnMoodDebugError(object? sender, string error)
        {
            // --- 破棄済みなら反映しない ---
            if (IsClosed)
            {
                return;
            }

            // --- UIスレッドへ反映（エラーはステータスバーのみ更新、同期Invokeは避ける） ---
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _viewModel.SetStatus("エラー", error);
            }), DispatcherPriority.Background);
        }

        private void ApplyPendingUiUpdate()
        {
            MoodDebugDisplayData? displayData;
            DateTimeOffset receivedAt;

            // --- 直近のデータを取り出してフラグを戻す ---
            lock (_uiUpdateLock)
            {
                displayData = _pendingDisplayData;
                receivedAt = _pendingReceivedAt;
                _pendingDisplayData = null;
                _isUiUpdateScheduled = false;
            }

            if (displayData == null)
            {
                return;
            }

            // --- UI反映は軽量に（文字列/配列の参照を差し替えるだけ） ---
            _viewModel.Apply(displayData, receivedAt);
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
	        private string _dtSeconds = "-";
	        private string _lastConfirmedAt = "-";

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

        // --- recent_affects（直近の瞬間感情） ---
        private MoodDebugRecentAffectRowViewModel _latestAffect = MoodDebugRecentAffectRowViewModel.CreateEmpty();

        // --- ステータス ---
        private string _statusMessage = "待機中";
        private string _lastUpdatedText = string.Empty;

        public string StateId { get => _stateId; set => SetProperty(ref _stateId, value); }
        public string BodyText { get => _bodyText; set => SetProperty(ref _bodyText, value); }
	        public string Confidence { get => _confidence; set => SetProperty(ref _confidence, value); }
	        public string DtSeconds { get => _dtSeconds; set => SetProperty(ref _dtSeconds, value); }
	        public string LastConfirmedAt { get => _lastConfirmedAt; set => SetProperty(ref _lastConfirmedAt, value); }

        public string BaselineV { get => _baselineV; set => SetProperty(ref _baselineV, value); }
        public string BaselineA { get => _baselineA; set => SetProperty(ref _baselineA, value); }
        public string BaselineD { get => _baselineD; set => SetProperty(ref _baselineD, value); }
        public string ShockV { get => _shockV; set => SetProperty(ref _shockV, value); }
        public string ShockA { get => _shockA; set => SetProperty(ref _shockA, value); }
        public string ShockD { get => _shockD; set => SetProperty(ref _shockD, value); }
        public string VadV { get => _vadV; set => SetProperty(ref _vadV, value); }
        public string VadA { get => _vadA; set => SetProperty(ref _vadA, value); }
        public string VadD { get => _vadD; set => SetProperty(ref _vadD, value); }

        public MoodDebugRecentAffectRowViewModel LatestAffect { get => _latestAffect; set => SetProperty(ref _latestAffect, value); }

        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
        public string LastUpdatedText { get => _lastUpdatedText; set => SetProperty(ref _lastUpdatedText, value); }

        /// <summary>
        /// 表示用に整形済みのデータをUIへ反映する。
        /// </summary>
	        public void Apply(MoodDebugDisplayData data, DateTimeOffset receivedAt)
	        {
	            // --- 直近の瞬間感情（最新1件） ---
	            LatestAffect = data.LatestAffect ?? MoodDebugRecentAffectRowViewModel.CreateEmpty();

	            // --- 基本情報 ---
	            StateId = data.StateId;
	            BodyText = data.BodyText;
	            Confidence = data.Confidence;
	            DtSeconds = data.DtSeconds;
	            LastConfirmedAt = data.LastConfirmedAt;

            // --- VAD ---
            BaselineV = data.BaselineV;
            BaselineA = data.BaselineA;
            BaselineD = data.BaselineD;
            ShockV = data.ShockV;
            ShockA = data.ShockA;
            ShockD = data.ShockD;
            VadV = data.VadV;
            VadA = data.VadA;
            VadD = data.VadD;

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

    /// <summary>
    /// recent_affects の表示用行。
    /// </summary>
    public class MoodDebugRecentAffectRowViewModel
    {
        /// <summary>
        /// event のsource。
        /// </summary>
        public string EventSource { get; set; } = string.Empty;

        /// <summary>
        /// event の作成時刻。
        /// </summary>
        public string EventCreatedAt { get; set; } = "-";

        /// <summary>
        /// affect の作成時刻。
        /// </summary>
        public string AffectCreatedAt { get; set; } = "-";

        /// <summary>
        /// 瞬間感情の本文。
        /// </summary>
        public string MomentAffectText { get; set; } = string.Empty;

        /// <summary>
        /// 瞬間感情のラベル（表示用に結合済み）。
        /// </summary>
        public string MomentAffectLabels { get; set; } = string.Empty;

        /// <summary>
        /// V（快・不快）。
        /// </summary>
        public string VadV { get; set; } = "-";

        /// <summary>
        /// A（覚醒・沈静）。
        /// </summary>
        public string VadA { get; set; } = "-";

        /// <summary>
        /// D（支配・従属）。
        /// </summary>
        public string VadD { get; set; } = "-";

        /// <summary>
        /// 瞬間感情の確信度。
        /// </summary>
        public string Confidence { get; set; } = "-";

        /// <summary>
        /// 「データが無い」場合の表示用に空行を作る。
        /// </summary>
        public static MoodDebugRecentAffectRowViewModel CreateEmpty()
        {
            // --- nullバインディング回避（XAMLをシンプルに保つ） ---
            return new MoodDebugRecentAffectRowViewModel
            {
                EventSource = "-",
                EventCreatedAt = "-",
                AffectCreatedAt = "-",
                MomentAffectLabels = "（なし）",
                VadV = "-",
                VadA = "-",
                VadD = "-",
                Confidence = "-",
                MomentAffectText = "（なし）",
            };
        }
    }

    /// <summary>
    /// /api/mood/debug の表示用に整形したデータ（UIスレッドで重い変換をしないための中間表現）。
    /// </summary>
	    public class MoodDebugDisplayData
	    {
        /// <summary>
        /// 状態ID。
        /// </summary>
        public string StateId { get; set; } = "-";

        /// <summary>
        /// 本文。
        /// </summary>
        public string BodyText { get; set; } = "（未取得）";

        /// <summary>
        /// 確信度。
        /// </summary>
        public string Confidence { get; set; } = "-";

        /// <summary>
        /// 経過秒数。
        /// </summary>
        public string DtSeconds { get; set; } = "-";

        /// <summary>
        /// 最終確認時刻。
        /// </summary>
        public string LastConfirmedAt { get; set; } = "-";


        /// <summary>
        /// ベースラインVAD（V）。
        /// </summary>
        public string BaselineV { get; set; } = "-";

        /// <summary>
        /// ベースラインVAD（A）。
        /// </summary>
        public string BaselineA { get; set; } = "-";

        /// <summary>
        /// ベースラインVAD（D）。
        /// </summary>
        public string BaselineD { get; set; } = "-";

        /// <summary>
        /// ショックVAD（V）。
        /// </summary>
        public string ShockV { get; set; } = "-";

        /// <summary>
        /// ショックVAD（A）。
        /// </summary>
        public string ShockA { get; set; } = "-";

        /// <summary>
        /// ショックVAD（D）。
        /// </summary>
        public string ShockD { get; set; } = "-";

        /// <summary>
        /// 合成VAD（V）。
        /// </summary>
        public string VadV { get; set; } = "-";

        /// <summary>
        /// 合成VAD（A）。
        /// </summary>
        public string VadA { get; set; } = "-";

        /// <summary>
        /// 合成VAD（D）。
        /// </summary>
        public string VadD { get; set; } = "-";

        /// <summary>
        /// recent_affects（最新1件）。
        /// </summary>
        public MoodDebugRecentAffectRowViewModel? LatestAffect { get; set; }

        /// <summary>
        /// /api/mood/debug のレスポンスから表示用データを生成する。
        /// </summary>
	        public static MoodDebugDisplayData Create(MoodDebugResponse response)
	        {
            // --- 返却オブジェクト ---
            var data = new MoodDebugDisplayData();

            // --- recent_affects（最新1件のみ。moodの有無に関わらず表示） ---
            // 仕様上は「直近から」返るが、念のため時刻で最新を選ぶ。
            var latest = response?.RecentAffects?
                .OrderByDescending(x => x.AffectCreatedAt)
                .ThenByDescending(x => x.EventCreatedAt)
                .FirstOrDefault();
            if (latest != null)
            {
                // --- ラベルは読みやすさ優先で1行結合 ---
                var labels = latest.MomentAffectLabels == null || latest.MomentAffectLabels.Length == 0
                    ? "（なし）"
                    : string.Join(", ", latest.MomentAffectLabels.Where(x => !string.IsNullOrWhiteSpace(x)));

                data.LatestAffect = new MoodDebugRecentAffectRowViewModel
                {
                    EventSource = string.IsNullOrWhiteSpace(latest.EventSource) ? "-" : latest.EventSource,
                    EventCreatedAt = FormatDateTimeOffset(latest.EventCreatedAt),
                    AffectCreatedAt = FormatDateTimeOffset(latest.AffectCreatedAt),
                    MomentAffectText = string.IsNullOrWhiteSpace(latest.MomentAffectText) ? "（なし）" : latest.MomentAffectText,
                    MomentAffectLabels = labels,
                    VadV = FormatVadValue(latest.Vad?.V),
                    VadA = FormatVadValue(latest.Vad?.A),
                    VadD = FormatVadValue(latest.Vad?.D),
                    Confidence = FormatNumber(latest.Confidence),
                };
            }

            // --- mood が無い場合はここで終了 ---
            var mood = response?.Mood;
            if (mood == null)
            {
                data.StateId = "-";
                data.BodyText = "（未作成: long_mood_state が存在しません）";
                return data;
            }

            // --- 基本情報 ---
            data.StateId = mood.StateId.ToString(CultureInfo.InvariantCulture);
            data.BodyText = mood.BodyText ?? string.Empty;
            data.Confidence = FormatNumber(mood.Confidence);
	            data.DtSeconds = mood.DtSeconds.ToString(CultureInfo.InvariantCulture);
	            data.LastConfirmedAt = FormatDateTimeOffset(mood.LastConfirmedAt);

	            // --- VAD ---
	            data.BaselineV = FormatVadValue(mood.BaselineVad?.V);
	            data.BaselineA = FormatVadValue(mood.BaselineVad?.A);
	            data.BaselineD = FormatVadValue(mood.BaselineVad?.D);
            data.ShockV = FormatVadValue(mood.ShockVad?.V);
            data.ShockA = FormatVadValue(mood.ShockVad?.A);
            data.ShockD = FormatVadValue(mood.ShockVad?.D);
            data.VadV = FormatVadValue(mood.Vad?.V);
            data.VadA = FormatVadValue(mood.Vad?.A);
            data.VadD = FormatVadValue(mood.Vad?.D);

            return data;
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

    }
}
