using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Utilities
{
    public static class ClipboardPasteOverride
    {
        public enum CopyResult
        {
            Copied,
            AlreadyPending,
            Failed
        }

        private static readonly SemaphoreSlim ClipboardWriteLock = new SemaphoreSlim(1, 1);
        private static readonly object ActiveCopyLock = new object();
        private static Task<bool>? _activeCopyTask;
        private static string? _activeCopyText;

        public static void CopyToClipboard(TextBox source)
        {
            if (source == null)
            {
                return;
            }

            _ = SetTextAsync(source.Text);
        }

        public static void CopyToClipboard(PasswordBox source)
        {
            if (source == null)
            {
                return;
            }

            _ = SetTextAsync(source.Password);
        }

        public static void PasteOverwrite(TextBox target)
        {
            if (target == null)
            {
                return;
            }

            if (TryGetTrimmedText(out string text))
            {
                target.Text = text;
            }
        }

        public static void PasteOverwrite(PasswordBox target)
        {
            if (target == null)
            {
                return;
            }

            if (TryGetTrimmedText(out string text))
            {
                target.Password = text;
            }
        }

        private static bool TryGetTrimmedText(out string text)
        {
            text = string.Empty;

            try
            {
                if (!Clipboard.ContainsText())
                {
                    return false;
                }

                text = Clipboard.GetText()?.Trim() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(text);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Clipboard paste failed: {ex}");
                return false;
            }
        }

        public static async Task<bool> TrySetTextAsync(string? text)
        {
            return await SetTextAsync(text).ConfigureAwait(true) != CopyResult.Failed;
        }

        public static Task<CopyResult> SetTextAsync(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Task.FromResult(CopyResult.Failed);
            }

            lock (ActiveCopyLock)
            {
                if (_activeCopyTask != null
                    && !_activeCopyTask.IsCompleted
                    && string.Equals(_activeCopyText, text, StringComparison.Ordinal))
                {
                    return Task.FromResult(CopyResult.AlreadyPending);
                }

                var copyTask = TrySetTextCoreAsync(text);
                _activeCopyTask = copyTask;
                _activeCopyText = text;
                _ = ClearActiveCopyAsync(copyTask);
                return ToCopyResultAsync(copyTask);
            }
        }

        private static async Task<CopyResult> ToCopyResultAsync(Task<bool> copyTask)
        {
            return await copyTask.ConfigureAwait(true) ? CopyResult.Copied : CopyResult.Failed;
        }

        private static async Task ClearActiveCopyAsync(Task<bool> copyTask)
        {
            try
            {
                await copyTask.ConfigureAwait(false);
            }
            finally
            {
                lock (ActiveCopyLock)
                {
                    if (ReferenceEquals(_activeCopyTask, copyTask))
                    {
                        _activeCopyTask = null;
                        _activeCopyText = null;
                    }
                }
            }
        }

        private static async Task<bool> TrySetTextCoreAsync(string text)
        {
            const int clipboardCannotOpenHResult = unchecked((int)0x800401D0);
            const int maxAttempts = 8;
            await ClipboardWriteLock.WaitAsync().ConfigureAwait(true);
            try
            {
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        Clipboard.SetText(text);
                        return true;
                    }
                    catch (COMException ex) when (ex.ErrorCode == clipboardCannotOpenHResult && attempt < maxAttempts)
                    {
                        await Task.Delay(25).ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Clipboard copy failed: {ex}");
                        return false;
                    }
                }

                Debug.WriteLine("Clipboard copy failed: OpenClipboard に失敗しました");
                return false;
            }
            finally
            {
                ClipboardWriteLock.Release();
            }
        }
    }
}
