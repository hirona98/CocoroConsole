using CocoroConsole.Services;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace CocoroConsole.Utilities
{
    /// <summary>
    /// 現在のアバター設定に応じて CocoroShell の起動状態を調整する。
    /// </summary>
    public static class CocoroShellProcessManager
    {
        private const string ConsoleApiUrlEnvironmentVariable = "COCORO_CONSOLE_API_URL";
        private const string SessionTokenEnvironmentVariable = "COCORO_SHELL_SESSION_TOKEN";

        /// <summary>
        /// CocoroShellの起動単位認証に使う一時トークンを準備する。
        /// </summary>
        public static void PrepareSessionToken(IAppSettings appSettings, bool rotate)
        {
            ArgumentNullException.ThrowIfNull(appSettings);
            if (rotate || string.IsNullOrEmpty(appSettings.ShellSessionToken))
            {
                appSettings.ShellSessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            }
        }

        public static void Apply(IAppSettings appSettings, ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
            ArgumentNullException.ThrowIfNull(appSettings);

#if !DEBUG
            if (HasDisplayableAvatar(appSettings))
            {
                // Shell 再起動ごとに資格を更新し、永続ファイルとコマンドラインへ残さない。
                PrepareSessionToken(appSettings, true);
                var environmentVariables = new Dictionary<string, string>
                {
                    [ConsoleApiUrlEnvironmentVariable] =
                        $"http://127.0.0.1:{appSettings.CocoroConsolePort}",
                    [SessionTokenEnvironmentVariable] = appSettings.ShellSessionToken,
                };
                ProcessHelper.LaunchExternalApplication(
                    "CocoroShell.exe",
                    "CocoroShell",
                    operation,
                    true,
                    environmentVariables);
                return;
            }

            ProcessHelper.LaunchExternalApplication("CocoroShell.exe", "CocoroShell", ProcessOperation.Terminate, true);
            appSettings.ShellSessionToken = string.Empty;
#else
            _ = appSettings;
            _ = operation;
#endif
        }

        private static bool HasDisplayableAvatar(IAppSettings appSettings)
        {
            if (appSettings.AvatarList.Count == 0 ||
                appSettings.CurrentAvatarIndex < 0 ||
                appSettings.CurrentAvatarIndex >= appSettings.AvatarList.Count)
            {
                return false;
            }

            var currentAvatar = appSettings.AvatarList[appSettings.CurrentAvatarIndex];
            return !string.IsNullOrWhiteSpace(currentAvatar.vrmFilePath) || currentAvatar.isReadOnly == true;
        }
    }
}
