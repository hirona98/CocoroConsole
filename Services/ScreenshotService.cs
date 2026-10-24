using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CocoroConsole.Models.OtomeKairoApi;

namespace CocoroAI.Services
{
    /// <summary>
    /// スクリーンショット取得がスキップされた理由。
    /// </summary>
    public enum ScreenshotSkipReason
    {
        None = 0,
        Idle = 1,
        ExcludedWindowTitle = 2,
        InvalidWindowBounds = 3
    }

    /// <summary>
    /// OtomeKairo の要求に含まれる撮影方針で画面を取得する。
    /// </summary>
    public class ScreenshotService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public ScreenshotSkipReason LastSkipReason { get; private set; }

        public Task<ScreenshotData?> CaptureAsync(
            OtomeKairoDesktopCaptureSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var captureTarget = settings.CaptureTarget;
            if (captureTarget != "active_window" && captureTarget != "primary_screen")
            {
                throw new ArgumentException("取得範囲が不正です。", nameof(settings));
            }
            var idleTimeoutMinutes = settings.IdleTimeoutMinutes;
            if (idleTimeoutMinutes < 0 || settings.ExcludePatterns == null || settings.ExcludePatterns.Any(pattern => pattern == null))
            {
                throw new ArgumentException("撮影方針が不正です。", nameof(settings));
            }
            // 正規表現の不正や実行タイムアウトは呼び出し側へ失敗として返す。
            var excludePatterns = settings.ExcludePatterns
                .Select(pattern => new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                .ToArray();

            return Task.Run<ScreenshotData?>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastSkipReason = ScreenshotSkipReason.None;
                if (IsUserIdle(idleTimeoutMinutes))
                {
                    LastSkipReason = ScreenshotSkipReason.Idle;
                    return null;
                }

                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    throw new InvalidOperationException("アクティブウィンドウが見つかりません。");
                }
                var titleBuilder = new StringBuilder(GetWindowTextLength(hwnd) + 1);
                GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
                var windowTitle = titleBuilder.ToString();
                if (excludePatterns.Any(pattern => pattern.IsMatch(windowTitle)))
                {
                    LastSkipReason = ScreenshotSkipReason.ExcludedWindowTitle;
                    return null;
                }

                var isActiveWindow = captureTarget == "active_window";
                Rectangle bounds;
                if (isActiveWindow)
                {
                    if (!GetWindowRect(hwnd, out var rect))
                    {
                        throw new InvalidOperationException("ウィンドウの情報を取得できません。");
                    }
                    bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                    if (bounds.Width <= 0 || bounds.Height <= 0)
                    {
                        LastSkipReason = ScreenshotSkipReason.InvalidWindowBounds;
                        return null;
                    }
                }
                else
                {
                    var screen = Screen.PrimaryScreen
                        ?? throw new InvalidOperationException("プライマリモニターを取得できません。");
                    bounds = screen.Bounds;
                    if (bounds.Width <= 0 || bounds.Height <= 0)
                    {
                        throw new InvalidOperationException("プライマリモニターの範囲が不正です。");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                }
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = new MemoryStream();
                bitmap.Save(stream, ImageFormat.Png);
                return new ScreenshotData
                {
                    ImageBase64 = Convert.ToBase64String(stream.ToArray()),
                    WindowTitle = isActiveWindow ? windowTitle : "プライマリモニター全体",
                    CaptureTime = DateTime.Now,
                    IsActiveWindow = isActiveWindow,
                    Width = bounds.Width,
                    Height = bounds.Height
                };
            }, cancellationToken);
        }

        private static bool IsUserIdle(int timeoutMinutes)
        {
            if (timeoutMinutes == 0)
            {
                return false;
            }
            var lastInputInfo = new LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>()
            };
            if (!GetLastInputInfo(ref lastInputInfo))
            {
                throw new InvalidOperationException("最終入力時刻を取得できません。");
            }
            var idleMilliseconds = unchecked((uint)Environment.TickCount - lastInputInfo.dwTime);
            return idleMilliseconds > (long)timeoutMinutes * 60 * 1000;
        }
    }

    /// <summary>
    /// スクリーンショットデータ。
    /// </summary>
    public class ScreenshotData
    {
        public string ImageBase64 { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public DateTime CaptureTime { get; set; }
        public bool IsActiveWindow { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
