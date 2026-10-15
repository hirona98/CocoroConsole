using CocoroConsole.Communication;
using CocoroConsole.Models.OtomeKairoApi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CocoroConsole.Services
{
    /// <summary>
    /// アプリケーション設定を管理するクラス。
    /// ローカルに保存するのは接続情報だけです。通常設定は OtomeKairo の文書を実行時の正とします。
    /// </summary>
    public class AppSettings : IAppSettings
    {
        public const string BuiltInAvatarModel = "default";

        private static readonly Lazy<AppSettings> _instance = new Lazy<AppSettings>(() => new AppSettings());
        private readonly object _connectionSettingsSaveLock = new object();

        public static AppSettings Instance => _instance.Value;

        public static event EventHandler? SettingsSaved;

        public string UserDataDirectory { get; }

        private string ConnectionSettingsFilePath => Path.Combine(UserDataDirectory, "Connection.json");

        public string ServerUrl { get; set; } = "https://127.0.0.1:55601";
        public List<string> ServerUrlHistory { get; private set; } = new List<string>();
        public string ClientId { get; set; } = string.Empty;
        public string? SelectedConversationDisplayNameId
        {
            get => CurrentSettings.SelectedConversationDisplayNameId;
            set => CurrentSettings.SelectedConversationDisplayNameId = value;
        }
        public List<OtomeKairoConversationDisplayNameDefinition> ConversationDisplayNames { get; set; }
            = new List<OtomeKairoConversationDisplayNameDefinition>();
        public string ConversationDisplayName => ConversationDisplayNames
            .SingleOrDefault(definition => string.Equals(
                definition.ConversationDisplayNameId,
                SelectedConversationDisplayNameId,
                StringComparison.Ordinal))?.DisplayName ?? string.Empty;
        public string OtomeKairoBearerToken { get; set; } = string.Empty;
        public string ShellSessionToken { get; set; } = string.Empty;

        public OtomeKairoConsoleClientSettings ConsoleClient { get; set; } = new OtomeKairoConsoleClientSettings();
        public OtomeKairoCurrentSettings CurrentSettings { get; set; } = new OtomeKairoCurrentSettings();
        public OtomeKairoAvatarSpeechEditorState AvatarSpeech { get; set; } = new OtomeKairoAvatarSpeechEditorState();

        public int CocoroConsolePort
        {
            get => ConsoleClient.Process.ConsoleApiPort;
            set => ConsoleClient.Process.ConsoleApiPort = value;
        }

        public int CocoroShellPort
        {
            get => ConsoleClient.Process.CocoroShellPort;
            set => ConsoleClient.Process.CocoroShellPort = value;
        }

        public string OtomeKairoHost
        {
            get
            {
                if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri))
                {
                    return string.Empty;
                }

                return uri.Host;
            }
        }

        public int OtomeKairoPort
        {
            get
            {
                if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri))
                {
                    return 0;
                }

                return uri.Port;
            }
        }

        public bool IsLoaded { get; set; } = false;
        public bool HasRemoteSettings { get; private set; }

        private const int MaxServerUrlHistory = 20;

        public void SaveVerifiedConnection(string serverUrl, string consoleAccessToken)
        {
            var normalizedServerUrl = OtomeKairoConnectionBootstrapper.NormalizeServerUrl(serverUrl);
            string? normalizedCurrentServerUrl;
            try
            {
                normalizedCurrentServerUrl =
                    OtomeKairoConnectionBootstrapper.NormalizeServerUrl(ServerUrl);
            }
            catch (InvalidOperationException)
            {
                normalizedCurrentServerUrl = null;
            }
            var endpointChanged = !string.Equals(
                normalizedCurrentServerUrl,
                normalizedServerUrl,
                StringComparison.OrdinalIgnoreCase);

            var previousServerUrl = ServerUrl;
            var previousAccessToken = OtomeKairoBearerToken;
            var previousHasRemoteSettings = HasRemoteSettings;
            var previousHistory = new List<string>(ServerUrlHistory);

            ServerUrl = normalizedServerUrl;
            OtomeKairoBearerToken = consoleAccessToken.Trim();
            RememberServerUrlInHistory(normalizedServerUrl);
            if (endpointChanged)
            {
                HasRemoteSettings = false;
            }

            try
            {
                SaveAppSettings();
            }
            catch
            {
                ServerUrl = previousServerUrl;
                OtomeKairoBearerToken = previousAccessToken;
                HasRemoteSettings = previousHasRemoteSettings;
                ServerUrlHistory = previousHistory;
                throw;
            }
        }

        private AppSettings()
        {
            UserDataDirectory = FindUserDataDirectory();
            LoadSettings();
        }

        private string FindUserDataDirectory()
        {
            var baseDirectory = AppContext.BaseDirectory;

            string[] searchPaths = {
#if !DEBUG
                Path.Combine(baseDirectory, "UserData"),
#endif
                Path.Combine(baseDirectory, "..", "UserData"),
                Path.Combine(baseDirectory, "..", "..", "UserData"),
                Path.Combine(baseDirectory, "..", "..", "..", "UserData"),
                Path.Combine(baseDirectory, "..", "..", "..", "..", "UserData")
            };

            foreach (var path in searchPaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                {
                    Debug.WriteLine($"UserDataディレクトリを解決しました: {fullPath}");
                    return fullPath;
                }
            }

            var defaultPath = Path.GetFullPath(searchPaths[0]);
            Debug.WriteLine($"UserDataディレクトリを作成しました: {defaultPath}");
            Directory.CreateDirectory(defaultPath);
            return defaultPath;
        }

        public ShellRuntimeConfig BuildShellRuntimeConfig()
        {
            if (!HasRemoteSettings)
            {
                throw new InvalidOperationException("OtomeKairoの端末設定を取得していません。");
            }

            PrepareConsoleClientForSave();
            var speech = GetCurrentAvatar()
                ?? throw new InvalidOperationException("選択中のアバター設定がありません。");
            var presentation = GetCurrentAvatarPresentation()
                ?? throw new InvalidOperationException("選択中のアバター表示設定がありません。");
            var motion = ConsoleClient.Motion;
            var selectedAnimation = motion.AnimationSets
                .Single(animationSet => animationSet.AnimationSetId == motion.SelectedAnimationSetId);
            var display = ConsoleClient.Display;

            return new ShellRuntimeConfig
            {
                clientId = ClientId,
                shellApiPort = CocoroShellPort,
                display = new ShellDisplaySettings
                {
                    restoreWindowPosition = display.RestoreWindowPosition,
                    topmost = display.Topmost,
                    escapeCursor = display.EscapeCursor,
                    escapePositions = display.EscapePositions.Select(position => new EscapePosition
                    {
                        x = position.X,
                        y = position.Y,
                        enabled = position.Enabled,
                    }).ToList(),
                    touchVirtualKeyEnabled = display.TouchVirtualKeyEnabled,
                    virtualKey = display.VirtualKey,
                    autoMove = display.AutoMove,
                    showMessageWindow = display.ShowMessageWindow,
                    messageWindow = new MessageWindowSettings
                    {
                        maxMessageCount = display.MessageWindow.MaxMessageCount,
                        maxTotalAvatars = display.MessageWindow.MaxTotalCharacters,
                        minWindowSize = display.MessageWindow.MinWindowSize,
                        maxWindowSize = display.MessageWindow.MaxWindowSize,
                        fontSize = display.MessageWindow.FontSize,
                        horizontalOffset = display.MessageWindow.HorizontalOffset,
                        verticalOffset = display.MessageWindow.VerticalOffset,
                    },
                    ambientOcclusionEnabled = display.AmbientOcclusionEnabled,
                    avatarWindowSize = display.AvatarWindowSize,
                    avatarPositionX = display.AvatarPositionX,
                    avatarPositionY = display.AvatarPositionY,
                    msaaLevel = display.MsaaLevel,
                    avatarShadowMode = display.AvatarShadowMode,
                    avatarShadowResolution = display.AvatarShadowResolution,
                    backgroundShadowMode = display.BackgroundShadowMode,
                    backgroundShadowResolution = display.BackgroundShadowResolution,
                },
                avatar = new ShellAvatarSettings
                {
                    avatarId = speech.AvatarId,
                    isReadOnly = IsBuiltInAvatar(presentation),
                    modelName = speech.DisplayName,
                    vrmFilePath = presentation.Model,
                    isConvertMToon = presentation.ConvertUnlitToMtoon,
                    isEnableShadowOff = presentation.ShadowExclusionEnabled,
                    shadowOffMesh = string.Join(",", presentation.ShadowExcludedMeshNames),
                },
                motion = new ShellMotionSettings
                {
                    selectedAnimationSetId = selectedAnimation.AnimationSetId,
                    animationSettings = motion.AnimationSets.Select(animationSet => new AnimationSetting
                    {
                        animationSetId = animationSet.AnimationSetId,
                        animeSetName = animationSet.DisplayName,
                        postureChangeLoopCountStanding = animationSet.PostureChangeLoopCountStanding,
                        postureChangeLoopCountSittingFloor = animationSet.PostureChangeLoopCountSittingFloor,
                        animations = animationSet.Animations.Select(animation => new AnimationConfig
                        {
                            displayName = animation.DisplayName,
                            animationType = animation.AnimationType,
                            animationName = animation.AnimationName,
                            isEnabled = animation.Enabled,
                        }).ToList(),
                    }).ToList(),
                },
            };
        }

        public void ApplyRemoteSettings(
            OtomeKairoConsoleClientSettings consoleSettings,
            OtomeKairoCurrentSettings currentSettings,
            IReadOnlyList<OtomeKairoConversationDisplayNameDefinition> conversationDisplayNames,
            OtomeKairoAvatarSpeechEditorState avatarSpeech)
        {
            ConsoleClient = consoleSettings;
            CurrentSettings = currentSettings;
            ConversationDisplayNames = conversationDisplayNames.ToList();
            AvatarSpeech = avatarSpeech;
            IsLoaded = true;
            HasRemoteSettings = true;
        }

        public void PrepareConsoleClientForSave()
        {
            var animationSets = ConsoleClient.Motion.AnimationSets;
            if (animationSets.Count == 0)
            {
                throw new InvalidOperationException("アニメーション設定がありません。");
            }

            foreach (var animationSet in animationSets)
            {
                if (string.IsNullOrWhiteSpace(animationSet.AnimationSetId))
                {
                    animationSet.AnimationSetId = $"animation_set:{Guid.NewGuid():N}";
                }
            }

            if (!animationSets.Any(animationSet =>
                    animationSet.AnimationSetId == ConsoleClient.Motion.SelectedAnimationSetId))
            {
                ConsoleClient.Motion.SelectedAnimationSetId = animationSets[0].AnimationSetId;
            }
        }

        public void PrepareAvatarSpeechForSave()
        {
            if (AvatarSpeech.Avatars.Count == 0)
            {
                throw new InvalidOperationException("アバター設定がありません。");
            }

            foreach (var avatar in AvatarSpeech.Avatars)
            {
                if (string.IsNullOrWhiteSpace(avatar.AvatarId))
                {
                    avatar.AvatarId = $"avatar:{Guid.NewGuid():N}";
                }
            }

            if (!AvatarSpeech.Avatars.Any(avatar => avatar.AvatarId == AvatarSpeech.SelectedAvatarId))
            {
                AvatarSpeech.SelectedAvatarId = AvatarSpeech.Avatars[0].AvatarId;
            }
        }

        public static bool IsBuiltInAvatar(OtomeKairoConsoleAvatarPresentation? presentation)
        {
            return presentation != null &&
                string.Equals(presentation.Model, BuiltInAvatarModel, StringComparison.OrdinalIgnoreCase);
        }

        public static T CloneDocument<T>(T value) where T : class
        {
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize<T>(json)
                ?? throw new InvalidOperationException("設定を複製できません。");
        }

        public string GetOtomeKairoBaseUrl()
        {
            return NormalizeServerUrl(ServerUrl);
        }

        public string GetOtomeKairoWebSocketBaseUrl()
        {
            var serverUri = new Uri(GetOtomeKairoBaseUrl(), UriKind.Absolute);
            var builder = new UriBuilder(serverUri)
            {
                Scheme = serverUri.Scheme == Uri.UriSchemeHttp ? "ws" : "wss",
            };
            return builder.Uri.GetLeftPart(UriPartial.Authority);
        }

        private static string NormalizeServerUrl(string? serverUrl)
        {
            return OtomeKairoConnectionBootstrapper.NormalizeServerUrl(serverUrl);
        }

        public void LoadSettings()
        {
            LoadAppSettings();
            IsLoaded = true;
        }

        public void LoadAppSettings()
        {
            EnsureUserDataDirectoryExists();
            if (File.Exists(ConnectionSettingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(ConnectionSettingsFilePath);
                    var connection = JsonSerializer.Deserialize<ConnectionSettings>(json)
                        ?? throw new InvalidOperationException("Connection.jsonを読み込めません。");
                    ServerUrl = connection.ServerUrl.Trim();
                    ClientId = connection.ClientId.Trim();
                    OtomeKairoBearerToken = connection.ConsoleAccessToken;
                    ServerUrlHistory = NormalizeServerUrlHistory(connection.ServerUrlHistory);
                }
                catch (Exception ex) when (
                    ex is JsonException ||
                    ex is InvalidOperationException)
                {
                    ServerUrl = string.Empty;
                    ClientId = string.Empty;
                    OtomeKairoBearerToken = string.Empty;
                    ServerUrlHistory = new List<string>();
                    Debug.WriteLine($"Connection.jsonの読み込みに失敗しました: {ex.Message}");
                }
            }

            if (string.IsNullOrWhiteSpace(ClientId))
            {
                ClientId = $"console-{Guid.NewGuid()}";
            }

            try
            {
                ServerUrl = NormalizeServerUrl(ServerUrl);
                RememberServerUrlInHistory(ServerUrl);
                SaveConnectionSettings();
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Connection.jsonのサーバーURLが不正です: {ex.Message}");
            }
        }

        private void EnsureUserDataDirectoryExists()
        {
            if (!Directory.Exists(UserDataDirectory))
            {
                Directory.CreateDirectory(UserDataDirectory);
            }
        }

        public OtomeKairoAvatarSpeechDefinition CreateAvatarFromDefaults(string displayName)
        {
            var source = GetCurrentAvatar()
                ?? throw new InvalidOperationException("複製元のアバター設定がありません。");
            var avatar = CloneDocument(source);
            avatar.AvatarId = $"avatar:{Guid.NewGuid():N}";
            avatar.DisplayName = displayName;
            return avatar;
        }

        public void SaveAppSettings()
        {
            try
            {
                SaveConnectionSettings();
                SettingsSaved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"接続情報の保存に失敗しました: {ex.Message}");
                throw;
            }
        }

        public void SaveSettings()
        {
            SaveAppSettings();
        }

        private void SaveConnectionSettings()
        {
            lock (_connectionSettingsSaveLock)
            {
                SaveConnectionSettingsCore();
            }
        }

        private void SaveConnectionSettingsCore()
        {
            EnsureUserDataDirectoryExists();
            ServerUrl = NormalizeServerUrl(ServerUrl);
            RememberServerUrlInHistory(ServerUrl);
            var connection = new ConnectionSettings
            {
                ServerUrl = ServerUrl,
                ClientId = ClientId.Trim(),
                ConsoleAccessToken = OtomeKairoBearerToken ?? string.Empty,
                ServerUrlHistory = new List<string>(ServerUrlHistory),
            };
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            var temporaryPath = Path.Combine(UserDataDirectory, $"Connection.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
                    writer.Write(JsonSerializer.Serialize(connection, options));
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                if (File.Exists(ConnectionSettingsFilePath))
                {
                    File.Replace(temporaryPath, ConnectionSettingsFilePath, null);
                }
                else
                {
                    File.Move(temporaryPath, ConnectionSettingsFilePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            Debug.WriteLine($"接続情報を保存しました: {ConnectionSettingsFilePath}");
        }

        private void RememberServerUrlInHistory(string serverUrl)
        {
            var normalized = NormalizeServerUrl(serverUrl);
            ServerUrlHistory.RemoveAll(url =>
                string.Equals(url, normalized, StringComparison.OrdinalIgnoreCase));
            ServerUrlHistory.Insert(0, normalized);
            if (ServerUrlHistory.Count > MaxServerUrlHistory)
            {
                ServerUrlHistory.RemoveRange(
                    MaxServerUrlHistory,
                    ServerUrlHistory.Count - MaxServerUrlHistory);
            }
        }

        private static List<string> NormalizeServerUrlHistory(IEnumerable<string>? source)
        {
            var result = new List<string>();
            if (source == null)
            {
                return result;
            }

            foreach (var item in source)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                try
                {
                    var normalized = NormalizeServerUrl(item);
                    if (result.Any(url =>
                            string.Equals(url, normalized, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    result.Add(normalized);
                    if (result.Count >= MaxServerUrlHistory)
                    {
                        break;
                    }
                }
                catch (InvalidOperationException)
                {
                    // 壊れた履歴項目は捨てる。
                }
            }

            return result;
        }

        public OtomeKairoAvatarSpeechDefinition? GetCurrentAvatar()
        {
            if (AvatarSpeech.Avatars.Count == 0)
            {
                return null;
            }

            return AvatarSpeech.Avatars.FirstOrDefault(avatar =>
                avatar.AvatarId == AvatarSpeech.SelectedAvatarId);
        }

        public OtomeKairoConsoleAvatarPresentation? GetCurrentAvatarPresentation()
        {
            var avatar = GetCurrentAvatar();
            if (avatar == null)
            {
                return null;
            }

            return ConsoleClient.AvatarPresentations.FirstOrDefault(presentation =>
                presentation.AvatarId == avatar.AvatarId);
        }

        public OtomeKairoConsoleWindowPlacement? GetWindowPlacement(string windowKey)
        {
            if (string.IsNullOrWhiteSpace(windowKey))
            {
                return null;
            }

            if (ConsoleClient.Display.WindowPlacements.TryGetValue(windowKey, out var placement))
            {
                return placement;
            }

            return null;
        }

        public void SetWindowPlacement(string windowKey, double left, double top)
        {
            if (string.IsNullOrWhiteSpace(windowKey))
            {
                return;
            }

            ConsoleClient.Display.WindowPlacements[windowKey] = new OtomeKairoConsoleWindowPlacement
            {
                Left = left,
                Top = top,
            };
        }
    }

    /// <summary>
    /// OtomeKairo へ接続するために端末内へ保持する最小設定。
    /// </summary>
    public class ConnectionSettings
    {
        [JsonPropertyName("server_url")]
        public string ServerUrl { get; set; } = "https://127.0.0.1:55601";

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("console_access_token")]
        public string ConsoleAccessToken { get; set; } = string.Empty;

        [JsonPropertyName("server_url_history")]
        public List<string> ServerUrlHistory { get; set; } = new List<string>();
    }
}
