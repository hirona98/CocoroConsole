using CocoroAI.Services;
using CocoroConsole.Communication;
using CocoroConsole.Controls;
using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Services;
using CocoroConsole.Utilities;
using CocoroConsole.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using System.Windows.Interop;

namespace CocoroConsole
{

    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private ICommunicationService? _communicationService;
        private readonly IAppSettings _appSettings;
        private bool _isDesktopWatchEnabled;
        private SettingWindow? _settingWindow;
        private LogViewerWindow? _logViewerWindow;
        private JudgmentTraceViewerWindow? _judgmentTraceViewerWindow;
        private CurrentStateViewerWindow? _currentStateViewerWindow;
        private AutonomousRunViewerWindow? _autonomousRunViewerWindow;
        private DebugTraceListener? _debugTraceListener;
        private bool _isConversationOutputActive;
        private bool _skipNextAssistantMessage;
        private string? _skipNextAssistantMessageContent;
        private bool _isLogStreamHandlersAttached;
        private const string MainWindowPlacementKey = "MainWindow";
        private const string SettingWindowPlacementKey = "SettingWindow";
        private const string LogViewerWindowPlacementKey = "LogViewerWindow";
        private const string JudgmentTraceViewerWindowPlacementKey = "JudgmentTraceViewerWindow";
        private const string CurrentStateViewerWindowPlacementKey = "CurrentStateViewerWindow";
        private const string AutonomousRunViewerWindowPlacementKey = "AutonomousRunViewerWindow";

        // --- OtomeKairo の最新ステータス（ステータスバー復帰先） ---
        // ログ表示で一時的に上書きしても、指定時間後「その時点の最新状態」に戻すために保持する。
        private OtomeKairoStatus _latestOtomeKairoStatus = OtomeKairoStatus.WaitingForStartup;

        // --- ステータスバーの一時上書き（ログ表示用） ---
        // 直近ログで上書きし、指定間上書きが無ければ null に戻して通常表示へ復帰する。
        private string? _statusBarOverrideText;
        private DispatcherTimer? _statusBarOverrideTimer;
        private static readonly TimeSpan StatusBarOverrideTimeout = TimeSpan.FromSeconds(3);

        private static readonly HashSet<string> VoiceRelatedComponents = new(StringComparer.OrdinalIgnoreCase)
        {
            "SileroVAD",
            "VoiceRecognition",
            "SpeakerRecognition",
            "VoiceService"
        };

        // --- 明示的な終了処理が進行中か（0/1） ---
        // WPF の Shutdown 中に Closing をキャンセルすると、Dispatcher が Shutdown 開始状態のまま残って
        // UI が固まることがあるため、明示的終了時はキャンセルしない判定に使う。
        private int _isShutdownInProgress;

        public MainWindow()
        {
            InitializeComponent();

            // ウィンドウのロード時にメッセージテキストボックスにフォーカスを設定するイベントを追加
            this.Loaded += MainWindow_Loaded;

            // 設定サービスの取得
            _appSettings = AppSettings.Instance;

            // メインウィンドウ位置を復元し、以降の移動を記録する
            WindowPlacementManager.AttachAndRestore(this, MainWindowPlacementKey, _appSettings);

            // 初期化と接続
            InitializeApp();
        }

        private void LogViewerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenLogViewer();
        }

        private void JudgmentTraceViewerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenJudgmentTraceViewer();
        }

        private void CurrentStateViewerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenCurrentStateViewer();
        }

        private void AutonomousRunViewerMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenAutonomousRunViewer();
        }


        /// <summary>
        /// チャット履歴をクリア
        /// </summary>
        public void ClearChatHistory()
        {
            ChatControlInstance.ClearChat();
            _communicationService?.StartNewConversation();
        }

        private void PositionWindowNearMain(Window child)
        {
            if (!IsLoaded)
            {
                Loaded += (_, __) => PositionWindowNearMain(child);
                return;
            }

            var handle = new WindowInteropHelper(this).Handle;
            var screen = Forms.Screen.FromHandle(handle);
            var workArea = screen.WorkingArea;

            child.WindowStartupLocation = WindowStartupLocation.Manual;

            void ApplyPosition()
            {
                var targetLeft = Left + 40;
                var targetTop = Top + 40;
                var childWidth = Math.Max(child.ActualWidth, child.Width);
                var childHeight = Math.Max(child.ActualHeight, child.Height);

                if (double.IsNaN(childWidth) || childWidth <= 0)
                {
                    childWidth = child.Width;
                }

                if (double.IsNaN(childHeight) || childHeight <= 0)
                {
                    childHeight = child.Height;
                }

                var left = Math.Min(Math.Max(targetLeft, workArea.Left), workArea.Right - childWidth);
                var top = Math.Min(Math.Max(targetTop, workArea.Top), workArea.Bottom - childHeight);

                child.Left = left;
                child.Top = top;
            }

            child.Loaded += (_, __) => ApplyPosition();
            ApplyPosition();
        }

        /// <summary>
        /// ウィンドウのロード完了時のイベントハンドラ
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // ChatControlのMessageTextBoxにフォーカス設定
            ChatControlInstance.FocusMessageTextBox();
        }

        /// <summary>
        /// アプリケーション初期化
        /// </summary>
        private void InitializeApp()
        {
            try
            {
                // 通信サービスを初期化
                // CommunicationServiceが初回Normal時にotomekairo設定を取得・反映する
                InitializeCommunicationService();

                // UIコントロールのイベントハンドラを登録
                RegisterEventHandlers();

                // CocoroConsole側ログを捕捉（ステータス表示用）
                AttachDebugTraceListener();

                // ボタンの初期状態を設定
                InitializeButtonStates();

                // 初期ステータス表示
                if (_communicationService != null)
                {
                    // 通信サービスが持つ最新状態を初期表示として反映
                    UpdateOtomeKairoStatusDisplay(_communicationService.CurrentStatus);
                }

                // APIサーバーの起動を開始
                _ = StartApiServerAsync();
            }
            catch (Exception ex)
            {
                UIHelper.ShowError("初期化エラー", ex.Message);
            }
        }

        /// <summary>
        /// ボタンの初期状態を設定
        /// </summary>
        private void InitializeButtonStates()
        {
            _isDesktopWatchEnabled = _appSettings.ScreenshotSettings.enabled;
            UpdateDesktopWatchButtonState();

            // 現在のキャラクターの設定を反映
            var currentAvatar = GetStoredAvatarSetting();
            if (currentAvatar != null)
            {
                UpdateMicrophoneButtonState(currentAvatar.isUseSTT);

                // TTSの状態を反映
                if (MuteButtonImage != null)
                {
                    MuteButtonImage.Source = new Uri(currentAvatar.isUseTTS ?
                        "pack://application:,,,/Resource/icon/SpeakerON.svg" :
                        "pack://application:,,,/Resource/icon/SpeakerOFF.svg",
                        UriKind.Absolute);
                }
                if (MuteButton != null)
                {
                    MuteButton.ToolTip = currentAvatar.isUseTTS ? "TTSを無効にする" : "TTSを有効にする";
                    MuteButton.Opacity = currentAvatar.isUseTTS ? 1.0 : 0.6;
                }
            }
        }

        private void UpdateDesktopWatchButtonState()
        {
            var isPaused = !_isDesktopWatchEnabled;
            if (ScreenshotButtonImage != null)
            {
                ScreenshotButtonImage.Source = new Uri(isPaused
                    ? "pack://application:,,,/Resource/icon/ScreenShotOFF.svg"
                    : "pack://application:,,,/Resource/icon/ScreenShotON.svg",
                    UriKind.Absolute);
            }
            if (PauseScreenshotButton != null)
            {
                PauseScreenshotButton.ToolTip = isPaused ? "デスクトップウォッチを有効にする" : "デスクトップウォッチを無効にする";
                PauseScreenshotButton.Opacity = isPaused ? 0.6 : 1.0;
            }
        }

        /// <summary>
        /// 通信サービスを初期化
        /// </summary>
        private void InitializeCommunicationService()
        {
            // 通信サービスを初期化 (REST APIサーバーを使用)
            _communicationService = new CommunicationService(_appSettings);            // 通信サービスのイベントハンドラを設定
            _communicationService.UiMessageReceived += OnUiMessageReceived;
            _communicationService.ConversationOutputReceived += OnConversationOutputReceived;
            _communicationService.VoiceConversationInputReceived += OnVoiceConversationInputReceived;
            _communicationService.ConversationInputBusyChanged += OnConversationInputBusyChanged;
            _communicationService.ControlCommandReceived += OnControlCommandReceived;
            _communicationService.ErrorOccurred += OnErrorOccurred;
            _communicationService.StatusChanged += OnOtomeKairoStatusChanged;
            _communicationService.AudioRuntimeStateChanged += OnAudioRuntimeStateChanged;
            _communicationService.EventsStreamConnectionChanged += OnEventsStreamConnectionChanged;
        }

        /// <summary>
        /// APIサーバーを起動（非同期タスク）
        /// </summary>
        private async Task StartApiServerAsync()
        {
            try
            {
                if (_communicationService != null && !_communicationService.IsServerRunning)
                {
                    // APIサーバーを起動
                    await _communicationService.StartServerAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APIサーバー起動エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// UIコントロールのイベントハンドラを登録
        /// </summary>
        private void RegisterEventHandlers()
        {
            // チャットコントロールのイベント登録
            ChatControlInstance.MessageSent += OnChatMessageSent;

            // 設定保存イベントの登録
            AppSettings.SettingsSaved += OnSettingsSaved;
        }





        /// <summary>
        /// OtomeKairoステータスに基づいて表示を更新
        /// </summary>
        /// <param name="status">OtomeKairoのステータス</param>
        private void UpdateOtomeKairoStatusDisplay(OtomeKairoStatus status)
        {
            // --- 最新状態を保持（ログ表示の復帰先になる） ---
            _latestOtomeKairoStatus = status;

            // --- 送信ボタンの有効/無効を状態に応じて更新 ---
            bool isLLMEnabled = _appSettings.IsUseLLM;
            bool isConversationInputBusy = _communicationService?.IsConversationInputBusy ?? false;

            // --- OtomeKairo起動待ちは最優先表示（ログ上書きより優先） ---
            if (status == OtomeKairoStatus.WaitingForStartup)
            {
                _statusBarOverrideTimer?.Stop();
                _statusBarOverrideText = null;
            }

            // --- ステータスバー表示は「通常表示 or ログ上書き」を統一して描画する ---
            RenderStatusBarText(BuildOtomeKairoStatusBarText(status));

            // 送信ボタンの有効/無効を制御
            // NOTE:
            // - 送信中（SSEストリーム中）は UI 表示が 1 本前提なので、二重送信を抑止する。
            // - ステータスが Normal のときだけ送信可能にする。
            bool isSendEnabled = isLLMEnabled && status == OtomeKairoStatus.Normal && !isConversationInputBusy;
            ChatControlInstance.UpdateSendButtonEnabled(isSendEnabled);
        }

        #region チャットコントロールイベントハンドラ

        /// <summary>
        /// チャットメッセージ送信時のハンドラ
        /// </summary>
        private void OnChatMessageSent(object? sender, string message)
        {
            if (!TryGetConversationDisplayName(out var displayName))
            {
                return;
            }

            // APIサーバーが起動している場合のみ送信
            if (_communicationService == null || !_communicationService.IsServerRunning)
            {
                ChatControlInstance.AddSystemErrorMessage("サーバーが起動していません");
                return;
            }

            // --- 送信中は二重送信を抑止（Enter送信などの抜け道もあるため） ---
            if (_communicationService.IsConversationInputBusy)
            {
                ChatControlInstance.AddSystemErrorMessage("前の対話入力が処理中です。発話を待ってください。");
                return;
            }

            // UIスレッドで画像データを取得・処理（スレッドセーフな形式に変換）
            var imageSources = ChatControlInstance.GetAttachedImageSources();
            var imageDataUrls = ChatControlInstance.GetAndClearAttachedImages();

            // ユーザーメッセージとしてチャットウィンドウに表示（送信前に表示）
            ChatControlInstance.AddUserMessage(displayName, message, imageSources);

            // --- 送信開始と同時に送信ボタンを無効化（連打を防ぐ） ---
            ChatControlInstance.UpdateSendButtonEnabled(false);

            // 非同期でOtomeKairoにメッセージを送信（UIをブロックしない）
            _ = Task.Run(async () =>
            {
                try
                {
                    // OtomeKairoにメッセージを送信（API使用、画像付きの場合は画像データも送信）
                    await _communicationService.SendConversationInputToOtomeKairoAsync(
                        message,
                        null,
                        imageDataUrls);
                }
                catch (TimeoutException)
                {
                    // UIスレッドでエラーメッセージを表示
                    UIHelper.RunOnUIThread(() =>
                    {
                        ChatControlInstance.AddSystemErrorMessage("AI発話がタイムアウトしました。もう一度お試しください。");
                    });
                }
                catch (HttpRequestException ex)
                {
                    // UIスレッドでエラーメッセージを表示
                    UIHelper.RunOnUIThread(() =>
                    {
                        ChatControlInstance.AddSystemErrorMessage("AI発話サーバーに接続できません。");
                    });
                    Debug.WriteLine($"HttpRequestException: {ex.Message}");
                }
                catch (Exception ex)
                {
                    // UIスレッドでエラーメッセージを表示
                    UIHelper.RunOnUIThread(() =>
                    {
                        ChatControlInstance.AddSystemErrorMessage($"エラーが発生しました: {ex.Message}");
                    });
                    Debug.WriteLine($"Exception: {ex}");
                }
            });
        }

        /// <summary>
        /// 設定保存時のイベントハンドラ
        /// </summary>
        private async void OnSettingsSaved(object? sender, EventArgs e)
        {
            if (_appSettings.HasRemoteSettings && _communicationService != null)
            {
                try
                {
                    // Shell が設定取得を開始する前に、Console API の待受を確定する。
                    await _communicationService.PrepareShellRuntimeAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CocoroShell実行設定APIの準備に失敗しました: {ex.Message}");
                    return;
                }
            }

            // UI側の設定反映（ボタン状態とLLM表示）
            UIHelper.RunOnUIThread(() =>
            {
                if (_appSettings.HasRemoteSettings)
                {
                    LaunchCocoroShell();
                    WindowPlacementManager.RestorePosition(this, MainWindowPlacementKey, _appSettings);
                }
                // 設定変更後のボタン状態を反映
                InitializeButtonStates();

                // LLM有効/無効の表示を即時更新
                var currentStatus = _communicationService?.CurrentStatus ?? OtomeKairoStatus.WaitingForStartup;
                UpdateOtomeKairoStatusDisplay(currentStatus);
            });
        }

        #endregion

        #region 通信サービスイベントハンドラ

        /// <summary>
        /// チャットメッセージ受信時のハンドラ（CocoroConsole APIから）
        /// </summary>
        private void OnUiMessageReceived(object? sender, UiMessageRequest request)
        {
            UIHelper.RunOnUIThread(() =>
            {
                if (_skipNextAssistantMessage && request.role == "assistant")
                {
                    var skipContent = _skipNextAssistantMessageContent;
                    _skipNextAssistantMessage = false;
                    _skipNextAssistantMessageContent = null;

                    if (string.IsNullOrEmpty(skipContent) || string.Equals(request.content, skipContent, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                if (request.role == "user")
                {
                    if (TryGetConversationDisplayName(out var displayName))
                    {
                        ChatControlInstance.AddUserMessage(displayName, request.content);
                    }
                }
                else if (request.role == "assistant")
                {
                    // サーバー側処理済みメッセージをそのまま新規追加
                    ChatControlInstance.AddAiMessage(request.content, request.forceNewBubble);
                }
            });
        }

        private void OnVoiceConversationInputReceived(
            object? sender,
            VoiceConversationInputEventArgs input)
        {
            UIHelper.RunOnUIThread(() =>
            {
                ChatControlInstance.AddUserMessage(input.DisplayName, input.Message);
            });
        }

        private void OnConversationOutputReceived(object? sender, ConversationOutputEventArgs e)
        {
            UIHelper.RunOnUIThread(() =>
            {
                if (e.IsError)
                {
                    ChatControlInstance.AddAiMessage($"[error] {e.ErrorMessage ?? "チャット中断"}");
                    _isConversationOutputActive = false;
                    _skipNextAssistantMessage = false;
                    _skipNextAssistantMessageContent = null;
                    return;
                }

                if (!e.IsFinished)
                {
                    if (!_isConversationOutputActive)
                    {
                        ChatControlInstance.AddAiMessage(e.Content);
                        _isConversationOutputActive = true;
                    }
                    else
                    {
                        ChatControlInstance.UpdateStreamingAiMessage(e.Content);
                    }
                }
                else
                {
                    ChatControlInstance.UpdateStreamingAiMessage(e.Content);
                    _isConversationOutputActive = false;
                    _skipNextAssistantMessage = true; // 直後の最終メッセージ表示を抑止
                    _skipNextAssistantMessageContent = e.Content;
                }
            });
        }

        private void OnConversationInputBusyChanged(object? sender, bool isBusy)
        {
            UIHelper.RunOnUIThread(() =>
            {
                // --- 送信ボタン状態を即時反映する（ステータスポーリングの待ちを作らない） ---
                var status = _communicationService?.CurrentStatus ?? OtomeKairoStatus.WaitingForStartup;
                UpdateOtomeKairoStatusDisplay(status);
            });
        }

        /// <summary>
        /// 制御コマンド受信時のハンドラ（CocoroConsole APIから）
        /// </summary>
        private void OnControlCommandReceived(object? sender, ControlRequest request)
        {
            UIHelper.RunOnUIThread(async () =>
            {
                // パラメータ情報をログ出力
                var paramsInfo = request.@params?.Count > 0 ? $" パラメータ: {request.@params.Count}個" : "";
                Debug.WriteLine($"制御コマンド受信: {request.action}, 理由: {request.reason}{paramsInfo}");

                switch (request.action)
                {
                    case "shutdown":
                        // 非同期でシャットダウン処理を実行
                        await PerformGracefulShutdownAsync();
                        break;

                    case "restart":
                        Debug.WriteLine("restart コマンドは現在未実装です");
                        break;

                    default:
                        Debug.WriteLine($"未知の制御コマンド: {request.action}");
                        break;
                }
            });
        }        /// <summary>
                 /// エラー発生時のハンドラ
                 /// </summary>
        private void OnErrorOccurred(object? sender, string error)
        {
            UIHelper.ShowError("エラー", error);
        }

        /// <summary>
        /// OtomeKairoステータス変更時のハンドラ
        /// </summary>
        private void OnOtomeKairoStatusChanged(object? sender, OtomeKairoStatus status)
        {
            UIHelper.RunOnUIThread(() =>
            {
                UpdateOtomeKairoStatusDisplay(status);
            });
        }

        /// <summary>
        /// OtomeKairoが通知した実効入力元の音量を表示します。
        /// </summary>
        private void OnAudioRuntimeStateChanged(object? sender, OtomeKairoAudioRuntimeState state)
        {
            var active = state.Available &&
                !string.IsNullOrWhiteSpace(state.ActiveSource) &&
                state.PausedReason == null;
            UIHelper.RunOnUIThread(() =>
            {
                ChatControlInstance.UpdateMicrophoneLevel(
                    state.Vad.Dbfs,
                    state.Vad.Speaking,
                    active);
            });
        }

        /// <summary>
        /// イベントストリーム切断時に古い音量表示を残しません。
        /// </summary>
        private void OnEventsStreamConnectionChanged(object? sender, bool isConnected)
        {
            if (isConnected)
            {
                return;
            }

            UIHelper.RunOnUIThread(() =>
            {
                ChatControlInstance.UpdateMicrophoneLevel(null, false, false);
            });
        }

        #endregion

        /// <summary>
        /// ログビューアーを開く
        /// </summary>
        public void OpenLogViewer()
        {
            // 既にログビューアーが開いている場合はアクティブにする
            if (_logViewerWindow != null && !_logViewerWindow.IsClosed)
            {
                _logViewerWindow.Activate();
                _logViewerWindow.WindowState = WindowState.Normal;
                return;
            }

            // ログビューアーを新規作成
            _logViewerWindow = new LogViewerWindow();
            var isLogViewerPositionRestored = WindowPlacementManager.AttachAndRestore(
                _logViewerWindow,
                LogViewerWindowPlacementKey,
                _appSettings);
            if (!isLogViewerPositionRestored)
            {
                PositionWindowNearMain(_logViewerWindow);
            }
            AttachLogStreamHandlers();
            AttachDebugTraceListener();

            // ウィンドウが閉じられた時の処理
            _logViewerWindow.Closed += async (sender, args) =>
            {
                DetachDebugTraceListener();
                DetachLogStreamHandlers();
                if (_communicationService != null)
                {
                    await _communicationService.StopLogStreamAsync();
                }
                _logViewerWindow = null;
            };

            // ログストリーム接続開始（失敗してもUIスレッドはブロックしない）
            if (_communicationService != null)
            {
                _ = _communicationService.StartLogStreamAsync();
                _logViewerWindow.UpdateStatusMessage("ログストリーム接続中...");
            }

            _logViewerWindow.Show();
        }

        public void OpenJudgmentTraceViewer()
        {
            if (_judgmentTraceViewerWindow != null && !_judgmentTraceViewerWindow.IsClosed)
            {
                _judgmentTraceViewerWindow.Activate();
                _judgmentTraceViewerWindow.WindowState = WindowState.Normal;
                return;
            }

            _judgmentTraceViewerWindow = new JudgmentTraceViewerWindow();
            var isPositionRestored = WindowPlacementManager.AttachAndRestore(
                _judgmentTraceViewerWindow,
                JudgmentTraceViewerWindowPlacementKey,
                _appSettings);
            if (!isPositionRestored)
            {
                PositionWindowNearMain(_judgmentTraceViewerWindow);
            }

            _judgmentTraceViewerWindow.Closed += (sender, args) =>
            {
                _judgmentTraceViewerWindow = null;
            };

            _judgmentTraceViewerWindow.Show();
        }

        public void OpenCurrentStateViewer()
        {
            if (_currentStateViewerWindow != null && !_currentStateViewerWindow.IsClosed)
            {
                _currentStateViewerWindow.Activate();
                _currentStateViewerWindow.WindowState = WindowState.Normal;
                return;
            }

            _currentStateViewerWindow = new CurrentStateViewerWindow();
            var isPositionRestored = WindowPlacementManager.AttachAndRestore(
                _currentStateViewerWindow,
                CurrentStateViewerWindowPlacementKey,
                _appSettings);
            if (!isPositionRestored)
            {
                PositionWindowNearMain(_currentStateViewerWindow);
            }

            _currentStateViewerWindow.Closed += (sender, args) =>
            {
                _currentStateViewerWindow = null;
            };

            _currentStateViewerWindow.Show();
        }

        public void OpenAutonomousRunViewer()
        {
            if (_autonomousRunViewerWindow != null && !_autonomousRunViewerWindow.IsClosed)
            {
                _autonomousRunViewerWindow.Activate();
                _autonomousRunViewerWindow.WindowState = WindowState.Normal;
                return;
            }

            _autonomousRunViewerWindow = new AutonomousRunViewerWindow();
            var isPositionRestored = WindowPlacementManager.AttachAndRestore(
                _autonomousRunViewerWindow,
                AutonomousRunViewerWindowPlacementKey,
                _appSettings);
            if (!isPositionRestored)
            {
                PositionWindowNearMain(_autonomousRunViewerWindow);
            }

            _autonomousRunViewerWindow.Closed += (sender, args) =>
            {
                _autonomousRunViewerWindow = null;
            };

            _autonomousRunViewerWindow.Show();
        }


        private void AttachLogStreamHandlers()
        {
            if (_communicationService == null || _isLogStreamHandlersAttached) return;

            _communicationService.LogMessagesReceived += OnLogStreamMessagesReceived;
            _communicationService.LogStreamConnectionChanged += OnLogStreamConnectionChanged;
            _communicationService.LogStreamError += OnLogStreamError;
            _isLogStreamHandlersAttached = true;
        }

        private void DetachLogStreamHandlers()
        {
            if (_communicationService == null || !_isLogStreamHandlersAttached) return;

            _communicationService.LogMessagesReceived -= OnLogStreamMessagesReceived;
            _communicationService.LogStreamConnectionChanged -= OnLogStreamConnectionChanged;
            _communicationService.LogStreamError -= OnLogStreamError;
            _isLogStreamHandlersAttached = false;
        }

        private void AttachDebugTraceListener()
        {
            if (_debugTraceListener != null) return;

            _debugTraceListener = DebugTraceListener.Register();
            _debugTraceListener.LogMessageReceived += OnDebugLogMessageReceived;
        }

        private void DetachDebugTraceListener()
        {
            if (_debugTraceListener == null) return;

            _debugTraceListener.LogMessageReceived -= OnDebugLogMessageReceived;
            _debugTraceListener.Unregister();
            _debugTraceListener = null;
        }

        private void OnDebugLogMessageReceived(object? sender, LogMessage logMessage)
        {
            if (IsVoiceRelatedLog(logMessage) && !IsRoutineVoiceLifecycleLog(logMessage))
            {
                UpdateStatusFromLog(logMessage);
            }

            if (_logViewerWindow == null || _logViewerWindow.IsClosed) return;

            UIHelper.RunOnUIThread(() =>
            {
                _logViewerWindow?.AddLogMessage(logMessage);
            });
        }

        private void OnLogStreamMessagesReceived(object? sender, IReadOnlyList<LogMessage> logs)
        {
            if (_logViewerWindow == null || _logViewerWindow.IsClosed) return;

            UIHelper.RunOnUIThread(() =>
            {
                _logViewerWindow?.AddLogMessages(logs);
            });
        }

        private void OnLogStreamConnectionChanged(object? sender, bool isConnected)
        {
            if (_logViewerWindow == null || _logViewerWindow.IsClosed) return;

            var status = isConnected ? "ログストリーム接続中" : "ログストリーム切断";
            UIHelper.RunOnUIThread(() => _logViewerWindow?.UpdateStatusMessage(status));
        }

        private void OnLogStreamError(object? sender, string error)
        {
            if (_logViewerWindow == null || _logViewerWindow.IsClosed) return;

            UIHelper.RunOnUIThread(() => _logViewerWindow?.UpdateStatusMessage($"ログストリームエラー: {error}"));
        }

        private static bool IsVoiceRelatedLog(LogMessage logMessage)
        {
            var component = logMessage.component ?? string.Empty;
            var message = logMessage.message ?? string.Empty;

            if (VoiceRelatedComponents.Contains(component)) return true;

            if (component.Contains("voice", StringComparison.OrdinalIgnoreCase)
                || component.Contains("vad", StringComparison.OrdinalIgnoreCase)
                || component.Contains("speaker", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (message.Contains("音声", StringComparison.OrdinalIgnoreCase)
                || message.Contains("話者", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ウェイク", StringComparison.OrdinalIgnoreCase)
                || message.Contains("認識", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool IsRoutineVoiceLifecycleLog(LogMessage logMessage)
        {
            var message = logMessage.message ?? string.Empty;

            return message.Contains("音声認識サービスを開始しました", StringComparison.OrdinalIgnoreCase)
                || message.Contains("音声認識サービスを停止しました", StringComparison.OrdinalIgnoreCase)
                || message.Contains("音声認識機能が無効、またはAPIキーが未設定", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Initialized with STT:", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Started listening", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Stopped listening", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Started in ACTIVE state", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Started in SLEEPING state", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateStatusFromLog(LogMessage logMessage)
        {
            UIHelper.RunOnUIThread(() =>
            {
                var componentText = string.IsNullOrWhiteSpace(logMessage.component) ? string.Empty : $"{logMessage.component} ";
                var messageText = logMessage.message ?? string.Empty;

                var statusText = $"状態: {componentText}{messageText}".Trim();

                // --- ログでステータスバーを一時上書き（デバウンス） ---
                // 直近ログで上書きし続け、指定時間ログ上書きが無ければ「その時点の最新ステータス表示」に戻す。
                SetStatusBarOverride(statusText);
            });
        }

        /// <summary>
        /// ステータスバーにログ由来の一時表示を設定し、一定時間後に通常表示へ復帰する（デバウンス）
        /// </summary>
        /// <param name="overrideText">ステータスバーに表示するテキスト（例: "状態: VoiceRecognition 〜"）</param>
        private void SetStatusBarOverride(string overrideText)
        {
            // --- OtomeKairo起動待ちは最優先表示（上書きしない） ---
            if (_latestOtomeKairoStatus == OtomeKairoStatus.WaitingForStartup)
            {
                return;
            }

            // --- 上書きテキストを更新 ---
            _statusBarOverrideText = overrideText;

            // --- 既存タイマーが無ければ作成（UIスレッド上） ---
            if (_statusBarOverrideTimer == null)
            {
                _statusBarOverrideTimer = new DispatcherTimer
                {
                    Interval = StatusBarOverrideTimeout
                };
                _statusBarOverrideTimer.Tick += OnStatusBarOverrideTimerTick;
            }

            // --- 表示を即時反映 ---
            RenderStatusBarText(_statusBarOverrideText);

            // --- デバウンス：タイマーをリセットして復帰を予約 ---
            _statusBarOverrideTimer.Stop();
            _statusBarOverrideTimer.Interval = StatusBarOverrideTimeout;
            _statusBarOverrideTimer.Start();
        }

        /// <summary>
        /// ステータスバー上書きのタイムアウト（上書きが無ければ通常表示へ復帰）
        /// </summary>
        private void OnStatusBarOverrideTimerTick(object? sender, EventArgs e)
        {
            // --- タイムアウト：上書きを解除して通常表示へ復帰 ---
            _statusBarOverrideTimer?.Stop();
            _statusBarOverrideText = null;
            RenderStatusBarText(BuildOtomeKairoStatusBarText(_latestOtomeKairoStatus));
        }

        /// <summary>
        /// 現在の状態（または一時上書き）に基づき、ステータスバーの表示を更新する
        /// </summary>
        /// <param name="normalStatusText">通常表示テキスト（例: "状態: OtomeKairo 正常動作中"）</param>
        private void RenderStatusBarText(string normalStatusText)
        {
            // --- OtomeKairo起動待ちは最優先表示 ---
            // 起動待ち中にログが流れても、ユーザーが状況を誤認しないよう「起動待ち」を固定で出す。
            var textToShow = _latestOtomeKairoStatus == OtomeKairoStatus.WaitingForStartup
                ? BuildOtomeKairoStatusBarText(OtomeKairoStatus.WaitingForStartup)
                : (_statusBarOverrideText ?? normalStatusText);

            // --- UI要素が未生成の場合は何もしない（初期化順による） ---
            if (ConnectionStatusText == null) return;

            ConnectionStatusText.Text = textToShow;
            ConnectionStatusText.ToolTip = textToShow;
        }

        /// <summary>
        /// OtomeKairoの状態から、ステータスバーの通常表示文字列を組み立てる
        /// </summary>
        /// <param name="status">OtomeKairoの状態</param>
        private string BuildOtomeKairoStatusBarText(OtomeKairoStatus status)
        {
            // --- 表示文言は UpdateOtomeKairoStatusDisplay と同じルールで統一 ---
            var isLLMEnabled = _appSettings.IsUseLLM;
            var statusText = status switch
            {
                OtomeKairoStatus.WaitingForStartup => isLLMEnabled ? "OtomeKairo起動待ち" : "LLM無効",
                OtomeKairoStatus.Normal => isLLMEnabled ? "正常動作中" : "LLM無効",
                OtomeKairoStatus.ProcessingConversationInput => "対話入力処理中",
                OtomeKairoStatus.ProcessingImage => "LLM画像処理中",
                _ => "不明な状態"
            };

            return $"状態: {statusText}";
        }

        /// <summary>
        /// アプリケーション終了時の処理
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // イベントハンドラの購読解除
                AppSettings.SettingsSaved -= OnSettingsSaved;

                // ステータスバー上書きタイマーの停止（閉じた後にTickで触らないようにする）
                _statusBarOverrideTimer?.Stop();
                if (_statusBarOverrideTimer != null)
                {
                    // --- Tick ハンドラを外して参照を切る（GCしやすくする） ---
                    _statusBarOverrideTimer.Tick -= OnStatusBarOverrideTimerTick;
                }
                _statusBarOverrideTimer = null;
                _statusBarOverrideText = null;

                // DebugTraceListenerの解除
                DetachDebugTraceListener();

                // 接続中ならリソース解放
                if (_communicationService != null)
                {
                    try
                    {
                        using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        _communicationService
                            .SaveConsoleClientSettingsAsync(saveTimeout.Token)
                            .GetAwaiter()
                            .GetResult();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"終了時の端末設定保存に失敗しました: {ex.Message}");
                    }
                    finally
                    {
                        _communicationService.Dispose();
                        _communicationService = null;
                    }
                }
            }
            catch (Exception)
            {
                // 切断中のエラーは無視
            }

            base.OnClosed(e);
        }

        /// <summary>
        /// 設定ボタンクリック時のイベントハンドラ
        /// </summary>
        private void AdminButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                // 既に設定画面が開いている場合はアクティブにする
                if (_settingWindow != null && !_settingWindow.IsClosed)
                {
                    _settingWindow.Activate();
                    _settingWindow.WindowState = WindowState.Normal;
                    return;
                }

                // 設定画面を新規作成
                _settingWindow = new SettingWindow(_communicationService);
                var isSettingPositionRestored = WindowPlacementManager.AttachAndRestore(
                    _settingWindow,
                    SettingWindowPlacementKey,
                    _appSettings);
                if (!isSettingPositionRestored)
                {
                    PositionWindowNearMain(_settingWindow);
                }

                // ウィンドウが閉じられた時にボタンの状態を更新
                _settingWindow.Closed += SettingWindow_Closed;

                _settingWindow.Show(); // モードレスダイアログとして表示
            }
            catch (Exception ex)
            {
                UIHelper.ShowError("設定取得エラー", ex.Message);
            }
        }

        /// <summary>
        /// 設定画面が閉じられた時のイベントハンドラ
        /// </summary>
        private void SettingWindow_Closed(object? sender, EventArgs e)
        {
            // ボタンの状態を最新の設定に更新
            InitializeButtonStates();

            // SettingWindowの参照をクリア
            _settingWindow = null;
        }

        /// <summary>
        /// 保存済みの現在のキャラクター設定を取得（AppSettingsから直接読み取り）
        /// </summary>
        private AvatarSettings? GetStoredAvatarSetting()
        {
            return _appSettings.GetCurrentAvatar();
        }

        /// <summary>
        /// 現在のアバターのSTT状態をマイクボタンへ反映します。
        /// </summary>
        private void UpdateMicrophoneButtonState(bool isEnabled)
        {
            MicButtonImage.Source = new Uri(
                isEnabled
                    ? "pack://application:,,,/Resource/icon/MicON.svg"
                    : "pack://application:,,,/Resource/icon/MicOFF.svg",
                UriKind.Absolute);
            MicButton.ToolTip = isEnabled ? "STTを無効にする" : "STTを有効にする";
            MicButton.Opacity = isEnabled ? 1.0 : 0.6;
        }

        /// <summary>
        /// 現在のアバターのSTT設定を切り替えます。
        /// </summary>
        private async void MicButton_Click(object sender, RoutedEventArgs e)
        {
            if (_communicationService == null)
            {
                return;
            }

            var currentAvatar = GetStoredAvatarSetting();
            if (currentAvatar == null)
            {
                return;
            }

            var previousEnabled = currentAvatar.isUseSTT;
            MicButton.IsEnabled = false;
            currentAvatar.isUseSTT = !previousEnabled;
            try
            {
                await _communicationService.SaveAvatarSpeechSettingsAsync();
                UpdateMicrophoneButtonState(currentAvatar.isUseSTT);
            }
            catch (Exception ex)
            {
                currentAvatar.isUseSTT = previousEnabled;
                UpdateMicrophoneButtonState(previousEnabled);
                UIHelper.ShowError("STT設定エラー", ex.Message);
            }
            finally
            {
                MicButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// デスクトップウォッチボタンクリック時のイベントハンドラ
        /// </summary>
        private async void PauseScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (_communicationService == null)
            {
                return;
            }

            await _communicationService.SetDesktopWatchEnabledAsync(!_isDesktopWatchEnabled);
            _isDesktopWatchEnabled = _appSettings.ScreenshotSettings.enabled;
            UpdateDesktopWatchButtonState();
            _settingWindow?.SetWakeDesktopObservationEnabled(_isDesktopWatchEnabled);
        }

        /// <summary>
        /// TTSボタンクリック時のイベントハンドラ
        /// </summary>
        private async void TTSButton_Click(object sender, RoutedEventArgs e)
        {
            if (_communicationService == null)
            {
                return;
            }

            // 現在のキャラクターのTTS設定をトグル
            var currentAvatar = GetStoredAvatarSetting();
            if (currentAvatar != null)
            {
                currentAvatar.isUseTTS = !currentAvatar.isUseTTS;
                try
                {
                    await _communicationService.SaveAvatarSpeechSettingsAsync();
                }
                catch (Exception ex)
                {
                    currentAvatar.isUseTTS = !currentAvatar.isUseTTS;
                    UIHelper.ShowError("TTS設定エラー", ex.Message);
                    return;
                }

                // ボタンの画像を更新
                if (MuteButtonImage != null)
                {
                    MuteButtonImage.Source = new Uri(currentAvatar.isUseTTS ?
                        "pack://application:,,,/Resource/icon/SpeakerON.svg" :
                        "pack://application:,,,/Resource/icon/SpeakerOFF.svg",
                        UriKind.Absolute);
                }

                // ツールチップを更新
                if (MuteButton != null)
                {
                    MuteButton.ToolTip = currentAvatar.isUseTTS ? "TTSを無効にする" : "TTSを有効にする";

                    // 無効状態の場合は半透明にする
                    MuteButton.Opacity = currentAvatar.isUseTTS ? 1.0 : 0.6;
                }

            }
        }


        /// <summary>
        /// CocoroShell.exeを起動する（既に起動している場合は終了してから再起動）
        /// </summary>
        /// <param name="operation">プロセス操作の種類（デフォルトは再起動）</param>
        private void LaunchCocoroShell(ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
            CocoroShellProcessManager.Apply(_appSettings, operation);
        }


        /// <summary>
        /// テキスト入力で participants[].display_name に渡す呼び名を取得する。
        /// </summary>
        private bool TryGetConversationDisplayName(out string displayName)
        {
            displayName = _appSettings.ConversationDisplayName.Trim();
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return true;
            }

            ChatControlInstance.AddSystemErrorMessage(
                "呼ばれ方が未設定です。設定の入力から「会話入力」を開いて設定してください。");
            return false;
        }

        /// <summary>
        /// ウィンドウのクローズイベントをキャンセルし、代わりに最小化する
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // --- 「ユーザーの閉じる」と「アプリ終了」を区別する ---
            // WPF の Shutdown 中（Dispatcher 側で Shutdown が開始済み）や、明示的な終了要求中は Close を通す。
            // それ以外（ALT+F4 / タイトルバーX）はトレイ常駐として Hide にする。
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            var isAppShuttingDown =
                Interlocked.CompareExchange(ref _isShutdownInProgress, 0, 0) == 1 ||
                (dispatcher?.HasShutdownStarted ?? false) ||
                (dispatcher?.HasShutdownFinished ?? false);

            if (!isAppShuttingDown)
            {
                // --- 終了ではなく最小化して非表示にする ---
                e.Cancel = true;
                WindowState = WindowState.Minimized;
                Hide();
                return;
            }

            base.OnClosing(e);
        }

        /// <summary>
        /// 正常なシャットダウン処理を実行
        /// </summary>
        public async Task PerformGracefulShutdownAsync()
        {
            try
            {
                // --- 二重実行防止 ---
                // Shell 側からの shutdown とトレイメニュー「終了」が競合しても 1 回だけ実行する。
                if (Interlocked.Exchange(ref _isShutdownInProgress, 1) == 1)
                {
                    Debug.WriteLine("[CocoroConsole] シャットダウンは既に進行中です。");
                    return;
                }

                // ウィンドウを最前面に表示
                this.Show();
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }
                this.Topmost = true;
                this.Activate();

                // --- 補助ウィンドウは先に閉じる（Shutdown をキャンセルさせないため） ---
                // LogViewer/Setting が残っている状態で MainWindow の Closing をキャンセルすると、
                // 「メインだけ消えて他が固まる」状態になりやすい。
                try
                {
                    _settingWindow?.Close();
                    _logViewerWindow?.Close();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CocoroConsole] 補助ウィンドウのクローズ中にエラー: {ex.Message}");
                }

                bool isLLMEnabled = _appSettings.IsUseLLM;

                if (!isLLMEnabled)
                {
                    // LLMが無効の場合は「記憶を整理しています」を非表示に
                    if (ShutdownOverlay.FindName("MemoryCleanupText") is System.Windows.Controls.TextBlock memoryText)
                    {
                        memoryText.Visibility = Visibility.Collapsed;
                    }
                }

                // シャットダウンオーバーレイを表示
                ShutdownOverlay.Visibility = Visibility.Visible;

                Debug.WriteLine("CocoroShell に終了要求を送信中...");
                var shutdownTasks = new List<Task>
                {
                    Task.Run(() => ProcessHelper.ExitProcess("CocoroShell", ProcessOperation.Terminate))
                };

                // すべてのシャットダウン要求の完了を待つ（最大5秒）
                try
                {
                    await Task.WhenAll(shutdownTasks).WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    Debug.WriteLine("一部のシャットダウン要求がタイムアウトしました。");
                }

                // オーバーレイを非表示
                ShutdownOverlay.Visibility = Visibility.Collapsed;

                // アプリケーションを終了
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"シャットダウン処理中にエラーが発生しました: {ex.Message}");

                // エラーが発生してもオーバーレイを非表示
                ShutdownOverlay.Visibility = Visibility.Collapsed;

                Application.Current.Shutdown();
            }
        }
    }
}
