using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CocoroConsole.Communication;

namespace CocoroConsole.Services
{
    /// <summary>
    /// Debug.WriteLineの出力をキャプチャしてLogMessageとして通知するTraceListener
    /// </summary>
    public class DebugTraceListener : TraceListener
    {
        private readonly StringBuilder _buffer = new();
        private static readonly Regex ComponentPattern = new(@"^\[([^\]]+)\]", RegexOptions.Compiled);

        public event EventHandler<LogMessage>? LogMessageReceived;

        public override void Write(string? message)
        {
            if (message != null)
            {
                _buffer.Append(message);
            }
        }

        public override void WriteLine(string? message)
        {
            if (message != null)
            {
                _buffer.Append(message);
            }

            var fullMessage = _buffer.ToString();
            _buffer.Clear();

            if (string.IsNullOrWhiteSpace(fullMessage))
            {
                return;
            }

            var logMessage = ParseMessage(fullMessage);
            LogMessageReceived?.Invoke(this, logMessage);
        }

        private static LogMessage ParseMessage(string message)
        {
            var component = "CocoroConsole";
            var level = "DEBUG";
            var content = message;

            // [ComponentName] パターンを検出
            var match = ComponentPattern.Match(message);
            if (match.Success)
            {
                component = match.Groups[1].Value;
                content = message.Substring(match.Length).TrimStart();
            }

            // エラーレベルを推測
            if (content.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("エラー") ||
                content.Contains("失敗"))
            {
                level = "ERROR";
            }
            else if (content.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase) ||
                     content.Contains("警告"))
            {
                level = "WARNING";
            }
            else if (content.StartsWith("INFO", StringComparison.OrdinalIgnoreCase))
            {
                level = "INFO";
            }

            return new LogMessage
            {
                timestamp = DateTime.UtcNow,
                level = level,
                component = component,
                message = content
            };
        }

        /// <summary>
        /// TraceListenerをDebug出力に登録する
        /// </summary>
        public static DebugTraceListener Register()
        {
            var listener = new DebugTraceListener();
            Trace.Listeners.Add(listener);
            return listener;
        }

        /// <summary>
        /// TraceListenerの登録を解除する
        /// </summary>
        public void Unregister()
        {
            Trace.Listeners.Remove(this);
        }
    }
}
