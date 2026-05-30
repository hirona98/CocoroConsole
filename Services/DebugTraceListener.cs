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
        private static readonly Regex BracketTokenPattern = new(@"^\[([^\]]+)\]", RegexOptions.Compiled);

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

            // [LEVEL] [Component] または [Component] の形式を検出する。
            var firstToken = BracketTokenPattern.Match(content);
            if (firstToken.Success)
            {
                var token = firstToken.Groups[1].Value;
                content = content.Substring(firstToken.Length).TrimStart();
                if (TryNormalizeLogLevel(token, out var parsedLevel))
                {
                    level = parsedLevel;
                    var componentToken = BracketTokenPattern.Match(content);
                    if (componentToken.Success)
                    {
                        component = componentToken.Groups[1].Value;
                        content = content.Substring(componentToken.Length).TrimStart();
                    }
                }
                else
                {
                    component = token;
                }
            }

            // エラーレベルを推測
            if (TryReadLeadingLevel(content, out var explicitLevel, out var strippedContent))
            {
                level = explicitLevel;
                content = strippedContent;
            }
            else if (level == "DEBUG" && (
                content.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("エラー") ||
                content.Contains("失敗")))
            {
                level = "ERROR";
            }
            else if (level == "DEBUG" && (
                content.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("警告")))
            {
                level = "WARNING";
            }

            return new LogMessage
            {
                timestamp = DateTime.UtcNow,
                level = level,
                component = component,
                message = content
            };
        }

        private static bool TryReadLeadingLevel(string content, out string level, out string strippedContent)
        {
            level = "";
            strippedContent = content;
            var separatorIndex = content.IndexOf(' ');
            var firstWord = separatorIndex >= 0 ? content[..separatorIndex] : content;
            if (!TryNormalizeLogLevel(firstWord.TrimEnd(':', '：'), out level))
            {
                return false;
            }

            strippedContent = separatorIndex >= 0 ? content[(separatorIndex + 1)..].TrimStart() : "";
            return true;
        }

        private static bool TryNormalizeLogLevel(string value, out string level)
        {
            level = value.Trim().ToUpperInvariant() switch
            {
                "DEBUG" => "DEBUG",
                "INFO" => "INFO",
                "WARN" => "WARNING",
                "WARNING" => "WARNING",
                "ERROR" => "ERROR",
                _ => ""
            };
            return level.Length > 0;
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
