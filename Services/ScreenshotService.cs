using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

namespace CocoroAI.Services
{
    /// <summary>
    /// スクリーンショット取得がスキップされた理由
    /// </summary>
    public enum ScreenshotSkipReason
    {
        /// <summary>
        /// スキップしていない（取得成功、または未実行）
        /// </summary>
        None = 0,

        /// <summary>
        /// ユーザーがアイドル状態のためスキップ
        /// </summary>
        Idle = 1,

        /// <summary>
        /// 除外パターンにマッチしたためスキップ
        /// </summary>
        ExcludedWindowTitle = 2
    }

    /// <summary>
    /// スクリーンショット取得サービス
    /// </summary>
    public class ScreenshotService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

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

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        private System.Threading.Timer? _captureTimer;
        private readonly int _intervalMilliseconds;
        private readonly Func<ScreenshotData, Task>? _onCaptured;
        private readonly Func<string, Task>? _onSkipped;
        private bool _isDisposed;
        private int _idleTimeoutMinutes = 10; // DefaultSetting.json と合わせて 10 分
        private List<Regex>? _compiledExcludePatterns;


        public bool IsRunning { get; private set; }
        public bool CaptureActiveWindowOnly { get; set; }
        public int IntervalMinutes => _intervalMilliseconds / 60000;

        /// <summary>
        /// 直近のキャプチャでスキップした理由（スキップしていない場合は None）
        /// </summary>
        public ScreenshotSkipReason LastSkipReason { get; private set; } = ScreenshotSkipReason.None;

        public int IdleTimeoutMinutes
        {
            get => _idleTimeoutMinutes;
            set => _idleTimeoutMinutes = value >= 0 ? value : 10;
        }

        public ScreenshotService(int intervalMinutes = 10, Func<ScreenshotData, Task>? onCaptured = null, Func<string, Task>? onSkipped = null)
        {
            _intervalMilliseconds = intervalMinutes * 60 * 1000;
            _onCaptured = onCaptured;
            _onSkipped = onSkipped;
            CaptureActiveWindowOnly = true;
        }

        /// <summary>
        /// 除外パターンを設定
        /// </summary>
        /// <param name="patterns">正規表現パターンのリスト</param>
        public void SetExcludePatterns(IEnumerable<string> patterns)
        {
            if (patterns == null || !patterns.Any())
            {
                _compiledExcludePatterns = null;
                return;
            }

            try
            {
                _compiledExcludePatterns = patterns
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(pattern =>
                    {
                        try
                        {
                            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        }
                        catch (ArgumentException ex)
                        {
                            Debug.WriteLine($"無効な正規表現パターンをスキップ: {pattern} - {ex.Message}");
                            return null;
                        }
                    })
                    .Where(regex => regex != null)
                    .Cast<Regex>()
                    .ToList();

                Debug.WriteLine($"除外パターンを設定: {_compiledExcludePatterns.Count}個");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"除外パターン設定エラー: {ex.Message}");
                _compiledExcludePatterns = null;
            }
        }

        /// <summary>
        /// ウィンドウタイトルが除外パターンにマッチするかを判定（最初にマッチしたRegexを返す）
        /// </summary>
        /// <param name="windowTitle">ウィンドウタイトル</param>
        private Regex? FindMatchedExcludePattern(string windowTitle)
        {
            if (_compiledExcludePatterns == null || _compiledExcludePatterns.Count == 0)
                return null;

            if (string.IsNullOrWhiteSpace(windowTitle))
                return null;

            try
            {
                foreach (var regex in _compiledExcludePatterns)
                {
                    if (regex.IsMatch(windowTitle))
                    {
                        Debug.WriteLine($"除外パターンマッチ: '{windowTitle}' - パターン: {regex}");
                        return regex;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フィルタリング判定エラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// スクリーンショットの定期取得を開始
        /// </summary>
        public void Start(int? initialDelayMilliseconds = null)
        {
            if (IsRunning) return;

            IsRunning = true;
            var dueTime = (initialDelayMilliseconds.HasValue && initialDelayMilliseconds.Value > 0)
                ? initialDelayMilliseconds.Value
                : _intervalMilliseconds;
            _captureTimer = new System.Threading.Timer(async _ => await CaptureTimerCallback(), null, dueTime, _intervalMilliseconds);
        }

        /// <summary>
        /// スクリーンショットの定期取得を停止
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            _captureTimer?.Dispose();
            _captureTimer = null;
        }

        /// <summary>
        /// アクティブウィンドウのスクリーンショットを取得
        /// </summary>
        /// <returns>除外パターンにマッチした場合はnull（スキップ扱い）</returns>
        public async Task<ScreenshotData?> CaptureActiveWindowAsync()
        {
            return await Task.Run<ScreenshotData?>(() =>
            {
                // 直近の結果を初期化
                LastSkipReason = ScreenshotSkipReason.None;

                // アイドルチェック（0 は無効）
                if (IsUserIdle())
                {
                    LastSkipReason = ScreenshotSkipReason.Idle;
                    return null;
                }

                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    throw new InvalidOperationException("アクティブウィンドウが見つかりません");
                }

                // ウィンドウタイトルを取得
                var titleLength = GetWindowTextLength(hwnd);
                var titleBuilder = new System.Text.StringBuilder(titleLength + 1);
                GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
                var windowTitle = titleBuilder.ToString();

                // フィルタリング判定
                var matched = FindMatchedExcludePattern(windowTitle);
                if (matched != null)
                {
                    // フィルタ一致は例外ではなく通常フローとして扱う（呼び出し側でスキップ処理）
                    LastSkipReason = ScreenshotSkipReason.ExcludedWindowTitle;
                    return null;
                }

                // ウィンドウの位置とサイズを取得
                if (!GetWindowRect(hwnd, out RECT rect))
                {
                    throw new InvalidOperationException("ウィンドウの情報を取得できません");
                }

                // スクリーンショットを撮影
                using var bitmap = new Bitmap(rect.Width, rect.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        rect.Left,
                        rect.Top,
                        0,
                        0,
                        new Size(rect.Width, rect.Height),
                        CopyPixelOperation.SourceCopy
                    );
                }

                // Base64エンコード
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                var imageBytes = ms.ToArray();
                var base64String = Convert.ToBase64String(imageBytes);

                return new ScreenshotData
                {
                    ImageBase64 = base64String,
                    WindowTitle = windowTitle,
                    CaptureTime = DateTime.Now,
                    IsActiveWindow = true,
                    Width = rect.Width,
                    Height = rect.Height
                };
            });
        }

        /// <summary>
        /// 全画面のスクリーンショットを取得
        /// </summary>
        public async Task<ScreenshotData> CaptureFullScreenAsync()
        {
            return await Task.Run(() =>
            {
                var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

                using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        bounds.X,
                        bounds.Y,
                        0,
                        0,
                        bounds.Size,
                        CopyPixelOperation.SourceCopy
                    );
                }

                // Base64エンコード
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                var imageBytes = ms.ToArray();
                var base64String = Convert.ToBase64String(imageBytes);

                return new ScreenshotData
                {
                    ImageBase64 = base64String,
                    WindowTitle = "全画面",
                    CaptureTime = DateTime.Now,
                    IsActiveWindow = false,
                    Width = bounds.Width,
                    Height = bounds.Height
                };
            });
        }

        private async Task CaptureTimerCallback()
        {
            try
            {
                // アイドル時間をチェック
                if (IsUserIdle())
                {
                    LastSkipReason = ScreenshotSkipReason.Idle;
                    Debug.WriteLine($"ユーザーがアイドル状態（{_idleTimeoutMinutes}分以上操作なし）のため、スクリーンショットをスキップします");

                    // アイドルスキップも「スキップ」として通知したい場合があるため、コールバックを呼ぶ
                    if (_onSkipped != null)
                    {
                        await _onSkipped($"ユーザーがアイドル状態（{_idleTimeoutMinutes}分以上操作なし）のため、スクリーンショットをスキップしました");
                    }
                    return;
                }

                ScreenshotData screenshot;
                if (CaptureActiveWindowOnly)
                {
                    var captured = await CaptureActiveWindowAsync();
                    if (captured == null)
                    {
                        // 除外パターンでスキップされた場合
                        if (_onSkipped != null)
                        {
                            await _onSkipped("除外パターンにマッチしたため画面キャプチャをスキップしました");
                        }
                        return;
                    }

                    screenshot = captured;
                }
                else
                {
                    screenshot = await CaptureFullScreenAsync();
                }

                // コールバックを実行
                if (_onCaptured != null)
                {
                    await _onCaptured(screenshot);
                }
            }
            catch (Exception ex)
            {
                // その他のエラー
                System.Diagnostics.Debug.WriteLine($"スクリーンショット取得エラー: {ex.Message}");
            }
        }



        /// <summary>
        /// ユーザーがアイドル状態かどうかを判定
        /// </summary>
        private bool IsUserIdle()
        {
            try
            {
                // 0 は無効（常にアイドルではない）
                if (_idleTimeoutMinutes <= 0)
                {
                    return false;
                }

                var lastInputInfo = new LASTINPUTINFO();
                lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);

                if (GetLastInputInfo(ref lastInputInfo))
                {
                    // 最終入力からの経過時間を計算（ミリ秒）
                    var idleTime = Environment.TickCount - lastInputInfo.dwTime;

                    // アイドルタイムアウト時間（ミリ秒）と比較
                    var idleTimeoutMs = _idleTimeoutMinutes * 60 * 1000;

                    return idleTime > idleTimeoutMs;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アイドル時間の取得エラー: {ex.Message}");
            }

            // エラーが発生した場合はアイドルとみなす
            return true;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            Stop();

            _isDisposed = true;
        }
    }

    /// <summary>
    /// スクリーンショットデータ
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
