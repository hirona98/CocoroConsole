using CocoroConsole.Communication;
using CocoroConsole.Models.CocoroGhostApi;
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
    /// CocoroAI（CocoroConsole / CocoroGhost / CocoroShell）間の通信を集約するサービス。
    /// 
    /// 主な責務:
    /// - CocoroConsole API サーバーの起動/停止（外部からの chat/control/status 更新を受ける）
    /// - CocoroShell への送信（発話/表示の連携）
    /// - CocoroGhost の状態ポーリングと、状態変化イベントの転送
    /// - cocoro_ghost HTTP API（Bearer 認証）を用いた設定取得やチャット SSE ストリーミング
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

        // cocoro_ghost の状態を定期取得して UI に通知する
        private StatusPollingService _statusPollingService;

        // cocoro_ghost HTTP API（Bearer トークン必須）。トークン未設定時は null。
        private CocoroGhostApiClient? _cocoroGhostApiClient;

        // cocoro_ghost のログ/イベントを購読するストリームクライアント（必要時に開始/停止）
        private LogStreamClient? _logStreamClient;
        private EventsStreamClient? _eventsStreamClient;

        // シェルへの送信順序を保証するためのセマフォ（同時送信を直列化）
        private readonly SemaphoreSlim _forwardMessageSemaphore = new SemaphoreSlim(1, 1);

        // チャット API に渡すセッション ID（会話のまとまり）。新規会話で null に戻す。
        private string? _currentSessionId;

        // 設定キャッシュ（SettingsSaved で更新し、差分に応じてランタイム反映）
        private ConfigSettings? _cachedConfigSettings;

        // memory_id キャッシュ（チャット返信を UI へ戻す際に付与）
        private string _cachedMemoryId = "memory";

        // cocoro_ghost /api/settings キャッシュ（embedding_preset_id 解決などに利用）
        private CocoroConsole.Models.CocoroGhostApi.CocoroGhostSettings? _cachedCocoroGhostSettings;

        // 起動後、cocoro_ghost が Normal になったタイミングで一度だけ設定取得するためのフラグ
        private bool _initialSettingsFetched = false;

        // desktop_watch 用: 直近のデスクトップキャプチャ（vision.capture_request）を一時キャッシュして
        // desktop_watch イベントの通知表示に画像を添付する。
        private readonly object _desktopWatchCaptureLock = new object();
        private (DateTime TimestampUtc, List<BitmapSource> Images, string? WindowTitle)? _lastDesktopWatchCapture;
        private static readonly TimeSpan DesktopWatchCaptureMaxAge = TimeSpan.FromSeconds(30);

        public event EventHandler<ChatRequest>? ChatMessageReceived;
        public event EventHandler<StreamingChatEventArgs>? StreamingChatReceived;
        public event Action<ChatMessagePayload, List<System.Windows.Media.Imaging.BitmapSource>?>? NotificationMessageReceived;
        public event EventHandler<ControlRequest>? ControlCommandReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<StatusUpdateEventArgs>? StatusUpdateRequested;
        public event EventHandler<CocoroGhostStatus>? StatusChanged;
        public event EventHandler<CocoroConsole.Models.CocoroGhostApi.CocoroGhostSettings>? CocoroGhostSettingsUpdated;
        public event EventHandler<IReadOnlyList<LogMessage>>? LogMessagesReceived;
        public event EventHandler<bool>? LogStreamConnectionChanged;
        public event EventHandler<string>? LogStreamError;

        public bool IsServerRunning => _apiServer.IsRunning;

        /// <summary>
        /// 現在のCocoroGhostステータス
        /// </summary>
        public CocoroGhostStatus CurrentStatus => _statusPollingService.CurrentStatus;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="appSettings">アプリケーション設定</param>
        public CommunicationService(IAppSettings appSettings)
        {
            _appSettings = appSettings;

            // APIサーバーの初期化
            _apiServer = CreateApiServer(_appSettings.CocoroConsolePort);

            // CocoroShellクライアントの初期化
            _shellClient = new CocoroShellClient(_appSettings.CocoroShellPort);

            // CocoroGhost APIクライアントの初期化（Bearer Tokenが設定されている場合のみ）
            var bearerToken = _appSettings.CocoroGhostBearerToken;
            if (!string.IsNullOrEmpty(bearerToken))
            {
                var baseUrl = $"http://127.0.0.1:{_appSettings.CocoroGhostPort}";
                _cocoroGhostApiClient = new CocoroGhostApiClient(baseUrl, bearerToken);
            }

            // 設定キャッシュを初期化
            RefreshSettingsCache();

            // ステータスポーリングサービスの初期化
            _statusPollingService = new StatusPollingService($"http://127.0.0.1:{_appSettings.CocoroGhostPort}");
            _statusPollingService.StatusChanged += OnStatusPollingServiceStatusChanged;

            // AppSettingsの変更イベントを購読
            AppSettings.SettingsSaved += OnSettingsSaved;
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
            bool bearerTokenChanged = !string.Equals(previousSettings.cocoroGhostBearerToken ?? string.Empty,
                currentSettings.cocoroGhostBearerToken ?? string.Empty, StringComparison.Ordinal);

            if (consolePortChanged)
            {
                _ = RestartApiServerAsync(currentSettings.CocoroConsolePort);
            }

            if (shellPortChanged)
            {
                _shellClient?.Dispose();
                _shellClient = new CocoroShellClient(currentSettings.cocoroShellPort);
            }

            if (ghostPortChanged)
            {
                ResetStatusPollingService(currentSettings.cocoroCorePort);
            }

            if (ghostPortChanged || bearerTokenChanged)
            {
                UpdateCocoroGhostApiClient(currentSettings);
                _cachedCocoroGhostSettings = null;
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

        private void ResetStatusPollingService(int cocoroGhostPort)
        {
            _statusPollingService.StatusChanged -= OnStatusPollingServiceStatusChanged;
            _statusPollingService.Dispose();

            _statusPollingService = new StatusPollingService($"http://127.0.0.1:{cocoroGhostPort}");
            _statusPollingService.StatusChanged += OnStatusPollingServiceStatusChanged;
        }

        private void UpdateCocoroGhostApiClient(ConfigSettings settings)
        {
            // 変更（ポート/トークン）に追従するため、既存クライアントは破棄して作り直す
            _cocoroGhostApiClient?.Dispose();
            _cocoroGhostApiClient = null;

            var bearerToken = settings.cocoroGhostBearerToken ?? string.Empty;
            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                // トークン未設定の場合、API 呼び出しは行わない
                return;
            }

            var baseUrl = $"http://127.0.0.1:{settings.cocoroCorePort}";
            _cocoroGhostApiClient = new CocoroGhostApiClient(baseUrl, bearerToken);
        }


        /// <summary>
        /// StatusPollingServiceのステータス変更ハンドラ
        /// </summary>
        private void OnStatusPollingServiceStatusChanged(object? sender, CocoroGhostStatus status)
        {
            // 外部イベントに転送
            StatusChanged?.Invoke(this, status);

            // 初回Normal時にcocoro_ghostから設定を取得
            if (!_initialSettingsFetched && status == CocoroGhostStatus.Normal)
            {
                _initialSettingsFetched = true;
                _ = FetchAndApplySettingsFromCocoroGhostAsync();
            }

            if (status == CocoroGhostStatus.Normal)
            {
                // 正常状態ではイベントストリームを開始
                _ = StartEventsStreamAsync();
            }
            else if (status == CocoroGhostStatus.WaitingForStartup)
            {
                // 起動待ちに戻った場合はイベントストリームを停止
                _ = StopEventsStreamAsync();
            }
        }

        /// <summary>
        /// cocoro_ghostから設定を取得してAppSettingsに反映
        /// </summary>
        public async Task FetchAndApplySettingsFromCocoroGhostAsync()
        {
            if (_cocoroGhostApiClient == null)
            {
                Debug.WriteLine("[CommunicationService] CocoroGhostApiClientが初期化されていないため、設定取得をスキップ");
                return;
            }

            try
            {
                Debug.WriteLine("[CommunicationService] cocoro_ghostから設定を取得中...");
                var settings = await _cocoroGhostApiClient.GetSettingsAsync();

                // チャット送信時に参照するためキャッシュ
                _cachedCocoroGhostSettings = settings;

                // AppSettingsに反映
                ApplyCocoroGhostSettingsToAppSettings(settings);

                CocoroGhostSettingsUpdated?.Invoke(this, settings);

                Debug.WriteLine("[CommunicationService] cocoro_ghostから設定を取得・反映しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CommunicationService] cocoro_ghostから設定取得に失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// cocoro_ghostから取得した設定をAppSettingsに反映
        /// </summary>
        private void ApplyCocoroGhostSettingsToAppSettings(Models.CocoroGhostApi.CocoroGhostSettings settings)
        {
            // 設定キャッシュを更新
            RefreshSettingsCache();

            Debug.WriteLine("[CommunicationService] AppSettingsにcocoro_ghost設定を反映完了");
        }

        public Task RefreshCocoroGhostSettingsAsync()
        {
            return FetchAndApplySettingsFromCocoroGhostAsync();
        }

        /// <summary>
        /// 新しい会話セッションを開始
        /// </summary>
        public void StartNewConversation()
        {
            _currentSessionId = null;
            Debug.WriteLine("新しい会話セッションを開始しました");
        }

        /// <summary>
        /// CocoroGhostにチャットメッセージを送信（HTTP/SSE）
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
            if (_cocoroGhostApiClient == null)
            {
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, "cocoro_ghostのBearerトークンが設定されていません"));
                return;
            }

            await SendChatViaHttpStreamingAsync(message, imageDataUrls);
        }

        private async Task SendChatViaHttpStreamingAsync(string message, List<string>? imageDataUrls)
        {
            try
            {
                // LLMが無効の場合は処理しない
                if (!_appSettings.IsUseLLM)
                {
                    Debug.WriteLine("チャット送信: LLMが無効のためスキップ");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_appSettings.ClientId))
                {
                    _appSettings.ClientId = $"console-{Guid.NewGuid()}";
                    _appSettings.SaveAppSettings();
                }

                // セッションIDを生成または既存のものを使用
                if (string.IsNullOrEmpty(_currentSessionId))
                {
                    _currentSessionId = $"dock_{DateTime.Now:yyyyMMddHHmmssfff}";
                }

                // 画像データを変換（複数対応）
                // cocoro_ghost の /api/chat は images を Data URI 配列で受け取る。
                var images = new List<string>();
                if (imageDataUrls != null && imageDataUrls.Count > 0)
                {
                    foreach (var dataUrl in imageDataUrls)
                    {
                        var cleaned = (dataUrl ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(cleaned))
                        {
                            continue;
                        }

                        // --- Data URI をそのまま送る（例: data:image/png;base64,....） ---
                        images.Add(cleaned);
                    }
                }

                // 画像がある場合は画像処理中、そうでなければメッセージ処理中に設定
                var processingStatus = images.Count > 0
                    ? CocoroGhostStatus.ProcessingImage
                    : CocoroGhostStatus.ProcessingMessage;
                _statusPollingService.SetProcessingStatus(processingStatus);

                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "チャット送信開始"));

                var embeddingPresetId = await ResolveEmbeddingPresetIdForChatAsync();
                if (string.IsNullOrWhiteSpace(embeddingPresetId))
                {
                    _statusPollingService.SetNormalStatus();
                    StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, "embedding_preset_idの取得に失敗しました（/api/settings を確認してください）"));
                    return;
                }

                var chatRequest = new ChatStreamRequest
                {
                    EmbeddingPresetId = embeddingPresetId,
                    ClientId = _appSettings.ClientId,
                    InputText = message,
                    Images = images.Count > 0 ? images : null,
                    ClientContext = GetClientContextSnapshot()
                };

                var buffer = new StringBuilder();

                await foreach (var ev in _cocoroGhostApiClient!.StreamChatAsync(chatRequest))
                {
                    switch (ev.Type)
                    {
                        case "token":
                            if (!string.IsNullOrEmpty(ev.Delta))
                            {
                                buffer.Append(ev.Delta);
                                StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                                {
                                    Content = buffer.ToString(),
                                    IsFinished = false,
                                    IsError = false
                                });
                            }
                            break;
                        case "done":
                            var replyText = ev.ReplyText ?? buffer.ToString();
                            var memoryId = _cachedMemoryId;
                            var chatReply = new ChatRequest
                            {
                                memoryId = memoryId,
                                sessionId = _currentSessionId,
                                message = replyText,
                                role = "assistant",
                                content = replyText
                            };

                            StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                            {
                                Content = replyText,
                                IsFinished = true,
                                IsError = false
                            });

                            ChatMessageReceived?.Invoke(this, chatReply);
                            ForwardMessageToShellAsync(replyText, GetStoredCharacterSetting());

                            _statusPollingService.SetNormalStatus();
                            StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "チャット完了"));
                            return;
                        case "error":
                            var errorMessage = ev.ErrorMessage ?? "チャットAPIエラーが発生しました";
                            StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                            {
                                Content = "",
                                IsFinished = true,
                                IsError = true,
                                ErrorMessage = errorMessage
                            });
                            _statusPollingService.SetNormalStatus();
                            StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"チャットエラー: {errorMessage}"));
                            return;
                        default:
                            break;
                    }
                }

                // 想定外にストリームが途切れた場合も正常化する
                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "チャット完了"));
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine($"チャット送信タイムアウト: {ex.Message}");
                StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                {
                    Content = "",
                    IsFinished = true,
                    IsError = true,
                    ErrorMessage = $"チャット送信タイムアウト: {ex.Message}"
                });
                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"チャット送信タイムアウト: {ex.Message}"));
            }
            catch (HttpRequestException ex)
            {
                // 422/401 など非200はここに来る（body含む例外メッセージ）
                Debug.WriteLine($"チャット送信HTTPエラー: {ex.Message}");
                StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                {
                    Content = "",
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
                    Content = "",
                    IsFinished = true,
                    IsError = true,
                    ErrorMessage = $"チャット送信エラー: {ex.Message}"
                });
                // エラー時は正常状態に戻す
                _statusPollingService.SetNormalStatus();
                // ステータスバーにエラー表示
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"チャット送信エラー: {ex.Message}"));
            }
        }

        private async Task<string?> ResolveEmbeddingPresetIdForChatAsync()
        {
            var cached = TryResolveEmbeddingPresetId(_cachedCocoroGhostSettings);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                return cached;
            }

            if (_cocoroGhostApiClient == null)
            {
                return null;
            }

            try
            {
                var settings = await _cocoroGhostApiClient.GetSettingsAsync();
                _cachedCocoroGhostSettings = settings;
                return TryResolveEmbeddingPresetId(settings);
            }
            catch
            {
                return null;
            }
        }

        private static string? TryResolveEmbeddingPresetId(CocoroConsole.Models.CocoroGhostApi.CocoroGhostSettings? settings)
        {
            if (settings == null)
            {
                return null;
            }

            var active = settings.ActiveEmbeddingPresetId;
            if (!string.IsNullOrWhiteSpace(active))
            {
                return active;
            }

            return settings.EmbeddingPreset?.FirstOrDefault()?.EmbeddingPresetId;
        }

        public async Task SetDesktopWatchEnabledAsync(bool enabled)
        {
            if (_cocoroGhostApiClient == null)
            {
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, "cocoro_ghostのBearerトークンが設定されていません"));
                return;
            }

            try
            {
                var latest = await _cocoroGhostApiClient.GetSettingsAsync();
                var request = CreateSettingsUpdateRequest(latest);

                request.DesktopWatchEnabled = enabled;
                if (request.DesktopWatchIntervalSeconds <= 0)
                {
                    request.DesktopWatchIntervalSeconds = 300;
                }

                if (enabled && string.IsNullOrWhiteSpace(request.DesktopWatchTargetClientId))
                {
                    request.DesktopWatchTargetClientId = _appSettings.ClientId;
                }

                var updated = await _cocoroGhostApiClient.UpdateSettingsAsync(request);
                _cachedCocoroGhostSettings = updated;
                ApplyCocoroGhostSettingsToAppSettings(updated);
                CocoroGhostSettingsUpdated?.Invoke(this, updated);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DesktopWatch] 設定更新エラー: {ex.Message}");
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"デスクトップウォッチ設定更新エラー: {ex.Message}"));
            }
        }


        /// <summary>
        /// CocoroShellにアニメーションコマンドを送信
        /// </summary>
        /// <param name="animationName">アニメーション名</param>
        public async Task SendAnimationToShellAsync(string animationName)
        {
            try
            {
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

            var bearerToken = _appSettings.CocoroGhostBearerToken;
            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                LogStreamError?.Invoke(this, "cocoro_ghostのBearerトークンが設定されていません");
                return;
            }

            var logStreamUri = new Uri($"ws://127.0.0.1:{_appSettings.CocoroGhostPort}/api/logs/stream");
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
                LogStreamError?.Invoke(this, $"ログストリーム接続に失敗しました: {ex.Message}");
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
                return;
            }

            var bearerToken = _appSettings.CocoroGhostBearerToken;
            if (string.IsNullOrWhiteSpace(bearerToken))
            {
                return;
            }

            var eventsStreamUri = new Uri($"ws://127.0.0.1:{_appSettings.CocoroGhostPort}/api/events/stream");
            _eventsStreamClient = new EventsStreamClient(
                eventsStreamUri,
                bearerToken,
                _appSettings.ClientId,
                new[] { "vision.desktop", "vision.camera", "speaker" }
            );
            _eventsStreamClient.EventReceived += OnEventsStreamEventReceived;
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
                _eventsStreamClient.ErrorOccurred -= OnEventsStreamErrorOccurred;
                _eventsStreamClient.Dispose();
                _eventsStreamClient = null;
            }
        }

        private void OnEventsStreamEventReceived(object? sender, CocoroGhostEvent ev)
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
                        }, null);
                    }

                    // New behavior: /api/notification generates persona "message" and pushes it via /api/events/stream
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
                        var (images, windowTitle) = TryConsumeDesktopWatchCapture();
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

        private void HandlePartnerMessageFromEvent(CocoroGhostEvent ev, string partnerMessage)
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

        private async Task HandleVisionCaptureRequestAsync(CocoroGhostEvent ev)
        {
            if (_cocoroGhostApiClient == null)
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
                    _statusPollingService.SetProcessingStatus(CocoroGhostStatus.ProcessingImage);
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
                await _cocoroGhostApiClient.SendVisionCaptureResponseAsync(response, cts.Token);
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

                const string marker = "base64,";
                var markerIndex = dataUri.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0)
                {
                    return null;
                }

                var base64 = dataUri.Substring(markerIndex + marker.Length);
                if (string.IsNullOrWhiteSpace(base64))
                {
                    return null;
                }

                var bytes = Convert.FromBase64String(base64);
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

        private static CocoroGhostSettingsUpdateRequest CreateSettingsUpdateRequest(CocoroGhostSettings latestSettings)
        {
            latestSettings ??= new CocoroGhostSettings();

            var activeLlmId = latestSettings.ActiveLlmPresetId ?? latestSettings.LlmPreset.FirstOrDefault()?.LlmPresetId;
            var activeEmbeddingId = latestSettings.ActiveEmbeddingPresetId ?? latestSettings.EmbeddingPreset.FirstOrDefault()?.EmbeddingPresetId;
            var activePersonaId = latestSettings.ActivePersonaPresetId ?? latestSettings.PersonaPreset.FirstOrDefault()?.PersonaPresetId;
            var activeAddonId = latestSettings.ActiveAddonPresetId ?? latestSettings.AddonPreset.FirstOrDefault()?.AddonPresetId;

            if (string.IsNullOrWhiteSpace(activeLlmId) ||
                string.IsNullOrWhiteSpace(activeEmbeddingId) ||
                string.IsNullOrWhiteSpace(activePersonaId) ||
                string.IsNullOrWhiteSpace(activeAddonId))
            {
                throw new InvalidOperationException("API設定のアクティブプリセットIDが取得できません。cocoro_ghost側のsettings.dbを確認してください。");
            }

            return new CocoroGhostSettingsUpdateRequest
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
        }


        /// <summary>
        /// CocoroShellから現在のキャラクター位置を取得
        /// </summary>
        public async Task<PositionResponse> GetShellPositionAsync()
        {
            try
            {
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

            // セマフォの解放
            _forwardMessageSemaphore?.Dispose();

            if (_statusPollingService != null)
            {
                _statusPollingService.StatusChanged -= OnStatusPollingServiceStatusChanged;
                _statusPollingService.Dispose();
            }
            _apiServer?.Dispose();
            _shellClient?.Dispose();
            _logStreamClient?.Dispose();
            _eventsStreamClient?.Dispose();
        }
    }
}
