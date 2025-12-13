using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Utilities
{
    public static class ClipboardPasteOverride
    {
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
    }
}

