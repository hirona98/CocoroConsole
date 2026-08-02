using CocoroConsole.Communication;
using CocoroConsole.Models;
using CocoroConsole.Models.OtomeKairoApi;
using CocoroConsole.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CocoroConsole.Services
{
    /// <summary>
    /// アプリケーション設定を管理するクラス
    /// </summary>
    public class AppSettings : IAppSettings
    {
        // CocoroConsoleとCocoroShellが組み込みモデルとして解釈するモデル指定値。
        private const string BuiltInModel = "default";

        private static readonly Lazy<AppSettings> _instance = new Lazy<AppSettings>(() => new AppSettings());

        public static AppSettings Instance => _instance.Value;

        /// <summary>
        /// 設定が保存されたときに発生するイベント
        /// </summary>
        public static event EventHandler? SettingsSaved;

        // UserDataディレクトリのパス
        public string UserDataDirectory { get; }

        // ローカルには OtomeKairo へ接続するためのブートストラップ情報だけを保存する。
        private string ConnectionSettingsFilePath => Path.Combine(UserDataDirectory, "Connection.json");

        public string ServerUrl { get; set; } = "https://127.0.0.1:55601";
        public int CocoroConsolePort { get; set; }
        public int OtomeKairoPort { get; set; } = 55601;
        public string OtomeKairoHost { get; set; } = "127.0.0.1";
        public int CocoroShellPort { get; set; }
        // /api/events/stream で hello を送るためのクライアントID（安定ID）
        public string ClientId { get; set; } = string.Empty;
        // テキスト入力で participants[].display_name に渡す呼び名
        public string ConversationDisplayName { get; set; } = string.Empty;
        // otomekairo API Bearer トークン
        public string OtomeKairoBearerToken { get; set; } = string.Empty;
        // CocoroShell プロセスの起動中だけ保持する一時トークン
        public string ShellSessionToken { get; set; } = string.Empty;
        // LLMを使用するか
        public bool IsUseLLM { get; set; } = false;
        // UI設定
        public bool IsRestoreWindowPosition { get; set; }
        public bool IsTopmost { get; set; }
        public bool IsEscapeCursor { get; set; }
        public List<EscapePosition> EscapePositions { get; set; } = new List<EscapePosition>();
        public bool IsInputVirtualKey { get; set; }
        public string VirtualKeyString { get; set; } = string.Empty;
        public bool IsAutoMove { get; set; }
        public bool ShowMessageWindow { get; set; }
        public bool IsEnableAmbientOcclusion { get; set; }
        public int MsaaLevel { get; set; }
        public int AvatarShadow { get; set; }
        public int AvatarShadowResolution { get; set; }
        public int BackgroundShadow { get; set; }
        public int BackgroundShadowResolution { get; set; }
        public int WindowSize { get; set; }
        public float WindowPositionX { get; set; }
        public float WindowPositionY { get; set; }
        public Dictionary<string, WindowPlacement> WindowPlacements { get; set; } = new Dictionary<string, WindowPlacement>();

        // アバター設定
        public int CurrentAvatarIndex { get; set; } = 0;
        public List<AvatarSettings> AvatarList { get; set; } = new List<AvatarSettings>();

        // アニメーション設定
        public int CurrentAnimationSettingIndex { get; set; } = 0;
        public List<AnimationSetting> AnimationSettings { get; set; } = new List<AnimationSetting>();

        // スクリーンショット設定
        public ScreenshotSettings ScreenshotSettings { get; set; } = new ScreenshotSettings();

        // マイク設定
        public MicrophoneSettings MicrophoneSettings { get; set; } = new MicrophoneSettings();

        // メッセージウィンドウ設定
        public MessageWindowSettings MessageWindowSettings { get; set; } = new MessageWindowSettings();

        public bool IsLoaded { get; set; } = false;
        public bool HasRemoteSettings { get; private set; }

        /// <summary>
        /// 検証済みの接続情報を一括保存する。
        /// </summary>
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
            var previousHost = OtomeKairoHost;
            var previousPort = OtomeKairoPort;

            ServerUrl = normalizedServerUrl;
            OtomeKairoBearerToken = consoleAccessToken.Trim();
            if (endpointChanged)
            {
                // 旧接続先から取得した通常設定を新接続先の実行状態として扱わない。
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
                OtomeKairoHost = previousHost;
                OtomeKairoPort = previousPort;
                throw;
            }
        }

        // コンストラクタはprivate（シングルトンパターン）
        private AppSettings()
        {
            UserDataDirectory = FindUserDataDirectory();

            // 接続情報だけをローカルから読み込む。通常設定は接続後にAPIから反映する。
            LoadSettings();
        }

        /// <summary>
        /// UserDataディレクトリを探索して見つける
        /// </summary>
        /// <returns>UserDataディレクトリのパス</returns>
        private string FindUserDataDirectory()
        {
            var baseDirectory = AppContext.BaseDirectory;

            // 探索するパスの配列
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

            // 見つからない場合は、最初のパスを使用してディレクトリを作成
            var defaultPath = Path.GetFullPath(searchPaths[0]);
            Debug.WriteLine($"UserDataディレクトリを作成しました: {defaultPath}");
            Directory.CreateDirectory(defaultPath);
            return defaultPath;
        }

        /// <summary>
        /// 現在の設定からConfigSettingsオブジェクトを作成
        /// </summary>
        /// <returns>ConfigSettings オブジェクト</returns>
        public ConfigSettings GetConfigSettings()
        {
            var snapshot = new ConfigSettings
            {
                CocoroConsolePort = CocoroConsolePort,
                otomeKairoPort = OtomeKairoPort,
                useExternalOtomeKairo = true,
                otomeKairoHost = OtomeKairoHost,
                cocoroShellPort = CocoroShellPort,
                clientId = ClientId,
                conversationDisplayName = ConversationDisplayName,
                otomeKairoBearerToken = OtomeKairoBearerToken,
                isUseLLM = IsUseLLM,
                isRestoreWindowPosition = IsRestoreWindowPosition,
                isTopmost = IsTopmost,
                isEscapeCursor = IsEscapeCursor,
                escapePositions = new List<EscapePosition>(EscapePositions),
                isInputVirtualKey = IsInputVirtualKey,
                virtualKeyString = VirtualKeyString,
                isAutoMove = IsAutoMove,
                showMessageWindow = ShowMessageWindow,
                isEnableAmbientOcclusion = IsEnableAmbientOcclusion,
                msaaLevel = MsaaLevel,
                avatarShadow = AvatarShadow,
                avatarShadowResolution = AvatarShadowResolution,
                backgroundShadow = BackgroundShadow,
                backgroundShadowResolution = BackgroundShadowResolution,
                windowSize = WindowSize,
                windowPositionX = WindowPositionX,
                windowPositionY = WindowPositionY,
                screenshotSettings = ScreenshotSettings,
                microphoneSettings = MicrophoneSettings,
                messageWindowSettings = MessageWindowSettings,
                windowPlacements = new Dictionary<string, WindowPlacement>(WindowPlacements),
                currentAvatarIndex = CurrentAvatarIndex,
                avatarList = new List<AvatarSettings>(AvatarList)
            };

            return snapshot.DeepCopy();
        }

        /// <summary>
        /// OtomeKairo 由来の設定から CocoroShell 専用の実行スナップショットを構築する。
        /// </summary>
        public ShellRuntimeConfig BuildShellRuntimeConfig()
        {
            if (!HasRemoteSettings)
            {
                throw new InvalidOperationException("OtomeKairoの端末設定を取得していません。");
            }

            var currentAvatar = GetCurrentAvatar()?.DeepCopy()
                ?? throw new InvalidOperationException("選択中のアバター設定がありません。");
            if (AnimationSettings.Count == 0)
            {
                throw new InvalidOperationException("アニメーション設定がありません。");
            }

            var selectedIndex = Math.Clamp(
                CurrentAnimationSettingIndex,
                0,
                AnimationSettings.Count - 1);
            var animationSettings = AnimationSettings.Select(animationSet => new AnimationSetting
            {
                animationSetId = animationSet.animationSetId,
                animeSetName = animationSet.animeSetName,
                postureChangeLoopCountStanding = animationSet.postureChangeLoopCountStanding,
                postureChangeLoopCountSittingFloor = animationSet.postureChangeLoopCountSittingFloor,
                animations = animationSet.animations.Select(animation => new AnimationConfig
                {
                    displayName = animation.displayName,
                    animationType = animation.animationType,
                    animationName = animation.animationName,
                    isEnabled = animation.isEnabled,
                }).ToList(),
            }).ToList();

            return new ShellRuntimeConfig
            {
                clientId = ClientId,
                shellApiPort = CocoroShellPort,
                display = new ShellDisplaySettings
                {
                    restoreWindowPosition = IsRestoreWindowPosition,
                    topmost = IsTopmost,
                    escapeCursor = IsEscapeCursor,
                    escapePositions = EscapePositions.Select(position => new EscapePosition
                    {
                        x = position.x,
                        y = position.y,
                        enabled = position.enabled,
                    }).ToList(),
                    touchVirtualKeyEnabled = IsInputVirtualKey,
                    virtualKey = VirtualKeyString,
                    autoMove = IsAutoMove,
                    showMessageWindow = ShowMessageWindow,
                    messageWindow = new MessageWindowSettings
                    {
                        maxMessageCount = MessageWindowSettings.maxMessageCount,
                        maxTotalAvatars = MessageWindowSettings.maxTotalAvatars,
                        minWindowSize = MessageWindowSettings.minWindowSize,
                        maxWindowSize = MessageWindowSettings.maxWindowSize,
                        fontSize = MessageWindowSettings.fontSize,
                        horizontalOffset = MessageWindowSettings.horizontalOffset,
                        verticalOffset = MessageWindowSettings.verticalOffset,
                    },
                    ambientOcclusionEnabled = IsEnableAmbientOcclusion,
                    avatarWindowSize = WindowSize,
                    avatarPositionX = WindowPositionX,
                    avatarPositionY = WindowPositionY,
                    msaaLevel = MsaaLevel,
                    avatarShadowMode = AvatarShadow,
                    avatarShadowResolution = AvatarShadowResolution,
                    backgroundShadowMode = BackgroundShadow,
                    backgroundShadowResolution = BackgroundShadowResolution,
                },
                avatar = new ShellAvatarSettings
                {
                    avatarId = currentAvatar.avatarId,
                    isReadOnly = currentAvatar.isReadOnly,
                    modelName = currentAvatar.modelName,
                    vrmFilePath = currentAvatar.vrmFilePath,
                    isConvertMToon = currentAvatar.isConvertMToon,
                    isEnableShadowOff = currentAvatar.isEnableShadowOff,
                    shadowOffMesh = currentAvatar.shadowOffMesh,
                },
                motion = new ShellMotionSettings
                {
                    selectedAnimationSetId = animationSettings[selectedIndex].animationSetId,
                    animationSettings = animationSettings,
                },
            };
        }

        /// <summary>
        /// OtomeKairo の端末設定・現在設定・音声設定を実行時モデルへ反映する。
        /// </summary>
        public void ApplyRemoteSettings(
            OtomeKairoConsoleClientSettings consoleSettings,
            OtomeKairoCurrentSettings currentSettings,
            OtomeKairoAvatarSpeechEditorState avatarSpeech)
        {
            var process = consoleSettings.Process;
            CocoroConsolePort = process.ConsoleApiPort;
            CocoroShellPort = process.CocoroShellPort;
            IsUseLLM = process.ConversationInputEnabled;
            ConversationDisplayName = currentSettings.ConversationDisplayName.Trim();

            var display = consoleSettings.Display;
            IsRestoreWindowPosition = display.RestoreWindowPosition;
            IsTopmost = display.Topmost;
            IsEscapeCursor = display.EscapeCursor;
            EscapePositions = display.EscapePositions.Select(position => new EscapePosition
            {
                x = position.X,
                y = position.Y,
                enabled = position.Enabled,
            }).ToList();
            IsInputVirtualKey = display.TouchVirtualKeyEnabled;
            VirtualKeyString = display.VirtualKey;
            IsAutoMove = display.AutoMove;
            ShowMessageWindow = display.ShowMessageWindow;
            IsEnableAmbientOcclusion = display.AmbientOcclusionEnabled;
            MsaaLevel = display.MsaaLevel;
            AvatarShadow = display.AvatarShadowMode;
            AvatarShadowResolution = display.AvatarShadowResolution;
            BackgroundShadow = display.BackgroundShadowMode;
            BackgroundShadowResolution = display.BackgroundShadowResolution;
            WindowSize = display.AvatarWindowSize;
            WindowPositionX = display.AvatarPositionX;
            WindowPositionY = display.AvatarPositionY;
            MessageWindowSettings = new MessageWindowSettings
            {
                maxMessageCount = display.MessageWindow.MaxMessageCount,
                maxTotalAvatars = display.MessageWindow.MaxTotalCharacters,
                minWindowSize = display.MessageWindow.MinWindowSize,
                maxWindowSize = display.MessageWindow.MaxWindowSize,
                fontSize = display.MessageWindow.FontSize,
                horizontalOffset = display.MessageWindow.HorizontalOffset,
                verticalOffset = display.MessageWindow.VerticalOffset,
            };
            WindowPlacements = display.WindowPlacements.ToDictionary(
                entry => entry.Key,
                entry => new WindowPlacement
                {
                    left = entry.Value.Left,
                    top = entry.Value.Top,
                });

            var desktop = consoleSettings.DesktopCapture;
            ScreenshotSettings = new ScreenshotSettings
            {
                enabled = desktop.Enabled,
                captureActiveWindowOnly = desktop.CaptureActiveWindowOnly,
                idleTimeoutMinutes = desktop.IdleTimeoutMinutes,
                excludePatterns = new List<string>(desktop.ExcludePatterns),
            };

            MicrophoneSettings = new MicrophoneSettings
            {
                inputSource = avatarSpeech.MicrophoneSettings.InputSource,
                localInputDevice = avatarSpeech.MicrophoneSettings.LocalInputDevice == null
                    ? null
                    : new MicrophoneInputDevice
                    {
                        hostApi = avatarSpeech.MicrophoneSettings.LocalInputDevice.HostApi,
                        name = avatarSpeech.MicrophoneSettings.LocalInputDevice.Name,
                    },
                console = avatarSpeech.MicrophoneSettings.Console == null
                    ? null
                    : new ConsoleMicrophoneSettings
                    {
                        clientId = avatarSpeech.MicrophoneSettings.Console.ClientId,
                        inputDevice = new ConsoleMicrophoneInputDevice
                        {
                            deviceId = avatarSpeech.MicrophoneSettings.Console.InputDevice.DeviceId,
                            name = avatarSpeech.MicrophoneSettings.Console.InputDevice.Name,
                        },
                    },
                vadProbabilityThreshold = avatarSpeech.MicrophoneSettings.VadProbabilityThreshold,
                speakerRecognitionThreshold = avatarSpeech.MicrophoneSettings.SpeakerRecognitionThreshold,
            };
            ApplyAvatarSettings(consoleSettings.AvatarPresentations, avatarSpeech);
            ApplyMotionSettings(consoleSettings.Motion);
            IsLoaded = true;
            HasRemoteSettings = true;
        }

        /// <summary>
        /// 現在の実行時モデルから端末設定bundleを構築する。
        /// </summary>
        public OtomeKairoConsoleClientSettings BuildConsoleClientSettings()
        {
            if (AnimationSettings.Count == 0)
            {
                throw new InvalidOperationException("アニメーション設定がありません。");
            }

            foreach (var animationSet in AnimationSettings)
            {
                if (string.IsNullOrWhiteSpace(animationSet.animationSetId))
                {
                    animationSet.animationSetId = $"animation_set:{Guid.NewGuid():N}";
                }
            }

            var selectedAnimationIndex = Math.Clamp(
                CurrentAnimationSettingIndex,
                0,
                AnimationSettings.Count - 1);

            return new OtomeKairoConsoleClientSettings
            {
                ClientId = ClientId,
                Process = new OtomeKairoConsoleProcessSettings
                {
                    ConsoleApiPort = CocoroConsolePort,
                    CocoroShellPort = CocoroShellPort,
                    ConversationInputEnabled = IsUseLLM,
                },
                Display = new OtomeKairoConsoleDisplaySettings
                {
                    RestoreWindowPosition = IsRestoreWindowPosition,
                    Topmost = IsTopmost,
                    EscapeCursor = IsEscapeCursor,
                    EscapePositions = EscapePositions.Select(position => new OtomeKairoConsoleEscapePosition
                    {
                        X = position.x,
                        Y = position.y,
                        Enabled = position.enabled,
                    }).ToList(),
                    TouchVirtualKeyEnabled = IsInputVirtualKey,
                    VirtualKey = VirtualKeyString,
                    AutoMove = IsAutoMove,
                    ShowMessageWindow = ShowMessageWindow,
                    AmbientOcclusionEnabled = IsEnableAmbientOcclusion,
                    MsaaLevel = MsaaLevel,
                    AvatarShadowMode = AvatarShadow,
                    AvatarShadowResolution = AvatarShadowResolution,
                    BackgroundShadowMode = BackgroundShadow,
                    BackgroundShadowResolution = BackgroundShadowResolution,
                    AvatarWindowSize = WindowSize,
                    AvatarPositionX = WindowPositionX,
                    AvatarPositionY = WindowPositionY,
                    MessageWindow = new OtomeKairoConsoleMessageWindowSettings
                    {
                        MaxMessageCount = MessageWindowSettings.maxMessageCount,
                        MaxTotalCharacters = MessageWindowSettings.maxTotalAvatars,
                        MinWindowSize = MessageWindowSettings.minWindowSize,
                        MaxWindowSize = MessageWindowSettings.maxWindowSize,
                        FontSize = MessageWindowSettings.fontSize,
                        HorizontalOffset = MessageWindowSettings.horizontalOffset,
                        VerticalOffset = MessageWindowSettings.verticalOffset,
                    },
                    WindowPlacements = WindowPlacements.ToDictionary(
                        entry => entry.Key,
                        entry => new OtomeKairoConsoleWindowPlacement
                        {
                            Left = entry.Value.left,
                            Top = entry.Value.top,
                        }),
                },
                DesktopCapture = new OtomeKairoConsoleDesktopCaptureSettings
                {
                    Enabled = ScreenshotSettings.enabled,
                    CaptureActiveWindowOnly = ScreenshotSettings.captureActiveWindowOnly,
                    IdleTimeoutMinutes = ScreenshotSettings.idleTimeoutMinutes,
                    ExcludePatterns = new List<string>(ScreenshotSettings.excludePatterns),
                },
                AvatarPresentations = BuildAvatarPresentations(),
                Motion = new OtomeKairoConsoleMotionSettings
                {
                    SelectedAnimationSetId = AnimationSettings[selectedAnimationIndex].animationSetId,
                    AnimationSets = AnimationSettings.Select(animationSet => new OtomeKairoConsoleAnimationSet
                    {
                        AnimationSetId = animationSet.animationSetId,
                        DisplayName = animationSet.animeSetName,
                        PostureChangeLoopCountStanding = animationSet.postureChangeLoopCountStanding,
                        PostureChangeLoopCountSittingFloor = animationSet.postureChangeLoopCountSittingFloor,
                        Animations = animationSet.animations.Select(animation => new OtomeKairoConsoleAnimation
                        {
                            DisplayName = animation.displayName,
                            AnimationType = animation.animationType,
                            AnimationName = animation.animationName,
                            Enabled = animation.isEnabled,
                        }).ToList(),
                    }).ToList(),
                },
            };
        }

        /// <summary>
        /// 現在の実行時モデルからアバター音声設定bundleを構築する。
        /// </summary>
        public OtomeKairoAvatarSpeechEditorState BuildAvatarSpeechEditorState()
        {
            if (AvatarList.Count == 0)
            {
                throw new InvalidOperationException("アバター設定がありません。");
            }

            foreach (var avatar in AvatarList)
            {
                if (string.IsNullOrWhiteSpace(avatar.avatarId))
                {
                    avatar.avatarId = $"avatar:{Guid.NewGuid():N}";
                }
            }

            var selectedAvatarIndex = Math.Clamp(CurrentAvatarIndex, 0, AvatarList.Count - 1);
            return new OtomeKairoAvatarSpeechEditorState
            {
                SelectedAvatarId = AvatarList[selectedAvatarIndex].avatarId,
                MicrophoneSettings = new OtomeKairoMicrophoneSettings
                {
                    InputSource = MicrophoneSettings.inputSource,
                    LocalInputDevice = MicrophoneSettings.localInputDevice == null
                        ? null
                        : new OtomeKairoSelectedAudioInputDevice
                        {
                            HostApi = MicrophoneSettings.localInputDevice.hostApi,
                            Name = MicrophoneSettings.localInputDevice.name,
                        },
                    Console = MicrophoneSettings.console == null
                        ? null
                        : new OtomeKairoConsoleMicrophoneSettings
                        {
                            ClientId = MicrophoneSettings.console.clientId,
                            InputDevice = new OtomeKairoConsoleMicrophoneInputDevice
                            {
                                DeviceId = MicrophoneSettings.console.inputDevice.deviceId,
                                Name = MicrophoneSettings.console.inputDevice.name,
                            },
                        },
                    VadProbabilityThreshold = MicrophoneSettings.vadProbabilityThreshold,
                    SpeakerRecognitionThreshold = MicrophoneSettings.speakerRecognitionThreshold,
                },
                Avatars = AvatarList.Select(BuildAvatarSpeechDefinition).ToList(),
            };
        }

        private void ApplyAvatarSettings(
            IReadOnlyCollection<OtomeKairoConsoleAvatarPresentation> presentations,
            OtomeKairoAvatarSpeechEditorState avatarSpeech)
        {
            var presentationsByAvatarId = presentations.ToDictionary(item => item.AvatarId);
            AvatarList = avatarSpeech.Avatars.Select(definition =>
            {
                presentationsByAvatarId.TryGetValue(definition.AvatarId, out var presentation);
                return BuildRuntimeAvatar(definition, presentation);
            }).ToList();
            CurrentAvatarIndex = Math.Max(
                0,
                AvatarList.FindIndex(avatar => avatar.avatarId == avatarSpeech.SelectedAvatarId));
        }

        private static AvatarSettings BuildRuntimeAvatar(
            OtomeKairoAvatarSpeechDefinition definition,
            OtomeKairoConsoleAvatarPresentation? presentation)
        {
            var model = presentation?.Model ?? string.Empty;
            var voicevox = definition.Tts.VoicevoxConfig;
            var styleBert = definition.Tts.StyleBertVits2Config;
            var aivis = definition.Tts.AivisCloudConfig;
            return new AvatarSettings
            {
                avatarId = definition.AvatarId,
                modelName = definition.DisplayName,
                isReadOnly = string.Equals(model, BuiltInModel, StringComparison.OrdinalIgnoreCase),
                vrmFilePath = model,
                isConvertMToon = presentation?.ConvertUnlitToMtoon ?? false,
                isEnableShadowOff = presentation?.ShadowExclusionEnabled ?? false,
                shadowOffMesh = string.Join(",", presentation?.ShadowExcludedMeshNames ?? new List<string>()),
                isUseSTT = definition.Stt.Enabled,
                sttEngine = definition.Stt.Engine,
                sttWakeWords = new List<string>(definition.Stt.WakeWords),
                sttProfileId = definition.Stt.ProfileId,
                sttApiKey = definition.Stt.ApiKey,
                isUseTTS = definition.Tts.Enabled,
                ttsType = definition.Tts.Engine,
                voicevoxConfig = new VoicevoxConfig
                {
                    endpointUrl = voicevox.EndpointUrl,
                    speakerId = voicevox.SpeakerId,
                    speedScale = voicevox.SpeedScale,
                    pitchScale = voicevox.PitchScale,
                    intonationScale = voicevox.IntonationScale,
                    volumeScale = voicevox.VolumeScale,
                    prePhonemeLength = voicevox.PrePhonemeLength,
                    postPhonemeLength = voicevox.PostPhonemeLength,
                    outputSamplingRate = voicevox.OutputSamplingRate,
                    outputStereo = voicevox.OutputStereo,
                },
                styleBertVits2Config = new StyleBertVits2Config
                {
                    endpointUrl = styleBert.EndpointUrl,
                    modelName = styleBert.ModelName,
                    modelId = styleBert.ModelId,
                    speakerName = styleBert.SpeakerName,
                    speakerId = styleBert.SpeakerId,
                    style = styleBert.Style,
                    styleWeight = styleBert.StyleWeight,
                    sdpRatio = styleBert.SdpRatio,
                    noise = styleBert.Noise,
                    noiseW = styleBert.NoiseW,
                    length = styleBert.Length,
                    language = styleBert.Language,
                    autoSplit = styleBert.AutoSplit,
                    splitInterval = styleBert.SplitInterval,
                    assistText = styleBert.AssistText,
                    assistTextWeight = styleBert.AssistTextWeight,
                    referenceAudioPath = styleBert.ReferenceAudioPath,
                },
                aivisCloudConfig = new AivisCloudConfig
                {
                    apiKey = aivis.ApiKey,
                    endpointUrl = aivis.EndpointUrl,
                    modelUuid = aivis.ModelUuid,
                    speakerUuid = aivis.SpeakerUuid,
                    styleId = aivis.StyleId,
                    styleName = aivis.StyleName,
                    useSSML = aivis.UseSsml,
                    language = aivis.Language,
                    speakingRate = aivis.SpeakingRate,
                    emotionalIntensity = aivis.EmotionalIntensity,
                    tempoDynamics = aivis.TempoDynamics,
                    pitch = aivis.Pitch,
                    volume = aivis.Volume,
                    outputFormat = aivis.OutputFormat,
                    outputBitrate = aivis.OutputBitrate,
                    outputSamplingRate = aivis.OutputSamplingRate,
                    outputAudioChannels = aivis.OutputAudioChannels,
                },
            };
        }

        private void ApplyMotionSettings(OtomeKairoConsoleMotionSettings motion)
        {
            AnimationSettings = motion.AnimationSets.Select(animationSet => new AnimationSetting
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
            }).ToList();
            CurrentAnimationSettingIndex = Math.Max(
                0,
                AnimationSettings.FindIndex(item => item.animationSetId == motion.SelectedAnimationSetId));
        }

        private List<OtomeKairoConsoleAvatarPresentation> BuildAvatarPresentations()
        {
            var presentations = new List<OtomeKairoConsoleAvatarPresentation>();
            foreach (var avatar in AvatarList)
            {
                if (string.IsNullOrWhiteSpace(avatar.vrmFilePath))
                {
                    continue;
                }

                presentations.Add(new OtomeKairoConsoleAvatarPresentation
                {
                    AvatarId = avatar.avatarId,
                    Model = avatar.vrmFilePath,
                    ConvertUnlitToMtoon = avatar.isConvertMToon,
                    ShadowExclusionEnabled = avatar.isEnableShadowOff,
                    ShadowExcludedMeshNames = avatar.shadowOffMesh
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(name => name.Trim())
                        .Where(name => name.Length > 0)
                        .ToList(),
                });
            }
            return presentations;
        }

        private static OtomeKairoAvatarSpeechDefinition BuildAvatarSpeechDefinition(AvatarSettings avatar)
        {
            return new OtomeKairoAvatarSpeechDefinition
            {
                AvatarId = avatar.avatarId,
                DisplayName = avatar.modelName,
                Stt = new OtomeKairoSttSettings
                {
                    Enabled = avatar.isUseSTT,
                    Engine = avatar.sttEngine,
                    WakeWords = new List<string>(avatar.sttWakeWords),
                    ProfileId = avatar.sttProfileId,
                    ApiKey = avatar.sttApiKey,
                },
                Tts = new OtomeKairoTtsSettings
                {
                    Enabled = avatar.isUseTTS,
                    Engine = avatar.ttsType,
                    VoicevoxConfig = new OtomeKairoVoicevoxSettings
                    {
                        EndpointUrl = avatar.voicevoxConfig.endpointUrl,
                        SpeakerId = avatar.voicevoxConfig.speakerId,
                        SpeedScale = avatar.voicevoxConfig.speedScale,
                        PitchScale = avatar.voicevoxConfig.pitchScale,
                        IntonationScale = avatar.voicevoxConfig.intonationScale,
                        VolumeScale = avatar.voicevoxConfig.volumeScale,
                        PrePhonemeLength = avatar.voicevoxConfig.prePhonemeLength,
                        PostPhonemeLength = avatar.voicevoxConfig.postPhonemeLength,
                        OutputSamplingRate = avatar.voicevoxConfig.outputSamplingRate,
                        OutputStereo = avatar.voicevoxConfig.outputStereo,
                    },
                    StyleBertVits2Config = new OtomeKairoStyleBertVits2Settings
                    {
                        EndpointUrl = avatar.styleBertVits2Config.endpointUrl,
                        ModelName = avatar.styleBertVits2Config.modelName,
                        ModelId = avatar.styleBertVits2Config.modelId,
                        SpeakerName = avatar.styleBertVits2Config.speakerName,
                        SpeakerId = avatar.styleBertVits2Config.speakerId,
                        Style = avatar.styleBertVits2Config.style,
                        StyleWeight = avatar.styleBertVits2Config.styleWeight,
                        SdpRatio = avatar.styleBertVits2Config.sdpRatio,
                        Noise = avatar.styleBertVits2Config.noise,
                        NoiseW = avatar.styleBertVits2Config.noiseW,
                        Length = avatar.styleBertVits2Config.length,
                        Language = avatar.styleBertVits2Config.language,
                        AutoSplit = avatar.styleBertVits2Config.autoSplit,
                        SplitInterval = avatar.styleBertVits2Config.splitInterval,
                        AssistText = avatar.styleBertVits2Config.assistText,
                        AssistTextWeight = avatar.styleBertVits2Config.assistTextWeight,
                        ReferenceAudioPath = avatar.styleBertVits2Config.referenceAudioPath,
                    },
                    AivisCloudConfig = new OtomeKairoAivisCloudSettings
                    {
                        ApiKey = avatar.aivisCloudConfig.apiKey,
                        EndpointUrl = avatar.aivisCloudConfig.endpointUrl,
                        ModelUuid = avatar.aivisCloudConfig.modelUuid,
                        SpeakerUuid = avatar.aivisCloudConfig.speakerUuid,
                        StyleId = avatar.aivisCloudConfig.styleId,
                        StyleName = avatar.aivisCloudConfig.styleName,
                        UseSsml = avatar.aivisCloudConfig.useSSML,
                        Language = avatar.aivisCloudConfig.language,
                        SpeakingRate = avatar.aivisCloudConfig.speakingRate,
                        EmotionalIntensity = avatar.aivisCloudConfig.emotionalIntensity,
                        TempoDynamics = avatar.aivisCloudConfig.tempoDynamics,
                        Pitch = avatar.aivisCloudConfig.pitch,
                        Volume = avatar.aivisCloudConfig.volume,
                        OutputFormat = avatar.aivisCloudConfig.outputFormat,
                        OutputBitrate = avatar.aivisCloudConfig.outputBitrate,
                        OutputSamplingRate = avatar.aivisCloudConfig.outputSamplingRate,
                        OutputAudioChannels = avatar.aivisCloudConfig.outputAudioChannels,
                    },
                },
            };
        }

        /// <summary>
        /// OtomeKairo の HTTPS ベースURLを返す。
        /// </summary>
        public string GetOtomeKairoBaseUrl()
        {
            return NormalizeServerUrl(ServerUrl);
        }

        /// <summary>
        /// OtomeKairo の WSS ベースURLを返す。
        /// </summary>
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

        /// <summary>
        /// ローカルの接続情報を読み込む。
        /// </summary>
        public void LoadSettings()
        {
            LoadAppSettings();
            IsLoaded = true;
        }

        /// <summary>
        /// Connection.json から接続情報を読み込む。
        /// </summary>
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
                }
                catch (Exception ex) when (
                    ex is JsonException ||
                    ex is InvalidOperationException)
                {
                    // 不正なローカル接続情報では通常機能を開始せず、起動時の専用画面で復旧する。
                    ServerUrl = string.Empty;
                    ClientId = string.Empty;
                    OtomeKairoBearerToken = string.Empty;
                    Debug.WriteLine($"Connection.jsonの読み込みに失敗しました: {ex.Message}");
                }
            }

            if (string.IsNullOrWhiteSpace(ClientId))
            {
                ClientId = $"console-{Guid.NewGuid()}";
            }

            try
            {
                ApplyConnectionUri();
                SaveConnectionSettings();
            }
            catch (InvalidOperationException)
            {
                OtomeKairoHost = string.Empty;
                OtomeKairoPort = 0;
            }
        }

        /// <summary>
        /// UserDataディレクトリの存在を確認し、必要なら作成する
        /// </summary>
        private void EnsureUserDataDirectoryExists()
        {
            string userDataDir = UserDataDirectory;

            if (!Directory.Exists(userDataDir))
            {
                Directory.CreateDirectory(userDataDir);
            }
        }

        /// <summary>
        /// 新しいアバターの編集用モデルを作成する。
        /// </summary>
        public AvatarSettings CreateAvatarFromDefaults(string modelName)
        {
            var source = GetCurrentAvatar()
                ?? throw new InvalidOperationException("複製元のアバター設定がありません。");
            var avatar = source.DeepCopy();
            avatar.avatarId = $"avatar:{Guid.NewGuid():N}";
            avatar.modelName = modelName;
            avatar.vrmFilePath = string.Empty;
            avatar.isReadOnly = false;
            return avatar;
        }

        /// <summary>
        /// 接続情報だけを Connection.json に保存する。
        /// </summary>
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

        /// <summary>
        /// ローカル接続情報を保存する。
        /// </summary>
        public void SaveSettings()
        {
            SaveAppSettings();
        }

        private void ApplyConnectionUri()
        {
            var uri = new Uri(NormalizeServerUrl(ServerUrl), UriKind.Absolute);
            OtomeKairoHost = uri.Host;
            OtomeKairoPort = uri.Port;
        }

        private void SaveConnectionSettings()
        {
            EnsureUserDataDirectoryExists();
            ServerUrl = NormalizeServerUrl(ServerUrl);
            ApplyConnectionUri();
            var connection = new ConnectionSettings
            {
                ServerUrl = ServerUrl,
                ClientId = ClientId.Trim(),
                ConsoleAccessToken = OtomeKairoBearerToken ?? string.Empty,
            };
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            File.WriteAllText(ConnectionSettingsFilePath, JsonSerializer.Serialize(connection, options));
            Debug.WriteLine($"接続情報を保存しました: {ConnectionSettingsFilePath}");
        }

        /// <summary>
        /// 現在選択されているアバター設定を取得
        /// </summary>
        /// <returns>現在のアバター設定、存在しない場合はnull</returns>
        public AvatarSettings? GetCurrentAvatar()
        {
            if (AvatarList == null || AvatarList.Count == 0)
                return null;

            if (CurrentAvatarIndex < 0 || CurrentAvatarIndex >= AvatarList.Count)
                return null;

            return AvatarList[CurrentAvatarIndex];
        }

        /// <summary>
        /// ウィンドウ位置を取得
        /// </summary>
        /// <param name="windowKey">ウィンドウ識別子</param>
        /// <returns>ウィンドウ位置。見つからない場合はnull</returns>
        public WindowPlacement? GetWindowPlacement(string windowKey)
        {
            if (string.IsNullOrWhiteSpace(windowKey))
            {
                return null;
            }

            if (WindowPlacements.TryGetValue(windowKey, out var placement))
            {
                return placement;
            }

            return null;
        }

        /// <summary>
        /// ウィンドウ位置を更新
        /// </summary>
        /// <param name="windowKey">ウィンドウ識別子</param>
        /// <param name="left">X座標</param>
        /// <param name="top">Y座標</param>
        public void SetWindowPlacement(string windowKey, double left, double top)
        {
            if (string.IsNullOrWhiteSpace(windowKey))
            {
                return;
            }

            WindowPlacements[windowKey] = new WindowPlacement
            {
                left = left,
                top = top
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
    }
}
