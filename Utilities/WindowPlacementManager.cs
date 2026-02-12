using CocoroConsole.Services;
using System.Windows;

namespace CocoroConsole.Utilities
{
    /// <summary>
    /// ウィンドウ位置の保存・復元を管理するヘルパー
    /// </summary>
    public static class WindowPlacementManager
    {
        /// <summary>
        /// ウィンドウ位置の保存イベントを接続し、保存済み位置があれば復元する
        /// </summary>
        /// <param name="window">対象ウィンドウ</param>
        /// <param name="windowKey">設定保存用の識別子</param>
        /// <param name="appSettings">設定サービス。未指定時はシングルトンを使用</param>
        /// <returns>保存済み位置を復元できた場合はtrue</returns>
        public static bool AttachAndRestore(Window window, string windowKey, IAppSettings? appSettings = null)
        {
            var settings = appSettings ?? AppSettings.Instance;

            // 先に復元を実行する。
            var restored = TryRestorePosition(window, windowKey, settings);

            // 移動時に最新位置を設定へ反映する。
            window.LocationChanged += (_, __) => SaveCurrentPosition(window, windowKey, settings);

            // クローズ時にも最終位置を設定へ反映する。
            window.Closing += (_, __) => SaveCurrentPosition(window, windowKey, settings);

            return restored;
        }

        /// <summary>
        /// 保存済み位置を復元する
        /// </summary>
        /// <param name="window">対象ウィンドウ</param>
        /// <param name="windowKey">設定保存用の識別子</param>
        /// <param name="appSettings">設定サービス</param>
        /// <returns>復元できた場合はtrue</returns>
        private static bool TryRestorePosition(Window window, string windowKey, IAppSettings appSettings)
        {
            // 保存済み位置が無い場合は復元しない。
            var placement = appSettings.GetWindowPlacement(windowKey);
            if (placement == null)
            {
                return false;
            }

            // 保存済み位置を適用する。
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = placement.left;
            window.Top = placement.top;
            return true;
        }

        /// <summary>
        /// 現在のウィンドウ位置を設定へ反映する
        /// </summary>
        /// <param name="window">対象ウィンドウ</param>
        /// <param name="windowKey">設定保存用の識別子</param>
        /// <param name="appSettings">設定サービス</param>
        private static void SaveCurrentPosition(Window window, string windowKey, IAppSettings appSettings)
        {
            // 無効な座標は保存しない。
            if (double.IsNaN(window.Left) || double.IsNaN(window.Top))
            {
                return;
            }

            // 現在位置をメモリ上の設定へ保存する。
            appSettings.SetWindowPlacement(windowKey, window.Left, window.Top);
        }
    }
}
