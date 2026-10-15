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
                // 旧Shellの終了要求には旧資格を使い、新processの起動直前に更新する。
                var environmentVariables = new Dictionary<string, string>
                {
                    [ConsoleApiUrlEnvironmentVariable] =
                        $"http://127.0.0.1:{appSettings.CocoroConsolePort}",
                };
                ProcessHelper.LaunchExternalApplication(
                    "CocoroShell.exe",
                    "CocoroShell",
                    operation,
                    true,
                    environmentVariables,
                    beforeStart: () =>
                    {
                        PrepareSessionToken(appSettings, true);
                        environmentVariables[SessionTokenEnvironmentVariable] = appSettings.ShellSessionToken;
                    });
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
            var presentation = appSettings.GetCurrentAvatarPresentation();
            return presentation != null && !string.IsNullOrWhiteSpace(presentation.Model);
        }
    }
}
