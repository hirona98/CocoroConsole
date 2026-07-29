using CocoroConsole.Communication;
using CocoroConsole.Models.OtomeKairoApi;
using CocoroAI.Services;
using CocoroConsole.Utilities;
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
    /// - otomekairo HTTP API（Bearer 認証）を用いた現在設定取得と会話入力送信
    /// - ログ/イベントのストリーミング接続の管理
    /// </summary>
    public class CommunicationService : ICommunicationService
    {
        private const string RequiredOtomeKairoApiVersion = "0.4.0";
        // CocoroConsole 側の HTTP API サーバー（外部クライアントからの受信）
        private CocoroConsoleApiServer _apiServer;
        private int _apiServerPort;

        // CocoroShell（Unity 側）へ送るクライアント
        private CocoroShellClient _shellClient;

        // CocoroShellが起動していない場合のWAV再生サービス
        private readonly DirectAudioPlaybackService _directAudioPlaybackService;

        private readonly IAppSettings _appSettings;

        // otomekairo の状態を定期取得して UI に通知する
        private StatusPollingService _statusPollingService;

        // otomekairo HTTP API（Bearer トークン必須）。トークン未設定時は null。
        private OtomeKairoApiClient? _otomeKairoApiClient;

        // otomekairo のログ/イベントを購読するストリームクライアント（必要時に開始/停止）
        private LogStreamClient? _logStreamClient;
        private EventsStreamClient? _eventsStreamClient;


        // シェルへの送信順序を保証するためのセマフォ（同時送信を直列化）
        private readonly SemaphoreSlim _forwardMessageSemaphore = new SemaphoreSlim(1, 1);

        // 対話入力送信順序を保証するためのセマフォ（同時送信を直列化）
        // NOTE:
        // - 現在の OtomeKairo API は 1 リクエスト 1 応答のため、二重送信だけを防げばよい。
        // - 入力経路（UI/音声など）を跨いでも「1回の送信=1応答」に揃える。
        private readonly SemaphoreSlim _conversationInputSendSemaphore = new SemaphoreSlim(1, 1);

        // OtomeKairo の bootstrap と認証確認を直列化するためのセマフォ
        private readonly SemaphoreSlim _otomeKairoBootstrapSemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _apiServerLifecycleSemaphore = new SemaphoreSlim(1, 1);
        private bool _remoteSettingsApplied;
        private bool _apiServerStartRequested;

        // 対話入力送信中フラグ（0/1）
        // UI 側の送信ボタン無効化・二重送信抑止に使う。
        private int _conversationInputBusy = 0;

        // 設定キャッシュ（SettingsSaved で更新し、差分に応じてランタイム反映）
        private ConfigSettings? _cachedConfigSettings;

        // memory_id キャッシュ（発話を UI へ戻す際に付与）
        private string _cachedMemoryId = "memory";

        // OtomeKairo が Normal になった後、現在設定と event stream を初期化済みかを表す。
        private bool _initialSettingsFetched = false;

        private static bool IsVrmDisplayEnabled(AvatarSettings? currentAvatar)
        {
            // MainWindow.LaunchCocoroShell と同じ判定: パスがあれば有効 / readOnlyキャラは常に有効
            return currentAvatar != null &&
                   (!string.IsNullOrWhiteSpace(currentAvatar.vrmFilePath) || currentAvatar.isReadOnly == true);
        }

        private bool ShouldForwardToShell(AvatarSettings? currentAvatar = null)
        {
            // 呼び出し元がアバターを渡していない場合はキャッシュから解決
            currentAvatar ??= GetStoredAvatarSetting();
            return IsVrmDisplayEnabled(currentAvatar);
        }

        private static bool IsCocoroShellProcessRunning()
        {
            var processes = Process.GetProcessesByName("CocoroShell");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        public event EventHandler<UiMessageRequest>? UiMessageReceived;
        public event EventHandler<ConversationOutputEventArgs>? ConversationOutputReceived;
        public event EventHandler<bool>? ConversationInputBusyChanged;
        public event EventHandler<ControlRequest>? ControlCommandReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<StatusUpdateEventArgs>? StatusUpdateRequested;
        public event EventHandler<OtomeKairoStatus>? StatusChanged;
        public event EventHandler<IReadOnlyList<LogMessage>>? LogMessagesReceived;
        public event EventHandler<bool>? LogStreamConnectionChanged;
        public event EventHandler<string>? LogStreamError;
        public event EventHandler<VoiceConversationInputEventArgs>? VoiceConversationInputReceived;
        public event EventHandler<OtomeKairoAudioRuntimeState>? AudioRuntimeStateChanged;

        public bool IsServerRunning => _apiServer.IsRunning;

        /// <summary>
        /// 現在のOtomeKairoステータス
        /// </summary>
        public OtomeKairoStatus CurrentStatus => _statusPollingService.CurrentStatus;

        /// <summary>
        /// 対話入力送信中かどうか
        /// </summary>
        public bool IsConversationInputBusy => Volatile.Read(ref _conversationInputBusy) == 1;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="appSettings">アプリケーション設定</param>
        public CommunicationService(IAppSettings appSettings)
        {
            _appSettings = appSettings;

            // --- ClientId は OtomeKairo の event stream の hello で必須になるため、起動時に確実に用意する ---
            EnsureClientIdInitialized();

            // APIサーバーの初期化
            _apiServer = CreateApiServer(_appSettings.CocoroConsolePort);
            _apiServerPort = _appSettings.CocoroConsolePort;

            // CocoroShellクライアントの初期化
            _shellClient = new CocoroShellClient(_appSettings.CocoroShellPort);
            _directAudioPlaybackService = new DirectAudioPlaybackService();

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
        /// - vision source id を安定化するためにも使う
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
                var serverIdentity = await _otomeKairoApiClient
                    .GetServerIdentityAsync()
                    .ConfigureAwait(false);
                if (!string.Equals(
                        serverIdentity.ApiVersion,
                        RequiredOtomeKairoApiVersion,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"OtomeKairo API {RequiredOtomeKairoApiVersion} が必要です。接続先は {serverIdentity.ApiVersion} です。");
                }

                // --- 既存トークンがあれば、まず有効性を確認する ---
                var bearerToken = (_appSettings.OtomeKairoBearerToken ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    try
                    {
                        await _otomeKairoApiClient.GetOtomeKairoStatusAsync().ConfigureAwait(false);
                        return;
                    }
                    catch (OtomeKairoApiException ex) when (ex.ErrorCode == "invalid_token")
                    {
                        throw new InvalidOperationException(
                            "保存済みのconsole_access_tokenが接続先のOtomeKairoと一致しません。システム設定の「OtomeKairo接続」で有効なトークンを設定してください。",
                            ex);
                    }
                    catch (OtomeKairoApiException ex) when (ex.ErrorCode == "bootstrap_required")
                    {
                        // --- 接続先が未登録なら、保存値にかかわらず初回登録へ進む ---
                    }
                }

                // --- 未発行状態のときだけ最初のトークンを自動取得する ---
                var probe = await _otomeKairoApiClient.ProbeBootstrapAsync().ConfigureAwait(false);
                if (!string.Equals(probe.BootstrapState, "unregistered", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "接続先のOtomeKairoは登録済みですが、console_access_tokenが未設定です。システム設定の「OtomeKairo接続」で発行済みのトークンを設定してください。");
                }

                var registered = await _otomeKairoApiClient.RegisterFirstConsoleAsync().ConfigureAwait(false);
                StoreOtomeKairoBearerToken(registered.ConsoleAccessToken);
            }
            finally
            {
                _otomeKairoBootstrapSemaphore.Release();
            }
        }

        private void StoreOtomeKairoBearerToken(string bearerToken)
        {
            _appSettings.OtomeKairoBearerToken = bearerToken;
            _appSettings.SaveAppSettings();
            // --- 保存イベントでAPIクライアントが再生成された場合も新トークンを反映する ---
            _otomeKairoApiClient?.SetBearerToken(bearerToken);
        }

        private void SetConversationInputBusy(bool isBusy)
        {
            // --- 状態の更新は 0/1 で統一し、変化があるときだけイベントを発火する ---
            int next = isBusy ? 1 : 0;
            int prev = Interlocked.Exchange(ref _conversationInputBusy, next);
            if (prev == next)
            {
                return;
            }

            // --- イベントは呼び出しスレッドで発火（UI側で Dispatcher に載せ替える） ---
            try
            {
                ConversationInputBusyChanged?.Invoke(this, isBusy);
            }
            catch (Exception ex)
            {
                // NOTE: UI 側の例外で通信層が停止しないようにする
                Debug.WriteLine($"[CommunicationService] ConversationInputBusyChanged handler error: {ex.Message}");
            }
        }


        /// <summary>
        /// APIサーバーを開始
        /// </summary>
        public async Task StartServerAsync()
        {
            _apiServerStartRequested = true;
            if (!_remoteSettingsApplied)
            {
                return;
            }

            await EnsureLocalApiServerReadyAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// CocoroShell を起動する前に、最新設定のポートでローカルAPIを待受状態にする。
        /// </summary>
        public async Task PrepareShellRuntimeAsync()
        {
            if (!_remoteSettingsApplied)
            {
                throw new InvalidOperationException("OtomeKairoの端末設定を取得していません。");
            }

            // Unity Editorも同じ認証境界を使えるよう、API待受前に一時トークンを用意する。
            CocoroShellProcessManager.PrepareSessionToken(_appSettings, false);
            await EnsureLocalApiServerReadyAsync().ConfigureAwait(false);
        }

        private async Task EnsureLocalApiServerReadyAsync()
        {
            await _apiServerLifecycleSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_apiServerPort != _appSettings.CocoroConsolePort)
                {
                    if (_apiServer.IsRunning)
                    {
                        await _apiServer.StopAsync().ConfigureAwait(false);
                    }
                    _apiServer.Dispose();
                    _apiServer = CreateApiServer(_appSettings.CocoroConsolePort);
                    _apiServerPort = _appSettings.CocoroConsolePort;
                }

                if (!_apiServer.IsRunning)
                {
                    await _apiServer.StartAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CommunicationService: サーバー起動エラー: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"サーバー起動に失敗しました: {ex.Message}");
                throw;
            }
            finally
            {
                _apiServerLifecycleSemaphore.Release();
            }
        }

        /// <summary>
        /// APIサーバーを停止
        /// </summary>
        public async Task StopServerAsync()
        {
            _apiServerStartRequested = false;
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
            var server = new CocoroConsoleApiServer(
                port,
                _appSettings,
                SaveConsoleClientSettingsAsync);
            server.UiMessageReceived += (sender, request) => UiMessageReceived?.Invoke(this, request);
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

            bool shellPortChanged = previousSettings.cocoroShellPort != currentSettings.cocoroShellPort;
            bool otomeKairoPortChanged = previousSettings.otomeKairoPort != currentSettings.otomeKairoPort;
            bool otomeKairoHostChanged = !string.Equals(
                previousSettings.otomeKairoHost ?? string.Empty,
                currentSettings.otomeKairoHost ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            bool useExternalOtomeKairoChanged =
                (previousSettings.useExternalOtomeKairo ?? false) !=
                (currentSettings.useExternalOtomeKairo ?? false);
            bool otomeKairoEndpointChanged = otomeKairoPortChanged || otomeKairoHostChanged || useExternalOtomeKairoChanged;
            bool bearerTokenChanged = !string.Equals(previousSettings.otomeKairoBearerToken ?? string.Empty,
                currentSettings.otomeKairoBearerToken ?? string.Empty, StringComparison.Ordinal);

            if (shellPortChanged)
            {
                _shellClient?.Dispose();
                _shellClient = new CocoroShellClient(currentSettings.cocoroShellPort);
            }

            if (otomeKairoEndpointChanged)
            {
                ResetStatusPollingService();
            }

            if (otomeKairoEndpointChanged || bearerTokenChanged)
            {
                UpdateOtomeKairoApiClient(currentSettings);
                _initialSettingsFetched = false;

                _ = StopEventsStreamAsync();
                _ = StopLogStreamAsync();
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

            // Normal 復帰時は、初回または event stream 未接続なら source 登録まで再初期化する。
            if (status == OtomeKairoStatus.Normal && (!_initialSettingsFetched || !IsEventsStreamConnected()))
            {
                _initialSettingsFetched = true;
                _ = FetchAndApplyCurrentSettingsFromOtomeKairoAsync();
            }

            if (status == OtomeKairoStatus.WaitingForStartup)
            {
                // 起動待ちに戻った場合は旧イベントストリームを止め、Normal 復帰時に再登録する。
                _initialSettingsFetched = false;
                _ = StopEventsStreamAsync();
            }
        }

        private bool IsEventsStreamConnected()
        {
            return _eventsStreamClient?.IsConnected == true;
        }

        /// <summary>
        /// otomekairo から現在設定を取得してイベントに反映する。
        /// </summary>
        private async Task FetchAndApplyCurrentSettingsFromOtomeKairoAsync()
        {
            if (_otomeKairoApiClient == null)
            {
                Debug.WriteLine("[CommunicationService] APIクライアントが初期化されていないため、現在設定の取得をスキップ");
                return;
            }

            try
            {
                Debug.WriteLine("[CommunicationService] OtomeKairo への接続初期化を開始します");
                await EnsureOtomeKairoReadyAsync().ConfigureAwait(false);
                Debug.WriteLine("[CommunicationService] OtomeKairo への接続初期化を完了しました");

                var config = await _otomeKairoApiClient
                    .GetOtomeKairoConfigAsync()
                    .ConfigureAwait(false);
                var consoleClient = await _otomeKairoApiClient
                    .ConnectConsoleClientAsync(_appSettings.ClientId)
                    .ConfigureAwait(false);
                var avatarSpeech = await _otomeKairoApiClient
                    .GetAvatarSpeechEditorStateAsync()
                    .ConfigureAwait(false);
                _appSettings.ApplyRemoteSettings(
                    consoleClient.Settings,
                    config.SettingsSnapshot,
                    avatarSpeech);
                _remoteSettingsApplied = true;
                if (_apiServerStartRequested)
                {
                    await PrepareShellRuntimeAsync().ConfigureAwait(false);
                }
                RefreshSettingsCache();
                _appSettings.SaveAppSettings();
                await StartEventsStreamAsync().ConfigureAwait(false);
                await SyncDesktopWatchCapabilityStateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommunicationService] OtomeKairo 現在設定の取得に失敗: {ex.Message}");
            }
        }

        public Task RefreshOtomeKairoCurrentSettingsAsync()
        {
            return FetchAndApplyCurrentSettingsFromOtomeKairoAsync();
        }

        /// <summary>
        /// 新しい会話セッションを開始
        /// </summary>
        public void StartNewConversation()
        {
            Debug.WriteLine("新しい会話を開始しました");
        }

        /// <summary>
        /// OtomeKairoに対話入力を送信（HTTP/SSE）
        /// </summary>
        /// <param name="message">送信メッセージ</param>
        /// <param name="avatarName">アバター名（オプション）</param>
        /// <param name="imageDataUrl">画像データURL（オプション）</param>
        public async Task SendConversationInputToOtomeKairoAsync(
            string message,
            string? avatarName = null,
            string? imageDataUrl = null,
            string? speakerId = null,
            string? speakerDisplayName = null)
        {
            // 単一画像を配列に変換して複数画像対応版を呼び出し
            var imageDataUrls = imageDataUrl != null ? new List<string> { imageDataUrl } : null;
            await SendConversationInputToOtomeKairoAsync(
                message,
                avatarName,
                imageDataUrls,
                speakerId,
                speakerDisplayName);
        }

        /// <summary>
        /// OtomeKairoへ対話入力を送信（複数画像対応）
        /// </summary>
        /// <param name="message">送信メッセージ</param>
        /// <param name="avatarName">アバター名（オプション）</param>
        /// <param name="imageDataUrls">画像データURLリスト（オプション）</param>
        public async Task SendConversationInputToOtomeKairoAsync(
            string message,
            string? avatarName = null,
            List<string>? imageDataUrls = null,
            string? speakerId = null,
            string? speakerDisplayName = null)
        {
            if (_otomeKairoApiClient == null)
            {
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, "OtomeKairo APIクライアントを初期化できませんでした"));
                return;
            }

            await SendConversationInputViaHttpAsync(
                message,
                imageDataUrls,
                speakerId,
                speakerDisplayName);
        }

        private async Task SendConversationInputViaHttpAsync(
            string message,
            List<string>? imageDataUrls,
            string? speakerId,
            string? speakerDisplayName)
        {
            // --- 同時送信を直列化する ---
            await _conversationInputSendSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                // --- 送信中状態をセットする ---
                SetConversationInputBusy(true);

                // --- LLMが無効の場合は処理しない ---
                if (!_appSettings.IsUseLLM)
                {
                    Debug.WriteLine("対話入力送信: LLMが無効のためスキップ");
                    return;
                }

                // --- client_id は bootstrap 済みトークンと合わせて管理する ---
                EnsureClientIdInitialized();

                // --- 空入力は送らない ---
                if (string.IsNullOrWhiteSpace(message))
                {
                    var errorMessage = "メッセージを入力してください";
                    ConversationOutputReceived?.Invoke(this, new ConversationOutputEventArgs
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
                    ConversationOutputReceived?.Invoke(this, new ConversationOutputEventArgs
                    {
                        Content = string.Empty,
                        IsFinished = true,
                        IsError = true,
                        ErrorMessage = errorMessage
                    });
                    StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, errorMessage));
                    return;
                }

                // --- 能力実行要求を配送できるよう、会話送信前に event stream を接続する ---
                await StartEventsStreamAsync().ConfigureAwait(false);

                // --- 送信中ステータスを反映する ---
                _statusPollingService.SetProcessingStatus(OtomeKairoStatus.ProcessingConversationInput);
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "対話入力送信開始"));

                var normalizedImages = imageDataUrls?
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Select(url => url.Trim())
                    .Take(1)
                    .ToList();
                if (normalizedImages != null && normalizedImages.Count == 0)
                {
                    normalizedImages = null;
                }

                // --- Consoleが確定した人物と会話の参照をAPIへ渡す ---
                var interactionContext = BuildConversationInteractionContext(
                    speakerId,
                    speakerDisplayName);

                // --- OtomeKairo の会話入力 API を呼ぶ ---
                var request = new OtomeKairoConversationRequest
                {
                    Text = message,
                    Images = normalizedImages,
                    InteractionContext = interactionContext,
                    ClientContext = BuildOtomeKairoClientContext()
                };
                var response = await _otomeKairoApiClient
                    .SendConversationAsync(request)
                    .ConfigureAwait(false);
                ValidateConversationResponseRouting(response, interactionContext);

                // --- 発話種別ごとに UI へ反映する ---
                if (string.Equals(response.ResultKind, "speech", StringComparison.Ordinal))
                {
                    var speechText = response.Speech?.Text ?? string.Empty;
                    ConversationOutputReceived?.Invoke(this, new ConversationOutputEventArgs
                    {
                        Content = speechText,
                        IsFinished = true,
                        IsError = false
                    });

                    if (!string.IsNullOrWhiteSpace(speechText))
                    {
                        var uiMessage = new UiMessageRequest
                        {
                            memoryId = _cachedMemoryId,
                            sessionId = response.CycleId,
                            message = speechText,
                            role = "assistant",
                            content = speechText
                        };

                        UiMessageReceived?.Invoke(this, uiMessage);
                    }

                    _statusPollingService.SetNormalStatus();
                    StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "対話入力完了"));
                    return;
                }

                if (string.Equals(response.ResultKind, "noop", StringComparison.Ordinal))
                {
                    _statusPollingService.SetNormalStatus();
                    StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "今回は発話しませんでした"));
                    return;
                }

                if (string.Equals(response.ResultKind, "capability_request", StringComparison.Ordinal))
                {
                    _statusPollingService.SetNormalStatus();
                    var capabilityId = response.CapabilityRequest?.CapabilityId ?? "能力";
                    StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, $"{capabilityId} の実行要求を受信しました"));
                    return;
                }

                var internalFailureMessage = "OtomeKairo 内部処理に失敗しました";
                ConversationOutputReceived?.Invoke(this, new ConversationOutputEventArgs
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
                    "invalid_images" => "添付画像は Data URI 1枚まで送信できます。",
                    "invalid_person_display_name" => "呼ばれ方が不正です。設定の入力を確認してください。",
                    _ => ex.Message,
                };
                Debug.WriteLine($"OtomeKairo APIエラー: {errorMessage}");
                ConversationOutputReceived?.Invoke(this, new ConversationOutputEventArgs
                {
                    Content = string.Empty,
                    IsFinished = true,
                    IsError = true,
                    ErrorMessage = errorMessage
                });
                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"対話入力送信エラー: {errorMessage}"));
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine($"対話入力送信タイムアウト: {ex.Message}");
                ConversationOutputReceived?.Invoke(this, new ConversationOutputEventArgs
                {
                    Content = string.Empty,
                    IsFinished = true,
                    IsError = true,
                    ErrorMessage = $"対話入力送信タイムアウト: {ex.Message}"
                });
                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"対話入力送信タイムアウト: {ex.Message}"));
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"対話入力送信HTTPエラー: {ex.Message}");
                ConversationOutputReceived?.Invoke(this, new ConversationOutputEventArgs
                {
                    Content = string.Empty,
                    IsFinished = true,
                    IsError = true,
                    ErrorMessage = $"対話入力送信HTTPエラー: {ex.Message}"
                });
                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"対話入力送信HTTPエラー: {ex.Message}"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"対話入力送信エラー: {ex.Message}");
                ConversationOutputReceived?.Invoke(this, new ConversationOutputEventArgs
                {
                    Content = string.Empty,
                    IsFinished = true,
                    IsError = true,
                    ErrorMessage = $"対話入力送信エラー: {ex.Message}"
                });
                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"対話入力送信エラー: {ex.Message}"));
            }
            finally
            {
                // --- 送信状態を解除して次の送信を許可する ---
                try
                {
                    SetConversationInputBusy(false);
                }
                finally
                {
                    _conversationInputSendSemaphore.Release();
                }
            }
        }

        private OtomeKairoInteractionContext BuildConversationInteractionContext(
            string? speakerId,
            string? speakerDisplayName)
        {
            var hasSpeakerId = !string.IsNullOrWhiteSpace(speakerId);
            var hasSpeakerDisplayName = !string.IsNullOrWhiteSpace(speakerDisplayName);
            if (hasSpeakerId != hasSpeakerDisplayName)
            {
                throw new InvalidOperationException(
                    "話者IDと話者名は同時に指定してください。");
            }

            var personRef = hasSpeakerId
                ? $"person:console:speaker:{speakerId!.Trim()}"
                : $"person:console:{_appSettings.ClientId.Trim()}";
            if (!personRef.StartsWith("person:", StringComparison.Ordinal) ||
                personRef.Length == "person:".Length)
            {
                throw new InvalidOperationException("会話入力の person_ref が不正です。");
            }

            var displayName = hasSpeakerDisplayName
                ? speakerDisplayName!.Trim()
                : _appSettings.ConversationDisplayName.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new InvalidOperationException(
                    "呼ばれ方が未設定です。設定の入力から「会話入力」を開いて設定してください。");
            }

            var normalizedParticipant = new OtomeKairoInteractionParticipant
            {
                PersonRef = personRef,
                DisplayName = displayName,
            };
            var personKey = personRef.Substring("person:".Length);
            return new OtomeKairoInteractionContext
            {
                InteractionRef = $"interaction:console:direct:{personKey}",
                SpeakerRef = personRef,
                Participants = new List<OtomeKairoInteractionParticipant> { normalizedParticipant },
            };
        }

        private static void ValidateConversationResponseRouting(
            OtomeKairoConversationResponse response,
            OtomeKairoInteractionContext requestContext)
        {
            if (!string.Equals(
                    response.InteractionRef,
                    requestContext.InteractionRef,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("会話応答の interaction_ref がリクエストと一致しません。");
            }

            var expectedRecipients = requestContext.Participants
                .Select(participant => participant.PersonRef)
                .ToList();
            if (response.RecipientPersonRefs.Count != expectedRecipients.Count ||
                !response.RecipientPersonRefs.SequenceEqual(expectedRecipients, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("会話応答の recipient_person_refs がリクエストと一致しません。");
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

        public async Task SetDesktopWatchEnabledAsync(bool enabled)
        {
            if (_otomeKairoApiClient == null)
            {
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, "OtomeKairo APIクライアントを初期化できませんでした"));
                return;
            }

            var previousEnabled = _appSettings.ScreenshotSettings.enabled;
            try
            {
                _appSettings.ScreenshotSettings.enabled = enabled;
                await EnsureOtomeKairoReadyAsync().ConfigureAwait(false);
                await StartEventsStreamAsync().ConfigureAwait(false);
                var configResponse = await _otomeKairoApiClient
                    .GetOtomeKairoConfigAsync()
                    .ConfigureAwait(false);
                var currentWakePolicy = configResponse.SettingsSnapshot.WakePolicy ?? new Dictionary<string, object?>();
                var wakePolicy = DesktopWakePolicyHelper.BuildWakePolicyRequestFromCurrent(
                    currentWakePolicy,
                    _appSettings.ClientId,
                    enabled);
                await _otomeKairoApiClient
                    .PatchCurrentConfigAsync(new OtomeKairoCurrentSettingsPatch
                    {
                        WakePolicy = wakePolicy,
                    })
                    .ConfigureAwait(false);
                await _otomeKairoApiClient
                    .PatchCapabilityStateAsync("vision.capture", paused: !enabled)
                    .ConfigureAwait(false);
                await _otomeKairoApiClient
                    .ReplaceConsoleClientEditorStateAsync(
                        _appSettings.ClientId,
                        _appSettings.BuildConsoleClientSettings())
                    .ConfigureAwait(false);

                _appSettings.SaveAppSettings();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, enabled ? "デスクトップウォッチを有効にしました" : "デスクトップウォッチを無効にしました"));
            }
            catch (Exception ex)
            {
                _appSettings.ScreenshotSettings.enabled = previousEnabled;
                Debug.WriteLine($"[DesktopWatch] 切り替え失敗: {ex.Message}");
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"デスクトップウォッチ設定変更に失敗しました: {ex.Message}"));
            }
        }

        public async Task SaveAvatarSpeechSettingsAsync()
        {
            if (_otomeKairoApiClient == null)
            {
                throw new InvalidOperationException("OtomeKairo APIクライアントが初期化されていません。");
            }

            await EnsureOtomeKairoReadyAsync().ConfigureAwait(false);
            await _otomeKairoApiClient
                .ReplaceAvatarSpeechEditorStateAsync(_appSettings.BuildAvatarSpeechEditorState())
                .ConfigureAwait(false);
            _appSettings.SaveAppSettings();
        }

        public async Task SaveConsoleClientSettingsAsync(CancellationToken cancellationToken = default)
        {
            if (_otomeKairoApiClient == null)
            {
                throw new InvalidOperationException("OtomeKairo APIクライアントが初期化されていません。");
            }

            await EnsureOtomeKairoReadyAsync().ConfigureAwait(false);
            await _otomeKairoApiClient
                .ReplaceConsoleClientEditorStateAsync(
                    _appSettings.ClientId,
                    _appSettings.BuildConsoleClientSettings(),
                    cancellationToken)
                .ConfigureAwait(false);
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
        /// 保存済みの現在のアバター設定を取得（キャッシュ使用）
        /// </summary>
        private AvatarSettings? GetStoredAvatarSetting()
        {
            // キャッシュされた設定を使用
            var config = _cachedConfigSettings;
            if (config?.avatarList != null &&
                config.currentAvatarIndex >= 0 &&
                config.currentAvatarIndex < config.avatarList.Count)
            {
                return config.avatarList[config.currentAvatarIndex];
            }
            return null;
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

        private async Task StartEventsStreamAsync()
        {
            if (_eventsStreamClient != null)
            {
                await WaitForEventsStreamConnectionAsync(_eventsStreamClient).ConfigureAwait(false);
                return;
            }

            var bearerToken = _appSettings.OtomeKairoBearerToken;
            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                return;
            }

            // event stream の hello を確実に送るため、ClientId を用意しておく
            EnsureClientIdInitialized();

            var eventsStreamUri = new Uri($"{_appSettings.GetOtomeKairoWebSocketBaseUrl()}/api/events/stream");
            _eventsStreamClient = new EventsStreamClient(
                eventsStreamUri,
                bearerToken,
                _appSettings.ClientId,
                new[] { new OtomeKairoCapabilityOffer("vision.capture", "1") },
                BuildVisionSources()
            );
            _eventsStreamClient.EventReceived += OnEventsStreamEventReceived;
            _eventsStreamClient.AssistantAudioReceived += OnAssistantAudioReceived;
            _eventsStreamClient.ConnectionStateChanged += OnEventsStreamConnectionStateChanged;
            _eventsStreamClient.ErrorOccurred += OnEventsStreamErrorOccurred;

            try
            {
                await _eventsStreamClient.StartAsync();
                await WaitForEventsStreamConnectionAsync(_eventsStreamClient).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"イベントストリーム接続に失敗しました: {ex.Message}");
                await StopEventsStreamAsync();
            }
        }

        private static async Task WaitForEventsStreamConnectionAsync(EventsStreamClient client)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (!client.IsConnected && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50).ConfigureAwait(false);
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
                _eventsStreamClient.AssistantAudioReceived -= OnAssistantAudioReceived;
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
                if (string.Equals(ev.Type, "assistant_message", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(ev.Data.InteractionRef) ||
                        ev.Data.RecipientPersonRefs == null ||
                        ev.Data.RecipientPersonRefs.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "assistant_message に interaction_ref または recipient_person_refs がありません。");
                    }

                    var assistantSpeech = ev.Data.Message;
                    if (!string.IsNullOrWhiteSpace(assistantSpeech))
                    {
                        HandleAssistantSpeechFromEvent(ev, assistantSpeech);
                    }
                    return;
                }

                if (string.Equals(ev.Type, "assistant_audio", StringComparison.Ordinal))
                {
                    if (string.Equals(ev.Data.Status, "failed", StringComparison.Ordinal))
                    {
                        var errorCode = ev.Data.ErrorCode ?? "tts_request_failed";
                        StatusUpdateRequested?.Invoke(
                            this,
                            new StatusUpdateEventArgs(false, $"音声合成エラー: {errorCode}"));
                    }
                    return;
                }

                if (string.Equals(ev.Type, "conversation_input", StringComparison.Ordinal))
                {
                    if (ev.Data.UtteranceSeq == null ||
                        string.IsNullOrWhiteSpace(ev.Data.SourceKind) ||
                        string.IsNullOrWhiteSpace(ev.Data.Message) ||
                        string.IsNullOrWhiteSpace(ev.Data.InteractionRef) ||
                        string.IsNullOrWhiteSpace(ev.Data.SpeakerRef) ||
                        ev.Data.ParticipantRefs == null ||
                        ev.Data.ParticipantRefs.Count == 0 ||
                        string.IsNullOrWhiteSpace(ev.Data.DisplayName))
                    {
                        throw new InvalidOperationException(
                            "conversation_input に必須フィールドがありません。");
                    }

                    VoiceConversationInputReceived?.Invoke(
                        this,
                        new VoiceConversationInputEventArgs
                        {
                            UtteranceSeq = ev.Data.UtteranceSeq.Value,
                            SourceKind = ev.Data.SourceKind,
                            Message = ev.Data.Message,
                            InteractionRef = ev.Data.InteractionRef,
                            SpeakerRef = ev.Data.SpeakerRef,
                            ParticipantRefs = ev.Data.ParticipantRefs,
                            DisplayName = ev.Data.DisplayName,
                        });
                    return;
                }

                if (string.Equals(ev.Type, "audio_runtime_state", StringComparison.Ordinal))
                {
                    if (ev.Data.AudioRuntimeState == null)
                    {
                        throw new InvalidOperationException(
                            "audio_runtime_state の snapshot がありません。");
                    }

                    AudioRuntimeStateChanged?.Invoke(this, ev.Data.AudioRuntimeState);
                    return;
                }

                if (string.Equals(ev.Type, "vision.capture_request", StringComparison.OrdinalIgnoreCase))
                {
                    _ = Task.Run(() => HandleVisionCaptureRequestAsync(ev));
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"イベント処理エラー: {ex.Message}");
            }
        }

        private void HandleAssistantSpeechFromEvent(OtomeKairoEvent ev, string assistantSpeech)
        {
            var sourceKind = ev.Data.SourceKind ?? string.Empty;
            var uiMessage = new UiMessageRequest
            {
                memoryId = string.Empty,
                sessionId = ev.Data.InteractionRef!,
                message = assistantSpeech,
                role = "assistant",
                content = assistantSpeech,
                sourceKind = sourceKind,
                // spontaneous assistant_message は通常会話と分離して別バブルに出す。
                forceNewBubble = ShouldForceNewBubbleForAssistantEvent(sourceKind)
            };

            UiMessageReceived?.Invoke(this, uiMessage);
        }

        private void OnAssistantAudioReceived(
            object? sender,
            OtomeKairoAssistantAudioReceivedEventArgs args)
        {
            _ = HandleAssistantAudioAsync(args);
        }

        private async Task HandleAssistantAudioAsync(
            OtomeKairoAssistantAudioReceivedEventArgs args)
        {
            await _forwardMessageSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                // 通常版はprocess存在を正とし、Unity Editor実行時はloopback APIで判定する。
                var shellProcessRunning = IsCocoroShellProcessRunning();
                if (!shellProcessRunning &&
                    !await _shellClient.IsRunningAsync().ConfigureAwait(false))
                {
                    await _directAudioPlaybackService
                        .PlayAsync(args.AudioBytes)
                        .ConfigureAwait(false);
                    return;
                }

                // 起動確認後の配送失敗は再生先を切り替えず、接続異常として公開する。
                await _shellClient
                    .SendAudioAsync(args.AudioBytes, _appSettings.ShellSessionToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"音声配送エラー: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"音声配送エラー: {ex.Message}");
                StatusUpdateRequested?.Invoke(
                    this,
                    new StatusUpdateEventArgs(false, "音声配送に失敗しました"));
            }
            finally
            {
                _forwardMessageSemaphore.Release();
            }
        }

        private static bool ShouldForceNewBubbleForAssistantEvent(string? sourceKind)
        {
            if (string.IsNullOrWhiteSpace(sourceKind))
            {
                return false;
            }

            return string.Equals(sourceKind, "wake", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sourceKind, "background_thinking", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sourceKind, "capability_result", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sourceKind, "autonomous_run", StringComparison.OrdinalIgnoreCase);
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

            var expectedSource = BuildPrimaryVisionSource();
            var response = new OtomeKairoCapabilityResultRequest
            {
                RequestId = requestId!,
                ClientId = _appSettings.ClientId,
                CapabilityId = string.IsNullOrWhiteSpace(ev.Data.CapabilityId) ? "vision.capture" : ev.Data.CapabilityId.Trim(),
                Result = new VisionCaptureCapabilityResult
                {
                    ClientContext = BuildVisionCaptureClientContext(ev.Data, expectedSource),
                    Images = new List<string>(),
                    Error = null,
                },
            };

            try
            {
                if (!string.Equals(response.CapabilityId, "vision.capture", StringComparison.Ordinal))
                {
                    response.Result.Error = $"unsupported capability: {response.CapabilityId}";
                }
                else if (!string.Equals(ev.Data.Mode, "still", StringComparison.OrdinalIgnoreCase))
                {
                    response.Result.Error = $"unsupported mode: {ev.Data.Mode}";
                }
                else if (!string.Equals(ev.Data.SourceKind, expectedSource.Kind, StringComparison.OrdinalIgnoreCase))
                {
                    response.Result.Error = $"unsupported source_kind: {ev.Data.SourceKind}";
                }
                else if (!string.Equals(ev.Data.VisionSourceId, expectedSource.VisionSourceId, StringComparison.Ordinal))
                {
                    response.Result.Error = $"unsupported vision_source_id: {ev.Data.VisionSourceId}";
                }
                else
                {
                    _statusPollingService.SetProcessingStatus(OtomeKairoStatus.ProcessingImage);
                    var (dataUri, windowTitle, captureError) = await CaptureDesktopStillAsync(cts.Token);

                    if (!string.IsNullOrWhiteSpace(windowTitle))
                    {
                        response.Result.ClientContext.WindowTitle = windowTitle;
                    }

                    if (!string.IsNullOrWhiteSpace(dataUri))
                    {
                        response.Result.Images.Add(dataUri);
                    }
                    else
                    {
                        response.Result.Error = captureError ?? "capture skipped";
                    }
                }
            }
            catch (Exception ex)
            {
                response.Result.Error = ex.Message;
            }
            finally
            {
                _statusPollingService.SetNormalStatus();
            }

            try
            {
                await _otomeKairoApiClient.SendCapabilityResultAsync(response, cts.Token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Vision] capability-result送信失敗: {ex.Message}");
            }
        }

        private async Task<(string? DataUri, string? WindowTitle, string? Error)> CaptureDesktopStillAsync(CancellationToken cancellationToken)
        {
            using var service = new ScreenshotService();
            service.CaptureActiveWindowOnly = _appSettings.ScreenshotSettings.captureActiveWindowOnly;

            // 視覚キャプチャのアイドルタイムアウト（分）を反映（0 は無効）
            service.IdleTimeoutMinutes = _appSettings.ScreenshotSettings.idleTimeoutMinutes;

            // スクショ除外（ウィンドウタイトル正規表現）を反映
            service.SetExcludePatterns(_appSettings.ScreenshotSettings.excludePatterns);
            ScreenshotData? screenshot = service.CaptureActiveWindowOnly
                ? await service.CaptureActiveWindowAsync().WaitAsync(cancellationToken)
                : await service.CaptureFullScreenAsync().WaitAsync(cancellationToken);
            if (screenshot == null)
            {
                return service.LastSkipReason switch
                {
                    ScreenshotSkipReason.Idle => (null, null, "capture skipped (idle)"),
                    ScreenshotSkipReason.ExcludedWindowTitle => (null, null, "capture skipped (excluded window title)"),
                    ScreenshotSkipReason.InvalidWindowBounds => (null, null, "capture skipped (invalid window bounds)"),
                    _ => (null, null, "capture skipped")
                };
            }

            var dataUri = $"data:image/png;base64,{screenshot.ImageBase64}";
            return (dataUri, screenshot.WindowTitle, null);
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

        private async Task SyncDesktopWatchCapabilityStateAsync()
        {
            if (_otomeKairoApiClient == null)
            {
                return;
            }

            try
            {
                await _otomeKairoApiClient
                    .PatchCapabilityStateAsync("vision.capture", paused: !_appSettings.ScreenshotSettings.enabled)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopWatch] 初期同期失敗: {ex.Message}");
            }
        }

        private VisionClientContext BuildVisionCaptureClientContext(
            OtomeKairoEventData eventData,
            OtomeKairoVisionSourceOffer sourceOffer)
        {
            var snapshot = GetClientContextSnapshot();
            snapshot.VisionSourceId = string.IsNullOrWhiteSpace(eventData.VisionSourceId)
                ? sourceOffer.VisionSourceId
                : eventData.VisionSourceId.Trim();
            snapshot.SourceKind = string.IsNullOrWhiteSpace(eventData.SourceKind)
                ? sourceOffer.Kind
                : eventData.SourceKind.Trim();
            snapshot.SourceLabel = string.IsNullOrWhiteSpace(eventData.SourceLabel)
                ? sourceOffer.Label
                : eventData.SourceLabel.Trim();
            return snapshot;
        }

        private IReadOnlyList<OtomeKairoVisionSourceOffer> BuildVisionSources()
        {
            var useActiveWindow = _appSettings.ScreenshotSettings.captureActiveWindowOnly;
            var label = useActiveWindow ? "アクティブウィンドウ" : "メイン画面";
            var aliases = useActiveWindow
                ? new[] { "画面", "デスクトップ", "アクティブウィンドウ", "前景ウィンドウ" }
                : new[] { "画面", "デスクトップ", "メイン画面", "フルスクリーン" };
            return new[]
            {
                new OtomeKairoVisionSourceOffer(
                    DesktopWakePolicyHelper.BuildDesktopVisionSourceId(_appSettings.ClientId),
                    "vision.capture",
                    "desktop",
                    label,
                    aliases,
                    new[] { "visual", "desktop" },
                    new[] { "observe_desktop" })
            };
        }

        private OtomeKairoVisionSourceOffer BuildPrimaryVisionSource()
        {
            return BuildVisionSources().First();
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


        private void OnEventsStreamErrorOccurred(object? sender, string errorMessage)
        {
            Debug.WriteLine($"イベントストリームエラー: {errorMessage}");
            StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"イベントストリームエラー: {errorMessage}"));
        }


        /// <summary>
        /// CocoroShellから現在のアバター位置を取得
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
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            // イベント購読解除
            AppSettings.SettingsSaved -= OnSettingsSaved;

            // セマフォの解放
            _forwardMessageSemaphore?.Dispose();

            if (_statusPollingService != null)
            {
                _statusPollingService.StatusChanged -= OnStatusPollingServiceStatusChanged;
                _statusPollingService.Dispose();
            }
            _apiServer?.Dispose();
            _shellClient?.Dispose();
            _directAudioPlaybackService?.Dispose();
            _otomeKairoApiClient?.Dispose();
            _logStreamClient?.Dispose();
            _eventsStreamClient?.Dispose();
            _otomeKairoBootstrapSemaphore?.Dispose();
            _apiServerLifecycleSemaphore.Dispose();
            _conversationInputSendSemaphore?.Dispose();
        }
    }
}
