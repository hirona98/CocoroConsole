using CocoroConsole.Communication;
using CocoroConsole.Windows;
using CocoroAI.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CocoroConsole.Services
{
    /// <summary>
    /// CocoroAIとの通信を管理するサービスクラス
    /// </summary>
    public class CommunicationService : ICommunicationService
    {
        // リアルタイムストリーミング設定（即座表示方式）

        private readonly CocoroConsoleApiServer _apiServer;
        private readonly CocoroShellClient _shellClient;
        private readonly IAppSettings _appSettings;
        private readonly StatusPollingService _statusPollingService;
        private readonly CocoroGhostApiClient? _cocoroGhostApiClient;
        private LogStreamClient? _logStreamClient;

        // メッセージ順序保証用セマフォ
        private readonly SemaphoreSlim _forwardMessageSemaphore = new SemaphoreSlim(1, 1);

        // セッション管理用
        private string? _currentSessionId;

        // 設定キャッシュ用
        private ConfigSettings? _cachedConfigSettings;

        // memory_idキャッシュ
        private string _cachedMemoryId = "memory";

        // 起動時の設定取得済みフラグ
        private bool _initialSettingsFetched = false;

        public event EventHandler<ChatRequest>? ChatMessageReceived;
        public event EventHandler<StreamingChatEventArgs>? StreamingChatReceived;
        public event Action<ChatMessagePayload, List<System.Windows.Media.Imaging.BitmapSource>?>? NotificationMessageReceived;
        public event EventHandler<ControlRequest>? ControlCommandReceived;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<StatusUpdateEventArgs>? StatusUpdateRequested;
        public event EventHandler<CocoroGhostStatus>? StatusChanged;
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
            _apiServer = new CocoroConsoleApiServer(_appSettings.CocoroConsolePort, _appSettings);
            _apiServer.ChatMessageReceived += (sender, request) => ChatMessageReceived?.Invoke(this, request);
            _apiServer.ControlCommandReceived += (sender, request) => ControlCommandReceived?.Invoke(this, request);
            _apiServer.StatusUpdateReceived += (sender, request) =>
            {
                // ステータス更新イベントを発火
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, request.message));
            };

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
            RefreshSettingsCache();
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

                // AppSettingsに反映
                ApplyCocoroGhostSettingsToAppSettings(settings);

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
            // ExcludeKeywordsをScreenshotSettings.excludePatternsに反映
            // （LLM/Embedding設定はcocoro_ghost側で管理されているため、AppSettingsには保存しない）
            AppSettings.Instance.ApplyCocoroGhostSettings(settings);

            // 設定キャッシュを更新
            RefreshSettingsCache();

            Debug.WriteLine("[CommunicationService] AppSettingsにcocoro_ghost設定を反映完了");
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
                // 現在のキャラクター設定を取得
                var currentCharacter = GetStoredCharacterSetting();

                // LLMが無効の場合は処理しない
                if (currentCharacter == null || !currentCharacter.isUseLLM)
                {
                    Debug.WriteLine("チャット送信: LLMが無効のためスキップ");
                    return;
                }

                // セッションIDを生成または既存のものを使用
                if (string.IsNullOrEmpty(_currentSessionId))
                {
                    _currentSessionId = $"dock_{DateTime.Now:yyyyMMddHHmmssfff}";
                }

                // 画像データを変換
                string? imageBase64 = null;
                if (imageDataUrls != null && imageDataUrls.Count > 0)
                {
                    imageBase64 = ExtractBase64(imageDataUrls.First());
                }

                // 画像がある場合は画像処理中、そうでなければメッセージ処理中に設定
                var processingStatus = (!string.IsNullOrEmpty(imageBase64))
                    ? CocoroGhostStatus.ProcessingImage
                    : CocoroGhostStatus.ProcessingMessage;
                _statusPollingService.SetProcessingStatus(processingStatus);

                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "チャット送信開始"));

                var chatRequest = new ChatStreamRequest
                {
                    UserText = message,
                    Images = string.IsNullOrEmpty(imageBase64)
                        ? new List<CocoroGhostImage>()
                        : new List<CocoroGhostImage>
                        {
                            new CocoroGhostImage
                            {
                                Type = "desktop_capture",
                                Base64 = imageBase64
                            }
                        }
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

                            ChatMessageReceived?.Invoke(this, chatReply);
                            ForwardMessageToShellAsync(replyText, currentCharacter);

                            StreamingChatReceived?.Invoke(this, new StreamingChatEventArgs
                            {
                                Content = replyText,
                                IsFinished = true,
                                IsError = false
                            });

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
            catch (Exception ex)
            {
                Debug.WriteLine($"チャット送信エラー: {ex.Message}");
                // エラー時は正常状態に戻す
                _statusPollingService.SetNormalStatus();
                // ステータスバーにエラー表示
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"チャット送信エラー: {ex.Message}"));
            }
        }

        /// <summary>
        /// デスクトップウォッチ画像をCocoroGhostに送信
        /// </summary>
        /// <param name="screenshotData">スクリーンショットデータ</param>
        public async Task SendDesktopWatchToCoreAsync(ScreenshotData screenshotData)
        {
            if (_cocoroGhostApiClient == null)
            {
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, "cocoro_ghostのBearerトークンが設定されていません"));
                return;
            }

            try
            {
                var request = new CaptureRequest
                {
                    CaptureType = "desktop",
                    ImageBase64 = screenshotData.ImageBase64,
                    ContextText = screenshotData.WindowTitle
                };

                _statusPollingService.SetProcessingStatus(CocoroGhostStatus.ProcessingImage);
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "デスクトップウォッチ送信中"));

                await _cocoroGhostApiClient.SendCaptureAsync(request);

                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "デスクトップウォッチ送信完了"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"デスクトップウォッチ送信エラー: {ex.Message}");
                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"デスクトップウォッチ送信エラー: {ex.Message}"));
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
        /// 通知メッセージを処理（Notification API用）
        /// </summary>
        /// <param name="notification">通知メッセージ</param>
        /// <param name="imageDataUrls">画像データURL配列（オプション）</param>
        public async Task ProcessNotificationAsync(ChatMessagePayload notification, string[]? imageDataUrls = null)
        {
            try
            {
                if (_cocoroGhostApiClient == null)
                {
                    StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, "cocoro_ghostのBearerトークンが設定されていません"));
                    return;
                }

                // 現在のキャラクター設定を取得
                var currentCharacter = GetStoredCharacterSetting();

                // LLMが有効でない場合は処理しない
                if (currentCharacter == null || !currentCharacter.isUseLLM)
                {
                    Debug.WriteLine("通知処理: LLMが無効のためスキップ");
                    return;
                }

                var images = CreateCocoroGhostImages(imageDataUrls);

                var request = new NotificationRequest
                {
                    SourceSystem = notification.from,
                    Title = notification.from,
                    Body = notification.message,
                    Images = images
                };

                _statusPollingService.SetProcessingStatus(CocoroGhostStatus.ProcessingMessage);
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "通知処理開始"));

                await _cocoroGhostApiClient.SendNotificationAsync(request);

                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "通知処理完了"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"通知処理エラー: {ex.Message}");
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"通知処理エラー: {ex.Message}"));
            }
        }

        public async Task ProcessDirectRequestAsync(ChatMessagePayload request, string[]? imageDataUrls = null)
        {
            try
            {
                if (_cocoroGhostApiClient == null)
                {
                    StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, "cocoro_ghostのBearerトークンが設定されていません"));
                    return;
                }

                var currentCharacter = GetStoredCharacterSetting();

                if (currentCharacter == null || !currentCharacter.isUseLLM)
                {
                    Debug.WriteLine("DirectRequest処理: LLMが無効のためスキップ");
                    return;
                }

                var images = CreateCocoroGhostImages(imageDataUrls);

                var metaRequest = new MetaRequest
                {
                    Instruction = $"Direct request from {request.from}",
                    PayloadText = request.message,
                    Images = images
                };

                _statusPollingService.SetProcessingStatus(CocoroGhostStatus.ProcessingMessage);
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "DirectRequest処理開始"));

                await _cocoroGhostApiClient.SendMetaRequestAsync(metaRequest);

                _statusPollingService.SetNormalStatus();
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(true, "DirectRequest処理完了"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DirectRequest処理エラー: {ex.Message}");
                StatusUpdateRequested?.Invoke(this, new StatusUpdateEventArgs(false, $"DirectRequest処理エラー: {ex.Message}"));
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

        private string? ExtractBase64(string? dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl))
            {
                return null;
            }

            const string marker = "base64,";
            var index = dataUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && index + marker.Length < dataUrl.Length)
            {
                return dataUrl.Substring(index + marker.Length);
            }

            return dataUrl;
        }

        private List<CocoroGhostImage> CreateCocoroGhostImages(string[]? imageDataUrls)
        {
            var images = new List<CocoroGhostImage>();
            if (imageDataUrls == null || imageDataUrls.Length == 0)
            {
                return images;
            }

            foreach (var imageDataUrl in imageDataUrls)
            {
                if (string.IsNullOrWhiteSpace(imageDataUrl))
                {
                    continue;
                }

                var base64 = ExtractBase64(imageDataUrl);
                if (string.IsNullOrWhiteSpace(base64))
                {
                    continue;
                }

                images.Add(new CocoroGhostImage
                {
                    Type = "external",
                    Base64 = base64
                });
            }

            return images;
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

            _statusPollingService?.Dispose();
            _apiServer?.Dispose();
            _shellClient?.Dispose();
            _logStreamClient?.Dispose();
        }
    }
}
