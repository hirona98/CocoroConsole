using CocoroConsole.Communication;
using CocoroConsole.Models.OtomeKairoApi;
using CocoroAI.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CocoroConsole.Services
{
    /// <summary>
    /// CocoroAI（CocoroConsole / OtomeKairo / CocoroShell）間の通信を集約するサービス。
    /// 
    /// 主な責務:
    /// - CocoroConsole API サーバーの起動/停止（外部からの chat/control/status 更新を受ける）
    /// - CocoroShell への送信（発話/表示の連携）
    /// - OtomeKairo の状態ポーリングと、状態変化イベントの転送
    /// - otomekairo HTTP API（Bearer 認証）を用いた設定取得やチャット SSE ストリーミング
    /// - ログ/イベントのストリーミング接続の管理
    /// </summary>
    public class CommunicationService : ICommunicationService
    {
        // チャットは SSE の token を逐次 UI に転送して表示する（即時表示方式）

        // CocoroConsole 側の HTTP API サーバー（外部クライアントからの受信）
        private CocoroConsoleApiServer _apiServer;

        // CocoroShell（Unity 側）へ送るクライアント
        private CocoroShellClient _shellClient;

        private readonly IAppSettings _appSettings;

        // otomekairo の状態を定期取得して UI に通知する
        private StatusPollingService _statusPollingService;

        // otomekairo HTTP API（Bearer トークン必須）。トークン未設定時は null。
        private OtomeKairoApiClient? _otomeKairoApiClient;

        // otomekairo のログ/イベントを購読するストリームクライアント（必要時に開始/停止）
        private LogStreamClient? _logStreamClient;
        private EventsStreamClient? _eventsStreamClient;

        // /api/mood/debug を 1 秒間隔で取得するポーリング（感情デバッグ表示用）
        private readonly object _moodDebugPollingLock = new object();
        private CancellationTokenSource? _moodDebugPollingCts;
        private Task? _moodDebugPollingTask;

        // シェルへの送信順序を保証するためのセマフォ（同時送信を直列化）
        private readonly SemaphoreSlim _forwardMessageSemaphore = new SemaphoreSlim(1, 1);

        // チャット送信順序を保証するためのセマフォ（同時送信を直列化）
        // NOTE:
        // - 現在の OtomeKairo API は 1 リクエスト 1 応答のため、二重送信だけを防げばよい。
        // - 入力経路（UI/音声など）を跨いでも「1回の送信=1応答」に揃える。
        private readonly SemaphoreSlim _chatSendSemaphore = new SemaphoreSlim(1, 1);

        // OtomeKairo の bootstrap と認証確認を直列化するためのセマフォ
        private readonly SemaphoreSlim _otomeKairoBootstrapSemaphore = new SemaphoreSlim(1, 1);

        // チャット送信中フラグ（0/1）
        // UI 側の送信ボタン無効化・二重送信抑止に使う。
        private int _chatBusy = 0;

        // 設定キャッシュ（SettingsSaved で更新し、差分に応じてランタイム反映）
        private ConfigSettings? _cachedConfigSettings;

        // memory_id キャッシュ（チャット返信を UI へ戻す際に付与）
        private string _cachedMemoryId = "memory";

        // 起動後、otomekairo が Normal になったタイミングで一度だけ設定取得するためのフラグ
        private bool _initialSettingsFetched = false;

        // desktop_watch 用: 直近のデスクトップキャプチャ（vision.capture_request）を一時キャッシュして
        // desktop_watch イベントの通知表示に画像を添付する。
        private readonly object _desktopWatchCaptureLock = new object();
        private (DateTime TimestampUtc, List<BitmapSource> Images, string? WindowTitle)? _lastDesktopWatchCapture;
        private static readonly TimeSpan DesktopWatchCaptureMaxAge = TimeSpan.FromSeconds(30);

        private static bool IsVrmDisplayEnabled(CharacterSettings? currentCharacter)
        {
            // MainWindow.LaunchCocoroShell と同じ判定: パスがあれば有効 / readOnlyキャラは常に有効
            return currentCharacter != null &&
                   (!string.IsNullOrWhiteSpace(currentCharacter.vrmFilePath) || currentCharacter.isReadOnly == true);
        }

        private bool ShouldForwardToShell(CharacterSettings? currentCharacter = null)
        {
            // 呼び出し元がキャラクターを渡していない場合はキャッシュから解決
            currentCharacter ??= GetStoredCharacterSetting();
            return IsVrmDisplayEnabled(currentCharacter);
        }

        public event EventHandler<ChatRequest>? ChatMessageReceived;
        public event EventHandler<StreamingChatEventArgs>? StreamingChatReceived;
        public event EventHandler<bool>? ChatBusyChanged;
        public event Action<ChatMessagePayload, List<System.Windows.Media.Imaging.BitmapSource>?>? NotificationMessageReceived;
        public event EventHandler<ControlRequest>? ControlCommandReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<StatusUpdateEventArgs>? StatusUpdateRequested;
        public event EventHandler<OtomeKairoStatus>? StatusChanged;
        public event EventHandler<CocoroConsole.Models.OtomeKairoApi.OtomeKairoSettings>? OtomeKairoSettingsUpdated;
        public event EventHandler<IReadOnlyList<LogMessage>>? LogMessagesReceived;
        public event EventHandler<bool>? LogStreamConnectionChanged;
        public event EventHandler<string>? LogStreamError;
        public event EventHandler<MoodDebugUpdatedEventArgs>? MoodDebugUpdated;
        public event EventHandler<string>? MoodDebugError;

        public bool IsServerRunning => _apiServer.IsRunning;

        /// <summary>
        /// 現在のOtomeKairoステータス
        /// </summary>
        public OtomeKairoStatus CurrentStatus => _statusPollingService.CurrentStatus;

        /// <summary>
        /// チャット送信中かどうか
        /// </summary>
        public bool IsChatBusy => Volatile.Read(ref _chatBusy) == 1;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="appSettings">アプリケーション設定</param>
        public CommunicationService(IAppSettings appSettings)
        {
            _appSettings = appSettings;

            // --- ClientId は otomekairo の events stream の hello で必須になるため、起動時に確実に用意する ---
            EnsureClientIdInitialized();

            // APIサーバーの初期化
            _apiServer = CreateApiServer(_appSettings.CocoroConsolePort);

            // CocoroShellクライアントの初期化
            _shellClient = new CocoroShellClient(_appSettings.CocoroShellPort);

            // OtomeKairo APIクライアントを初期化する
            var bearerToken = _appSettings.OtomeKairoBearerToken;
            var baseUrl = _appSettings.GetOtomeKairoBaseUrl();
            _otomeKairoApiClient = new OtomeKairoApiClient(baseUrl, bearerToken);

            // 設定キャッシュを初期化
            RefreshSettingsCache();

            // ステータスポーリングサービスの初期化
            _statusPollingService = new StatusPollingService(_appSettings.GetOtomeKairoBaseUrl());
            _statusPollingService.StatusChanged += OnStatusPollingServiceStatusChanged;

            // AppSettingsの変更イベントを購読
            AppSettings.SettingsSaved += OnSettingsSaved;
        }

        /// <summary>
        /// ClientId が未設定の場合に生成して永続化する。
        /// 
        /// - /api/events/stream の hello で client_id を送るために必要
        /// - desktop_watch_target_client_id などの「端末識別」にも使う
        /// </summary>
        private void EnsureClientIdInitialized()
        {
            if (!string.IsNullOrWhiteSpace(_appSettings.ClientId))
            {
                return;
            }

            _appSettings.ClientId = $"console-{Guid.NewGuid()}";
            _appSettings.SaveAppSettings();
        }

        private async Task EnsureOtomeKairoReadyAsync()
        {
            if (_otomeKairoApiClient == null)
            {
                return;
            }

            await _otomeKairoBootstrapSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                // --- 既存トークンがあれば、まず有効性を確認する ---
                var bearerToken = (_appSettings.OtomeKairoBearerToken ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    try
                    {
                        await _otomeKairoApiClient.GetOtomeKairoStatusAsync().ConfigureAwait(false);
                        return;
                    }
                    catch (OtomeKairoApiException ex) when (ex.ErrorCode == "invalid_token" || ex.ErrorCode == "bootstrap_required")
                    {
                        _appSettings.OtomeKairoBearerToken = string.Empty;
                        _appSettings.SaveAppSettings();
                    }
                }

                // --- 未発行状態のときだけ最初のトークンを自動取得する ---
                var probe = await _otomeKairoApiClient.ProbeBootstrapAsync().ConfigureAwait(false);
                if (!string.Equals(probe.BootstrapState, "ready_for_first_console", StringComparison.Ordinal))
                {
                    return;
                }

                var registered = await _otomeKairoApiClient.RegisterFirstConsoleAsync().ConfigureAwait(false);
                _appSettings.OtomeKairoBearerToken = registered.ConsoleAccessToken;
                _appSettings.SaveAppSettings();
            }
            finally
            {
                _otomeKairoBootstrapSemaphore.Release();
            }
        }

        private void SetChatBusy(bool isBusy)
        {
            // --- 状態の更新は 0/1 で統一し、変化があるときだけイベントを発火する ---
            int next = isBusy ? 1 : 0;
            int prev = Interlocked.Exchange(ref _chatBusy, next);
            if (prev == next)
            {
                return;
            }

            // --- イベントは呼び出しスレッドで発火（UI側で Dispatcher に載せ替える） ---
            try
            {
                ChatBusyChanged?.Invoke(this, isBusy);
            }
            catch (Exception ex)
            {
                // NOTE: UI 側の例外で通信層が停止しないようにする
                Debug.WriteLine($"[CommunicationService] ChatBusyChanged handler error: {ex.Message}");
            }
        }


        /// <summary>
        /// APIサーバーを開始
        /// </summary>
        public async Task StartServerAsync()
        {
            try
            {
                // CocoroConsole APIサーバーを起動
                await _apiServer.StartAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CommunicationService: サーバー起動エラー: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"サーバー起動に失敗しました: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// APIサーバーを停止
        /// </summary>
        public async Task StopServerAsync()
        {
            try
            {
                // CocoroConsole APIサーバーを停止
                await _apiServer.StopAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CommunicationService: サーバー停止エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在の設定を取得
        /// </summary>
        public ConfigSettings GetCurrentConfig()
        {
            return _appSettings.GetConfigSettings();
        }

        /// <summary>
        /// 設定キャッシュを更新
        /// </summary>
        public void RefreshSettingsCache()
        {
            _cachedConfigSettings = _appSettings.GetConfigSettings();
        }

        /// <summary>
        /// OtomeKairo再起動開始を通知して起動待ち状態に戻す
        /// </summary>
        public void NotifyOtomeKairoRestarting()
        {
            // 再起動の切り替えを即時に反映し、ヘルスチェックを短周期に戻す
            _statusPollingService.SetWaitingForStartup();
        }

        /// <summary>
        /// AppSettings保存イベントハンドラー
        /// </summary>
        private void OnSettingsSaved(object? sender, EventArgs e)
        {
            var previousSettings = _cachedConfigSettings;
            RefreshSettingsCache();
            ApplyRuntimeSettingsChanges(previousSettings, _cachedConfigSettings);
        }

        private CocoroConsoleApiServer CreateApiServer(int port)
        {
            var server = new CocoroConsoleApiServer(port, _appSettings);
            server.ChatMessageReceived += (sender, request) => ChatMessageReceived?.Invoke(this, request);
            server.ControlCommandReceived += (sender, request) => ControlCommandReceived?.Invoke(this, request);
            server.StatusUpdateReceived += (sender, request) =>
            {
                // ステータス更新イベントを発火
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, request.message));
            };
            return server;
        }

        private void ApplyRuntimeSettingsChanges(ConfigSettings? previousSettings, ConfigSettings? currentSettings)
        {
            if (previousSettings == null || currentSettings == null)
            {
                return;
            }

            bool consolePortChanged = previousSettings.CocoroConsolePort != currentSettings.CocoroConsolePort;
            bool shellPortChanged = previousSettings.cocoroShellPort != currentSettings.cocoroShellPort;
            bool ghostPortChanged = previousSettings.cocoroCorePort != currentSettings.cocoroCorePort;
            bool ghostHostChanged = !string.Equals(
                previousSettings.otomeKairoHost ?? string.Empty,
                currentSettings.otomeKairoHost ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            bool useExternalGhostChanged =
                (previousSettings.useExternalOtomeKairo ?? false) !=
                (currentSettings.useExternalOtomeKairo ?? false);
            bool ghostEndpointChanged = ghostPortChanged || ghostHostChanged || useExternalGhostChanged;
            bool bearerTokenChanged = !string.Equals(previousSettings.otomeKairoBearerToken ?? string.Empty,
                currentSettings.otomeKairoBearerToken ?? string.Empty, StringComparison.Ordinal);

            if (consolePortChanged)
            {
                _ = RestartApiServerAsync(currentSettings.CocoroConsolePort);
            }

            if (shellPortChanged)
            {
                _shellClient?.Dispose();
                _shellClient = new CocoroShellClient(currentSettings.cocoroShellPort);
            }

            if (ghostEndpointChanged)
            {
                ResetStatusPollingService();
            }

            if (ghostEndpointChanged || bearerTokenChanged)
            {
                UpdateOtomeKairoApiClient(currentSettings);
                _initialSettingsFetched = false;

                _ = StopEventsStreamAsync();
                _ = StopLogStreamAsync();
            }
        }

        private async Task RestartApiServerAsync(int newPort)
        {
            var oldServer = _apiServer;
            bool wasRunning = oldServer.IsRunning;
            try
            {
                if (wasRunning)
                {
                    await oldServer.StopAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CommunicationService: サーバー再起動停止エラー: {ex.Message}");
            }
            finally
            {
                oldServer.Dispose();
            }

            _apiServer = CreateApiServer(newPort);

            if (wasRunning)
            {
                try
                {
                    await _apiServer.StartAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CommunicationService: サーバー再起動起動エラー: {ex.Message}");
                    ErrorOccurred?.Invoke(this, $"サーバー再起動に失敗しました: {ex.Message}");
                }
            }
        }

        private void ResetStatusPollingService()
        {
            _statusPollingService.StatusChanged -= OnStatusPollingServiceStatusChanged;
            _statusPollingService.Dispose();

            _statusPollingService = new StatusPollingService(_appSettings.GetOtomeKairoBaseUrl());
            _statusPollingService.StatusChanged += OnStatusPollingServiceStatusChanged;
        }

        private void UpdateOtomeKairoApiClient(ConfigSettings settings)
        {
            // 変更（ポート/トークン）に追従するため、既存クライアントは破棄して作り直す
            _otomeKairoApiClient?.Dispose();
            _otomeKairoApiClient = null;

            var bearerToken = settings.otomeKairoBearerToken ?? string.Empty;
            var baseUrl = _appSettings.GetOtomeKairoBaseUrl();
            _otomeKairoApiClient = new OtomeKairoApiClient(baseUrl, bearerToken);
        }


        /// <summary>
        /// StatusPollingServiceのステータス変更ハンドラ
        /// </summary>
        private void OnStatusPollingServiceStatusChanged(object? sender, OtomeKairoStatus status)
        {
            // 外部イベントに転送
            StatusChanged?.Invoke(this, status);

            // 初回Normal時に OtomeKairo への接続初期化を行う
            if (!_initialSettingsFetched && status == OtomeKairoStatus.Normal)
            {
                _initialSettingsFetched = true;
                _ = FetchAndApplySettingsFromOtomeKairoAsync();
            }

            if (status == OtomeKairoStatus.WaitingForStartup)
            {
                // 起動待ちに戻った場合は旧イベントストリームを停止
                _ = StopEventsStreamAsync();
            }
        }

        /// <summary>
        /// otomekairoから設定を取得してキャッシュ/イベントに反映
        /// </summary>
        public async Task FetchAndApplySettingsFromOtomeKairoAsync()
        {
            if (_otomeKairoApiClient == null)
            {
                Debug.WriteLine("[CommunicationService] APIクライアントが初期化されていないため、接続初期化をスキップ");
                return;
            }

            try
            {
                Debug.WriteLine("[CommunicationService] OtomeKairo への接続初期化を開始します");
                await EnsureOtomeKairoReadyAsync().ConfigureAwait(false);
                Debug.WriteLine("[CommunicationService] OtomeKairo への接続初期化を完了しました");

                // --- status.settings_snapshot から desktop_watch の現在値を UI へ流す ---
                var statusResponse = await _otomeKairoApiClient.GetOtomeKairoStatusAsync().ConfigureAwait(false);
                var desktopWatchEnabled = TryReadDesktopWatchEnabled(statusResponse.SettingsSnapshot);
                OtomeKairoSettingsUpdated?.Invoke(this, new OtomeKairoSettings
                {
                    DesktopWatchEnabled = desktopWatchEnabled
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommunicationService] OtomeKairo への接続初期化に失敗: {ex.Message}");
            }
        }

        public Task RefreshOtomeKairoSettingsAsync()
        {
            return FetchAndApplySettingsFromOtomeKairoAsync();
        }

        private static bool TryReadDesktopWatchEnabled(Dictionary<string, object?> settingsSnapshot)
        {
            // --- settings_snapshot.desktop_watch.enabled を安全に読む ---
            if (!settingsSnapshot.TryGetValue("desktop_watch", out var desktopWatchValue))
            {
                return false;
            }

            if (desktopWatchValue is System.Text.Json.JsonElement element &&
                element.ValueKind == System.Text.Json.JsonValueKind.Object &&
                element.TryGetProperty("enabled", out var enabledElement) &&
                (enabledElement.ValueKind == System.Text.Json.JsonValueKind.True || enabledElement.ValueKind == System.Text.Json.JsonValueKind.False))
            {
                return enabledElement.GetBoolean();
            }

            return false;
        }

        /// <summary>
        /// 新しい会話セッションを開始
        /// </summary>
        public void StartNewConversation()
        {
            Debug.WriteLine("新しい会話を開始しました");
        }

        /// <summary>
        /// OtomeKairoにチャットメッセージを送信（HTTP/SSE）
        /// </summary>
        /// <param name="message">送信メッセージ</param>
        /// <param name="characterName">キャラクター名（オプション）</param>
        /// <param name="imageDataUrl">画像データURL（オプション）</param>
        public async Task SendChatToCoreUnifiedAsync(string message, string? characterName = null, string? imageDataUrl = null)
        {
            // 単一画像を配列に変換して複数画像対応版を呼び出し
            var imageDataUrls = imageDataUrl != null ? new List<string> { imageDataUrl } : null;
            await SendChatToCoreUnifiedAsync(message, characterName, imageDataUrls);
        }

        /// <summary>
        /// CocoroCoreへメッセージを送信（複数画像対応）
        /// </summary>
        /// <param name="message">送信メッセージ</param>
        /// <param name="characterName">キャラクター名（オプション）</param>
        /// <param name="imageDataUrls">画像データURLリスト（オプション）</param>
        public async Task SendChatToCoreUnifiedAsync(string message, string? characterName = null, List<string>? imageDataUrls = null)
        {
            if (_otomeKairoApiClient == null)
            {
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, "OtomeKairo APIクライアントを初期化できませんでした"));
                return;
            }

            await SendChatViaHttpStreamingAsync(message, imageDataUrls);
        }

        private async Task SendChatViaHttpStreamingAsync(string message, List<string>? imageDataUrls)
        {
            // --- 同時送信を直列化する ---
            await _chatSendSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                // --- 送信中状態をセットする ---
                SetChatBusy(true);

                // --- LLMが無効の場合は処理しない ---
                if (!_appSettings.IsUseLLM)
                {
                    Debug.WriteLine("チャット送信: LLMが無効のためスキップ");
                    return;
                }

                // --- client_id は bootstrap 済みトークンと合わせて管理する ---
                EnsureClientIdInitialized();

                // --- 空入力は送らない ---
                if (string.IsNullOrWhiteSpace(message))
                {
                    var errorMessage = "メッセージを入力してください";
                    StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                    {
                        Content = string.Empty,
                        IsFinished = true,
                        IsError = true,
                        ErrorMessage = errorMessage
                    });
                    StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, errorMessage));
                    return;
                }

                // --- 現在の OtomeKairo API は画像付き会話に未対応 ---
                if (imageDataUrls != null && imageDataUrls.Any(url => !string.IsNullOrWhiteSpace(url)))
                {
                    var errorMessage = "現在の OtomeKairo API では画像付き会話は未対応です";
                    StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                    {
                        Content = string.Empty,
                        IsFinished = true,
                        IsError = true,
                        ErrorMessage = errorMessage
                    });
                    StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, errorMessage));
                    return;
                }

                // --- 送信前に bootstrap と認証状態を整える ---
                await EnsureOtomeKairoReadyAsync().ConfigureAwait(false);
                if (_otomeKairoApiClient == null || string.IsNullOrWhiteSpace(_appSettings.OtomeKairoBearerToken))
                {
                    var errorMessage = "OtomeKairo の console_access_token を取得できませんでした";
                    StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                    {
                        Content = string.Empty,
                        IsFinished = true,
                        IsError = true,
                        ErrorMessage = errorMessage
                    });
                    StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, errorMessage));
                    return;
                }

                // --- 送信中ステータスを反映する ---
                _statusPollingService.SetProcessingStatus(OtomeKairoStatus.ProcessingMessage);
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "チャット送信開始"));

                // --- OtomeKairo の会話観測 API を呼ぶ ---
                var response = await _otomeKairoApiClient.ObserveConversationAsync(new OtomeKairoConversationRequest
                {
                    Text = message,
                    ClientContext = BuildOtomeKairoClientContext()
                }).ConfigureAwait(false);

                // --- 応答種別ごとに UI へ反映する ---
                if (string.Equals(response.ResultKind, "reply", StringComparison.Ordinal))
                {
                    var replyText = response.Reply?.Text ?? string.Empty;
                    StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                    {
                        Content = replyText,
                        IsFinished = true,
                        IsError = false
                    });

                    if (!string.IsNullOrWhiteSpace(replyText))
                    {
                        var chatReply = new ChatRequest
                        {
                            memoryId = _cachedMemoryId,
                            sessionId = response.CycleId,
                            message = replyText,
                            role = "assistant",
                            content = replyText
                        };

                        ChatMessageReceived?.Invoke(this, chatReply);
                        ForwardMessageToShellAsync(replyText, GetStoredCharacterSetting());
                    }

                    _statusPollingService.SetNormalStatus();
                    StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "チャット完了"));
                    return;
                }

                if (string.Equals(response.ResultKind, "noop", StringComparison.Ordinal))
                {
                    _statusPollingService.SetNormalStatus();
                    StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "今回は応答しませんでした"));
                    return;
                }

                var internalFailureMessage = "OtomeKairo 内部処理に失敗しました";
                StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                {
                    Content = string.Empty,
                    IsFinished = true,
                    IsError = true,
                    ErrorMessage = internalFailureMessage
                });
                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, internalFailureMessage));
            }
            catch (OtomeKairoApiException ex)
            {
                var errorMessage = ex.ErrorCode switch
                {
                    "invalid_token" => "OtomeKairo の認証に失敗しました。接続設定を確認してください。",
                    "bootstrap_required" => "OtomeKairo の初回登録がまだ完了していません。",
                    _ => ex.Message,
                };
                Debug.WriteLine($"OtomeKairo APIエラー: {errorMessage}");
                StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                {
                    Content = string.Empty,
                    IsFinished = true,
                    IsError = true,
                    ErrorMessage = errorMessage
                });
                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"チャット送信エラー: {errorMessage}"));
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine($"チャット送信タイムアウト: {ex.Message}");
                StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                {
                    Content = string.Empty,
                    IsFinished = true,
                    IsError = true,
                    ErrorMessage = $"チャット送信タイムアウト: {ex.Message}"
                });
                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"チャット送信タイムアウト: {ex.Message}"));
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"チャット送信HTTPエラー: {ex.Message}");
                StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                {
                    Content = string.Empty,
                    IsFinished = true,
                    IsError = true,
                    ErrorMessage = $"チャット送信HTTPエラー: {ex.Message}"
                });
                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"チャット送信HTTPエラー: {ex.Message}"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"チャット送信エラー: {ex.Message}");
                StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                {
                    Content = string.Empty,
                    IsFinished = true,
                    IsError = true,
                    ErrorMessage = $"チャット送信エラー: {ex.Message}"
                });
                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"チャット送信エラー: {ex.Message}"));
            }
            finally
            {
                // --- 送信状態を解除して次の送信を許可する ---
                try
                {
                    SetChatBusy(false);
                }
                finally
                {
                    _chatSendSemaphore.Release();
                }
            }
        }

        private Dictionary<string, object?> BuildOtomeKairoClientContext()
        {
            // --- 送信元と表示コンテキストだけを OtomeKairo 側へ渡す ---
            var snapshot = GetClientContextSnapshot();
            return new Dictionary<string, object?>
            {
                ["source"] = "CocoroConsole",
                ["client_id"] = _appSettings.ClientId,
                ["active_app"] = snapshot.ActiveApp,
                ["window_title"] = snapshot.WindowTitle,
                ["locale"] = snapshot.Locale,
            };
        }

        /// <summary>
        /// /api/chat の error イベントを表示向けに整形する。
        /// </summary>
        private static string FormatChatStreamErrorMessage(string? message, string? code)
        {
            // --- 正規化（空白のみは null 扱い） ---
            var cleanMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
            var cleanCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();

            // --- 排他（同時実行） ---
            if (cleanCode == "chat_busy")
            {
                var fallback = "他のチャット処理中です。応答が完了してから再送してください。";
                return $"{(cleanMessage ?? fallback)} (code={cleanCode})";
            }

            // --- メッセージが無い場合はコードベースで返す ---
            if (cleanMessage == null)
            {
                return cleanCode == null
                    ? "チャットAPIエラーが発生しました"
                    : $"チャットAPIエラーが発生しました (code={cleanCode})";
            }

            // --- メッセージがある場合はコードがあれば添える（デバッグ用途） ---
            return cleanCode == null ? cleanMessage : $"{cleanMessage} (code={cleanCode})";
        }

        public Task SetDesktopWatchEnabledAsync(bool enabled)
        {
            // --- 現在の OtomeKairo API では desktop watch 設定操作を公開していない ---
            var message = "現在の OtomeKairo API ではデスクトップウォッチ設定変更は未対応です";
            Debug.WriteLine($"[DesktopWatch] {message}");
            StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, message));
            return Task.CompletedTask;
        }


        /// <summary>
        /// CocoroShellにアニメーションコマンドを送信
        /// </summary>
        /// <param name="animationName">アニメーション名</param>
        public async Task SendAnimationToShellAsync(string animationName)
        {
            try
            {
                if (!ShouldForwardToShell())
                {
                    Debug.WriteLine($"[Shell Forward] VRM表示OFFのためアニメーション転送をスキップ: {animationName}");
                    return;
                }

                var request = new AnimationRequest
                {
                    animationName = animationName
                };

                await _shellClient.SendAnimationCommandAsync(request);

                // 成功時のステータス更新
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, $"アニメーション '{animationName}' 実行"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アニメーションコマンド送信エラー: {ex.Message}");
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"アニメーション制御エラー: {ex.Message}"));
            }
        }

        /// <summary>
        /// 保存済みの現在のキャラクター設定を取得（キャッシュ使用）
        /// </summary>
        private CharacterSettings? GetStoredCharacterSetting()
        {
            // キャッシュされた設定を使用
            var config = _cachedConfigSettings;
            if (config?.characterList != null &&
                config.currentCharacterIndex >= 0 &&
                config.currentCharacterIndex < config.characterList.Count)
            {
                return config.characterList[config.currentCharacterIndex];
            }
            return null;
        }

        /// <summary>
        /// CocoroShellにメッセージを転送（ノンブロッキング）
        /// </summary>
        /// <param name="content">転送するメッセージ内容</param>
        /// <param name="currentCharacter">現在のキャラクター設定</param>
        private async void ForwardMessageToShellAsync(string content, CharacterSettings? currentCharacter)
        {
            await _forwardMessageSemaphore.WaitAsync();
            try
            {
                if (string.IsNullOrEmpty(content))
                {
                    return;
                }

                if (!ShouldForwardToShell(currentCharacter))
                {
                    Debug.WriteLine("[Shell Forward] VRM表示OFFのためチャット転送をスキップ");
                    return;
                }

                var shellRequest = new ShellChatRequest
                {
                    content = content,
                    animation = "talk",
                    characterName = currentCharacter?.modelName
                };

                await _shellClient.SendChatMessageAsync(shellRequest);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Shell Forward] CocoroShellへの転送エラー: {ex.Message}");
            }
            finally
            {
                _forwardMessageSemaphore.Release();
            }
        }


        /// <summary>
        /// 通知メッセージ受信イベントを発火（内部使用）
        /// </summary>
        /// <param name="notification">通知メッセージペイロード</param>
        /// <param name="imageSources">画像データリスト（オプション）</param>
        public void RaiseNotificationMessageReceived(ChatMessagePayload notification, List<System.Windows.Media.Imaging.BitmapSource>? imageSources = null)
        {
            NotificationMessageReceived?.Invoke(notification, imageSources);
        }


        /// <summary>
        /// CocoroShellにTTS状態を送信
        /// </summary>
        /// <param name="isUseTTS">TTS使用状態</param>
        public async Task SendTTSStateToShellAsync(bool isUseTTS)
        {
            try
            {
                if (!ShouldForwardToShell())
                {
                    Debug.WriteLine($"[Shell Forward] VRM表示OFFのためTTS状態転送をスキップ: enabled={isUseTTS}");
                    return;
                }

                var request = new ShellControlRequest
                {
                    action = "ttsControl",
                    @params = new Dictionary<string, object>
                    {
                        { "enabled", isUseTTS }
                    }
                };

                await _shellClient.SendControlCommandAsync(request);

                // 成功時のステータス更新
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, isUseTTS ? "音声合成有効" : "音声合成無効"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TTS状態送信エラー: {ex.Message}");
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"TTS設定通知エラー"));
            }
        }


        /// <summary>
        /// ログビューアーウィンドウを開く
        /// </summary>
        public void OpenLogViewer()
        {
            // MainWindowのOpenLogViewerメソッドに委譲
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.OpenLogViewer();
            }
        }

        /// <summary>
        /// ログストリーム接続を開始
        /// </summary>
        public async Task StartLogStreamAsync()
        {
            if (_logStreamClient != null)
            {
                return;
            }

            var bearerToken = _appSettings.OtomeKairoBearerToken;
            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                return;
            }

            var logStreamUri = new Uri($"{_appSettings.GetOtomeKairoWebSocketBaseUrl()}/api/logs/stream");
            _logStreamClient = new LogStreamClient(logStreamUri, bearerToken);
            _logStreamClient.LogsReceived += OnLogStreamLogsReceived;
            _logStreamClient.ConnectionStateChanged += OnLogStreamConnectionStateChanged;
            _logStreamClient.ErrorOccurred += OnLogStreamErrorOccurred;

            try
            {
                await _logStreamClient.StartAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログストリーム接続に失敗しました: {ex.Message}");
                await StopLogStreamAsync();
            }
        }

        /// <summary>
        /// ログストリーム接続を停止
        /// </summary>
        public async Task StopLogStreamAsync()
        {
            if (_logStreamClient == null)
            {
                return;
            }

            try
            {
                await _logStreamClient.StopAsync();
            }
            finally
            {
                _logStreamClient.LogsReceived -= OnLogStreamLogsReceived;
                _logStreamClient.ConnectionStateChanged -= OnLogStreamConnectionStateChanged;
                _logStreamClient.ErrorOccurred -= OnLogStreamErrorOccurred;
                _logStreamClient.Dispose();
                _logStreamClient = null;
            }
        }

        private void OnLogStreamLogsReceived(object? sender, IReadOnlyList<LogMessage> logs)
        {
            LogMessagesReceived?.Invoke(this, logs);
        }

        private void OnLogStreamConnectionStateChanged(object? sender, bool isConnected)
        {
            LogStreamConnectionChanged?.Invoke(this, isConnected);
        }

        private void OnLogStreamErrorOccurred(object? sender, string errorMessage)
        {
            LogStreamError?.Invoke(this, errorMessage);
        }

        /// <summary>
        /// /api/mood/debug のポーリングを開始（1秒間隔）
        /// </summary>
        public Task StartMoodDebugPollingAsync()
        {
            // --- 現在の OtomeKairo API では mood/debug を公開していない ---
            MoodDebugError?.Invoke(this, "現在の OtomeKairo API では mood/debug は未対応です");
            return Task.CompletedTask;
        }

        /// <summary>
        /// /api/mood/debug のポーリングを停止
        /// </summary>
        public async Task StopMoodDebugPollingAsync()
        {
            CancellationTokenSource? cts;
            Task? pollingTask;

            // --- 参照を切ってからキャンセル/待機する（ロック時間を最小化） ---
            lock (_moodDebugPollingLock)
            {
                cts = _moodDebugPollingCts;
                pollingTask = _moodDebugPollingTask;
                _moodDebugPollingCts = null;
                _moodDebugPollingTask = null;
            }

            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }

            try
            {
                if (pollingTask != null)
                {
                    await pollingTask.ConfigureAwait(false);
                }
            }
            catch
            {
                // --- 停止処理の例外は握りつぶす（UI/シャットダウンを阻害しない） ---
            }
        }

        /// <summary>
        /// /api/mood/debug を定期取得してイベント転送する（単一ループ・直列実行）
        /// </summary>
        private async Task MoodDebugPollingLoopAsync(CancellationToken cancellationToken)
        {
            // --- 1秒ごとのポーリング（処理が遅い場合は「終わってから1秒後」になる） ---
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // --- APIクライアント未初期化（トークン未設定等）の場合はエラー通知して待機 ---
                    if (_otomeKairoApiClient == null)
                    {
                        MoodDebugError?.Invoke(this, "otomekairo のBearerトークンが未設定のため、感情デバッグを取得できません。");
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    // --- 1秒ポーリング前提のため、1回の取得は短いタイムアウトにする ---
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(900));

                    var response = await _otomeKairoApiClient.GetMoodDebugAsync(timeoutCts.Token).ConfigureAwait(false);
                    MoodDebugUpdated?.Invoke(this, new MoodDebugUpdatedEventArgs(response, DateTimeOffset.Now));
                }
                catch (OperationCanceledException)
                {
                    // --- アプリ終了/停止要求時は静かに抜ける。タイムアウトはエラーとして扱う ---
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        MoodDebugError?.Invoke(this, "感情デバッグの取得がタイムアウトしました。");
                    }
                }
                catch (Exception ex)
                {
                    MoodDebugError?.Invoke(this, $"感情デバッグの取得に失敗しました: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // --- 停止要求 ---
                    break;
                }
            }
        }

        private async Task StartEventsStreamAsync()
        {
            if (_eventsStreamClient != null)
            {
                return;
            }

            var bearerToken = _appSettings.OtomeKairoBearerToken;
            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                return;
            }

            // events stream の hello を確実に送るため、ClientId を用意しておく
            EnsureClientIdInitialized();

            var eventsStreamUri = new Uri($"{_appSettings.GetOtomeKairoWebSocketBaseUrl()}/api/events/stream");
            _eventsStreamClient = new EventsStreamClient(
                eventsStreamUri,
                bearerToken,
                _appSettings.ClientId,
                new[] { "vision.desktop", "vision.camera" }
            );
            _eventsStreamClient.EventReceived += OnEventsStreamEventReceived;
            _eventsStreamClient.ConnectionStateChanged += OnEventsStreamConnectionStateChanged;
            _eventsStreamClient.ErrorOccurred += OnEventsStreamErrorOccurred;

            try
            {
                await _eventsStreamClient.StartAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"イベントストリーム接続に失敗しました: {ex.Message}");
                await StopEventsStreamAsync();
            }
        }

        private async Task StopEventsStreamAsync()
        {
            if (_eventsStreamClient == null)
            {
                return;
            }

            try
            {
                await _eventsStreamClient.StopAsync();
            }
            finally
            {
                _eventsStreamClient.EventReceived -= OnEventsStreamEventReceived;
                _eventsStreamClient.ConnectionStateChanged -= OnEventsStreamConnectionStateChanged;
                _eventsStreamClient.ErrorOccurred -= OnEventsStreamErrorOccurred;
                _eventsStreamClient.Dispose();
                _eventsStreamClient = null;
            }
        }

        private void OnEventsStreamConnectionStateChanged(object? sender, bool isConnected)
        {
            var message = isConnected
                ? "イベントストリームに接続しました"
                : "イベントストリームが切断されました";
            StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(isConnected, message));
        }

        private void OnEventsStreamEventReceived(object? sender, OtomeKairoEvent ev)
        {
            try
            {
                if (string.Equals(ev.Type, "reminder", StringComparison.OrdinalIgnoreCase))
                {
                    var message = ev.Data.Message;
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        HandlePartnerMessageFromEvent(ev, message);
                    }
                    return;
                }

                if (string.Equals(ev.Type, "notification", StringComparison.OrdinalIgnoreCase))
                {
                    var systemText = BuildNotificationDisplayText(ev.Data.SystemText);
                    if (!string.IsNullOrWhiteSpace(systemText))
                    {
                        var extractedSourceSystem = TryExtractBracketedSourceSystem(systemText);
                        var from = extractedSourceSystem ?? "notification";
                        var displayText = systemText;
                        if (!string.IsNullOrWhiteSpace(extractedSourceSystem))
                        {
                            var prefix = $"[{extractedSourceSystem}]";
                            if (displayText.StartsWith(prefix, StringComparison.Ordinal))
                            {
                                displayText = displayText.Substring(prefix.Length).TrimStart();
                            }
                        }
                        NotificationMessageReceived?.Invoke(new ChatMessagePayload
                        {
                            from = from,
                            sessionId = ev.EventId.ToString(CultureInfo.InvariantCulture),
                            message = displayText
                        }, TryDecodeBitmapSourcesFromDataUris(ev.Data.Images));
                    }

                    // /api/v2/notification は AI 人格のセリフ（data.message）を生成し、/api/events/stream へ配信する
                    var partnerMessage = ev.Data.Message;
                    if (!string.IsNullOrWhiteSpace(partnerMessage))
                    {
                        HandlePartnerMessageFromEvent(ev, partnerMessage);
                    }
                    return;
                }

                if (string.Equals(ev.Type, "meta-request", StringComparison.OrdinalIgnoreCase))
                {
                    // /api/v2/meta-request の結果は events stream の data.message で届く
                    var message = ev.Data.Message;
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        return;
                    }
                    HandlePartnerMessageFromEvent(ev, message);
                    return;
                }

                if (string.Equals(ev.Type, "desktop_watch", StringComparison.OrdinalIgnoreCase))
                {
                    var systemText = BuildNotificationDisplayText(ev.Data.SystemText);
                    if (!string.IsNullOrWhiteSpace(systemText))
                    {
                        var extractedSourceSystem = TryExtractBracketedSourceSystem(systemText);
                        var from = extractedSourceSystem ?? "desktop_watch";
                        if (string.Equals(from, "desktop_watch", StringComparison.OrdinalIgnoreCase))
                        {
                            from = "Desktop Watch";
                        }
                        var displayText = systemText;
                        if (!string.IsNullOrWhiteSpace(extractedSourceSystem))
                        {
                            var prefix = $"[{extractedSourceSystem}]";
                            if (displayText.StartsWith(prefix, StringComparison.Ordinal))
                            {
                                displayText = displayText.Substring(prefix.Length).TrimStart();
                            }
                        }

                        // desktop_watch の通知は system_text が "[desktop_watch]" のみになることがあるため、
                        // 直前の vision.capture_request(目的: desktop_watch) の画像があれば添付して表示する。
                        var images = TryDecodeBitmapSourcesFromDataUris(ev.Data.Images);
                        var windowTitle = (string?)null;

                        // events stream に画像が無い場合は、直近のローカルキャプチャを添付する
                        if (images == null)
                        {
                            (images, windowTitle) = TryConsumeDesktopWatchCapture();
                        }
                        if (string.IsNullOrWhiteSpace(displayText) && !string.IsNullOrWhiteSpace(windowTitle))
                        {
                            displayText = windowTitle;
                        }

                        NotificationMessageReceived?.Invoke(new ChatMessagePayload
                        {
                            from = from,
                            sessionId = ev.EventId.ToString(CultureInfo.InvariantCulture),
                            message = displayText
                        }, images);
                    }

                    var partnerMessage = ev.Data.Message;
                    if (!string.IsNullOrWhiteSpace(partnerMessage))
                    {
                        HandlePartnerMessageFromEvent(ev, partnerMessage);
                    }
                    return;
                }

                if (string.Equals(ev.Type, "vision.capture_request", StringComparison.OrdinalIgnoreCase))
                {
                    _ = Task.Run(() => HandleVisionCaptureRequestAsync(ev));
                    return;
                }

                // Fallback: unknown type but has persona message (e.g. desktop_watch proactive)
                if (!string.IsNullOrWhiteSpace(ev.Data.Message))
                {
                    HandlePartnerMessageFromEvent(ev, ev.Data.Message);
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"イベント処理エラー: {ex.Message}");
            }
        }

        private static string BuildNotificationDisplayText(string? systemText)
        {
            var systemTextTrimmed = systemText?.Trim();
            if (!string.IsNullOrEmpty(systemTextTrimmed))
            {
                return systemTextTrimmed;
            }

            return string.Empty;
        }

        private void HandlePartnerMessageFromEvent(OtomeKairoEvent ev, string partnerMessage)
        {
            var chatReply = new ChatRequest
            {
                memoryId = string.Empty,
                sessionId = ev.EventId.ToString(CultureInfo.InvariantCulture),
                message = partnerMessage,
                role = "assistant",
                content = partnerMessage
            };

            ChatMessageReceived?.Invoke(this, chatReply);

            var currentCharacter = GetStoredCharacterSetting();
            ForwardMessageToShellAsync(partnerMessage, currentCharacter);
        }

        private static string? TryExtractBracketedSourceSystem(string text)
        {
            // system_text format example: "[gmail] ...", extract gmail for display if possible
            if (string.IsNullOrWhiteSpace(text) || text[0] != '[')
            {
                return null;
            }

            var closeIndex = text.IndexOf(']');
            if (closeIndex <= 1)
            {
                return null;
            }

            var extracted = text.Substring(1, closeIndex - 1).Trim();
            return string.IsNullOrEmpty(extracted) ? null : extracted;
        }

        private async Task HandleVisionCaptureRequestAsync(OtomeKairoEvent ev)
        {
            if (_otomeKairoApiClient == null)
            {
                return;
            }

            var requestId = ev.Data.RequestId;
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            var timeoutMs = ev.Data.TimeoutMs ?? 5000;
            if (timeoutMs <= 0)
            {
                timeoutMs = 5000;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

            var clientContext = GetClientContextSnapshot();
            var response = new VisionCaptureResponseRequest
            {
                RequestId = requestId!,
                ClientId = _appSettings.ClientId,
                ClientContext = clientContext,
                Images = new List<string>(),
                Error = null
            };

            try
            {
                var source = ev.Data.Source?.Trim().ToLowerInvariant();
                var purpose = ev.Data.Purpose?.Trim();
                var isDesktopWatchPurpose = string.Equals(purpose, "desktop_watch", StringComparison.OrdinalIgnoreCase);
                if (string.Equals(source, "desktop", StringComparison.Ordinal))
                {
                    _statusPollingService.SetProcessingStatus(OtomeKairoStatus.ProcessingImage);
                    var (dataUri, windowTitle, captureError) = await CaptureDesktopStillAsync(cts.Token);

                    if (clientContext != null && !string.IsNullOrWhiteSpace(windowTitle))
                    {
                        clientContext.WindowTitle = windowTitle;
                    }

                    if (!string.IsNullOrWhiteSpace(dataUri))
                    {
                        response.Images.Add(dataUri);

                        if (isDesktopWatchPurpose)
                        {
                            CacheDesktopWatchCapture(dataUri, windowTitle);
                        }
                    }
                    else
                    {
                        // スキップ時は理由を返す（画像なしで応答する）
                        response.Error = captureError ?? "capture skipped";

                        if (isDesktopWatchPurpose)
                        {
                            ClearDesktopWatchCapture();
                        }
                    }
                }
                else
                {
                    response.Error = $"unsupported source: {ev.Data.Source}";
                }
            }
            catch (Exception ex)
            {
                response.Error = ex.Message;
            }
            finally
            {
                _statusPollingService.SetNormalStatus();
            }

            try
            {
                await _otomeKairoApiClient.SendVisionCaptureResponseAsync(response, cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Vision] capture-response送信失敗: {ex.Message}");
            }
        }

        private async Task<(string? DataUri, string? WindowTitle, string? Error)> CaptureDesktopStillAsync(CancellationToken cancellationToken)
        {
            using var service = new ScreenshotService(intervalMinutes: 1);

            // デスクトップウォッチのアイドルタイムアウト（分）を反映（0 は無効）
            service.IdleTimeoutMinutes = _appSettings.ScreenshotSettings.idleTimeoutMinutes;

            // スクショ除外（ウィンドウタイトル正規表現）を反映
            service.SetExcludePatterns(_appSettings.ScreenshotSettings.excludePatterns);
            var screenshot = await service.CaptureActiveWindowAsync().WaitAsync(cancellationToken);
            if (screenshot == null)
            {
                // スキップ理由に応じてエラー文字列を返す（呼び出し元で response.Error に入れる）
                return service.LastSkipReason switch
                {
                    ScreenshotSkipReason.Idle => (null, null, "capture skipped (idle)"),
                    ScreenshotSkipReason.ExcludedWindowTitle => (null, null, "capture skipped (excluded window title)"),
                    _ => (null, null, "capture skipped")
                };
            }

            var dataUri = $"data:image/png;base64,{screenshot.ImageBase64}";
            return (dataUri, screenshot.WindowTitle, null);
        }

        private void CacheDesktopWatchCapture(string dataUri, string? windowTitle)
        {
            var bitmap = TryDecodeBitmapSourceFromDataUri(dataUri);
            if (bitmap == null)
            {
                return;
            }

            lock (_desktopWatchCaptureLock)
            {
                _lastDesktopWatchCapture = (DateTime.UtcNow, new List<BitmapSource> { bitmap }, windowTitle);
            }
        }

        private void ClearDesktopWatchCapture()
        {
            lock (_desktopWatchCaptureLock)
            {
                _lastDesktopWatchCapture = null;
            }
        }

        private (List<BitmapSource>? Images, string? WindowTitle) TryConsumeDesktopWatchCapture()
        {
            lock (_desktopWatchCaptureLock)
            {
                if (_lastDesktopWatchCapture == null)
                {
                    return (null, null);
                }

                var (timestampUtc, images, windowTitle) = _lastDesktopWatchCapture.Value;
                if (DateTime.UtcNow - timestampUtc > DesktopWatchCaptureMaxAge)
                {
                    _lastDesktopWatchCapture = null;
                    return (null, null);
                }

                _lastDesktopWatchCapture = null;
                return (images, windowTitle);
            }
        }

        private static BitmapSource? TryDecodeBitmapSourceFromDataUri(string dataUri)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dataUri))
                {
                    return null;
                }

                // data:image/*;base64,... の形式を想定
                if (!dataUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                const string marker = "base64,";
                var markerIndex = dataUri.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0)
                {
                    return null;
                }

                // MIME を抽出（例: data:image/png;base64,... → image/png）
                var header = dataUri.Substring("data:".Length, markerIndex - "data:".Length);
                var semicolonIndex = header.IndexOf(';');
                var mimeType = (semicolonIndex >= 0 ? header.Substring(0, semicolonIndex) : header).Trim();

                // 通知/チャットで許可される MIME のみを表示対象にする
                if (!IsAllowedImageMimeType(mimeType))
                {
                    return null;
                }

                var base64 = dataUri.Substring(markerIndex + marker.Length);
                if (string.IsNullOrWhiteSpace(base64))
                {
                    return null;
                }

                // base64 部は改行等の空白を含み得るため除去してからデコードする
                var normalizedBase64 = RemoveWhitespace(base64);
                var bytes = Convert.FromBase64String(normalizedBase64);
                using var ms = new MemoryStream(bytes);

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Data URI 文字列リストを BitmapSource リストへ変換する（変換できない画像は無視）。
        /// </summary>
        private static List<BitmapSource>? TryDecodeBitmapSourcesFromDataUris(IReadOnlyList<string>? dataUris)
        {
            if (dataUris == null || dataUris.Count == 0)
            {
                return null;
            }

            // UI 用に BitmapSource へ変換（Freeze 済みで返る）
            var images = new List<BitmapSource>(capacity: dataUris.Count);
            foreach (var dataUri in dataUris)
            {
                var bitmap = TryDecodeBitmapSourceFromDataUri(dataUri);
                if (bitmap == null)
                {
                    continue;
                }

                images.Add(bitmap);
            }

            return images.Count > 0 ? images : null;
        }

        /// <summary>
        /// 通知/チャットで許可する画像 MIME か判定する。
        /// </summary>
        private static bool IsAllowedImageMimeType(string mimeType)
        {
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                return false;
            }

            // Ghost 側仕様に合わせる（image/png / image/jpeg / image/webp）
            return string.Equals(mimeType, "image/png", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(mimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(mimeType, "image/webp", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 文字列中の空白（改行/タブ含む）を除去する。
        /// </summary>
        private static string RemoveWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            // base64 の正規化（空白除去）
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (char.IsWhiteSpace(ch))
                {
                    continue;
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        private VisionClientContext GetClientContextSnapshot()
        {
            var (windowTitle, activeApp) = TryGetActiveWindowContext();
            return new VisionClientContext
            {
                ActiveApp = activeApp,
                WindowTitle = windowTitle,
                Locale = CultureInfo.CurrentUICulture.Name
            };
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static (string? WindowTitle, string? ActiveApp) TryGetActiveWindowContext()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    return (null, null);
                }

                var titleLength = GetWindowTextLength(hwnd);
                var titleBuilder = new StringBuilder(Math.Max(0, titleLength) + 1);
                GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
                var title = titleBuilder.ToString();

                string? activeApp = null;
                if (GetWindowThreadProcessId(hwnd, out var pid) != 0 && pid != 0)
                {
                    try
                    {
                        activeApp = Process.GetProcessById((int)pid).ProcessName;
                    }
                    catch
                    {
                        activeApp = null;
                    }
                }

                return (string.IsNullOrWhiteSpace(title) ? null : title, activeApp);
            }
            catch
            {
                return (null, null);
            }
        }

        private static OtomeKairoSettingsUpdateRequest CreateSettingsUpdateRequest(OtomeKairoSettings latestSettings)
        {
            latestSettings ??= new OtomeKairoSettings();

            var activeLlmId = latestSettings.ActiveLlmPresetId ?? latestSettings.LlmPreset.FirstOrDefault()?.LlmPresetId;
            var activeEmbeddingId = latestSettings.ActiveEmbeddingPresetId ?? latestSettings.EmbeddingPreset.FirstOrDefault()?.EmbeddingPresetId;
            var activePersonaId = latestSettings.ActivePersonaPresetId ?? latestSettings.PersonaPreset.FirstOrDefault()?.PersonaPresetId;
            var activeAddonId = latestSettings.ActiveAddonPresetId ?? latestSettings.AddonPreset.FirstOrDefault()?.AddonPresetId;

            if (string.IsNullOrWhiteSpace(activeLlmId) ||
                string.IsNullOrWhiteSpace(activeEmbeddingId) ||
                string.IsNullOrWhiteSpace(activePersonaId) ||
                string.IsNullOrWhiteSpace(activeAddonId))
            {
                throw new InvalidOperationException("API設定のアクティブプリセットIDが取得できません。otomekairo側のsettings.dbを確認してください。");
            }

            return new OtomeKairoSettingsUpdateRequest
            {
                MemoryEnabled = latestSettings.MemoryEnabled,
                DesktopWatchEnabled = latestSettings.DesktopWatchEnabled,
                DesktopWatchIntervalSeconds = latestSettings.DesktopWatchIntervalSeconds,
                DesktopWatchTargetClientId = latestSettings.DesktopWatchTargetClientId,
                ActiveLlmPresetId = activeLlmId!,
                ActiveEmbeddingPresetId = activeEmbeddingId!,
                ActivePersonaPresetId = activePersonaId!,
                ActiveAddonPresetId = activeAddonId!,
                LlmPreset = latestSettings.LlmPreset ?? new List<LlmPreset>(),
                EmbeddingPreset = latestSettings.EmbeddingPreset ?? new List<EmbeddingPreset>(),
                PersonaPreset = latestSettings.PersonaPreset ?? new List<PersonaPreset>(),
                AddonPreset = latestSettings.AddonPreset ?? new List<AddonPreset>()
            };
        }

        private void OnEventsStreamErrorOccurred(object? sender, string errorMessage)
        {
            Debug.WriteLine($"イベントストリームエラー: {errorMessage}");
            StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"イベントストリームエラー: {errorMessage}"));
        }


        /// <summary>
        /// CocoroShellから現在のキャラクター位置を取得
        /// </summary>
        public async Task<PositionResponse> GetShellPositionAsync()
        {
            try
            {
                if (!ShouldForwardToShell())
                {
                    Debug.WriteLine("[Shell Forward] VRM表示OFFのため位置取得をスキップ");
                    return new PositionResponse
                    {
                        status = "disabled",
                        message = "VRM表示がOFFのためCocoroShellへの位置取得をスキップしました",
                        timestamp = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture)
                    };
                }

                return await _shellClient.GetPositionAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"位置取得エラー: {ex.Message}");

                // エラー時のステータス更新
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"位置取得に失敗しました: {ex.Message}"));

                throw;
            }
        }

        /// <summary>
        /// CocoroShellに設定の部分更新を送信
        /// </summary>
        /// <param name="updates">更新する設定のキーと値のペア</param>
        public async Task SendConfigPatchToShellAsync(Dictionary<string, object> updates)
        {
            try
            {
                if (!ShouldForwardToShell())
                {
                    Debug.WriteLine($"[Shell Forward] VRM表示OFFのため設定部分更新をスキップ: {string.Join(", ", updates.Keys)}");
                    return;
                }

                var changedFields = new string[updates.Count];
                updates.Keys.CopyTo(changedFields, 0);

                var patch = new ConfigPatchRequest
                {
                    updates = updates,
                    changedFields = changedFields
                };

                await _shellClient.UpdateConfigPatchAsync(patch);
                Debug.WriteLine($"設定部分更新をCocoroShellに送信しました: {string.Join(", ", changedFields)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CocoroShell設定部分更新エラー: {ex.Message}");
                throw new InvalidOperationException($"Failed to send config patch to shell: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            // イベント購読解除
            AppSettings.SettingsSaved -= OnSettingsSaved;

            // --- 感情デバッグポーリングの停止（Disposeは同期のため、キャンセルのみ行う） ---
            lock (_moodDebugPollingLock)
            {
                try
                {
                    _moodDebugPollingCts?.Cancel();
                }
                catch
                {
                    // --- Dispose中の例外は握りつぶす ---
                }
                finally
                {
                    _moodDebugPollingCts?.Dispose();
                    _moodDebugPollingCts = null;
                    _moodDebugPollingTask = null;
                }
            }

            // セマフォの解放
            _forwardMessageSemaphore?.Dispose();

            if (_statusPollingService != null)
            {
                _statusPollingService.StatusChanged -= OnStatusPollingServiceStatusChanged;
                _statusPollingService.Dispose();
            }
            _apiServer?.Dispose();
            _shellClient?.Dispose();
            _otomeKairoApiClient?.Dispose();
            _logStreamClient?.Dispose();
            _eventsStreamClient?.Dispose();
            _otomeKairoBootstrapSemaphore?.Dispose();
            _chatSendSemaphore?.Dispose();
        }
    }
}
