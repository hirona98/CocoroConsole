using CocoroConsole.Services;
using System;

namespace CocoroConsole.Utilities
{
    /// <summary>
    /// 現在のアバター設定に応じて CocoroShell の起動状態を調整する。
    /// </summary>
    public static class CocoroShellProcessManager
    {
        public static void Apply(IAppSettings appSettings, ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
            ArgumentNullException.ThrowIfNull(appSettings);

#if !DEBUG
            if (HasDisplayableAvatar(appSettings))
            {
                ProcessHelper.LaunchExternalApplication("CocoroShell.exe", "CocoroShell", operation, true);
                return;
            }

            ProcessHelper.LaunchExternalApplication("CocoroShell.exe", "CocoroShell", ProcessOperation.Terminate, true);
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
