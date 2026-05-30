using CocoroConsole.Services;
using System;

namespace CocoroConsole.Utilities
{
    /// <summary>
    /// 現在のキャラクター設定に応じて CocoroShell の起動状態を調整する。
    /// </summary>
    public static class CocoroShellProcessManager
    {
        public static void Apply(IAppSettings appSettings, ProcessOperation operation = ProcessOperation.RestartIfRunning)
        {
            ArgumentNullException.ThrowIfNull(appSettings);

#if !DEBUG
            if (HasDisplayableCharacter(appSettings))
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

        private static bool HasDisplayableCharacter(IAppSettings appSettings)
        {
            if (appSettings.CharacterList.Count == 0 ||
                appSettings.CurrentCharacterIndex < 0 ||
                appSettings.CurrentCharacterIndex >= appSettings.CharacterList.Count)
            {
                return false;
            }

            var currentCharacter = appSettings.CharacterList[appSettings.CurrentCharacterIndex];
            return !string.IsNullOrWhiteSpace(currentCharacter.vrmFilePath) || currentCharacter.isReadOnly == true;
        }
    }
}
