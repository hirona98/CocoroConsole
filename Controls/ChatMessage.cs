using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace CocoroConsole.Controls
{
    public enum ChatMessageKind
    {
        User,
        AI,
        System,
        Notification
    }

    /// <summary>
    /// チャットに表示する1件。履歴ファイルには保存しない。
    /// </summary>
    public sealed class ChatMessage : INotifyPropertyChanged
    {
        private string _text;
        private DateTime _timestamp;

        public ChatMessage(
            ChatMessageKind kind,
            string displayName,
            string text,
            DateTime timestamp,
            IReadOnlyList<BitmapSource>? images = null)
        {
            Kind = kind;
            DisplayName = displayName;
            _text = text;
            _timestamp = timestamp;
            Images = images ?? Array.Empty<BitmapSource>();
        }

        public ChatMessageKind Kind { get; }

        public string DisplayName { get; }

        public IReadOnlyList<BitmapSource> Images { get; }

        public string Text
        {
            get => _text;
            set
            {
                if (_text == value)
                {
                    return;
                }

                _text = value;
                OnPropertyChanged(nameof(Text));
                OnPropertyChanged(nameof(HasText));
            }
        }

        public bool HasText => _text.Length > 0;

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                if (_timestamp == value)
                {
                    return;
                }

                _timestamp = value;
                OnPropertyChanged(nameof(TimestampText));
            }
        }

        public string TimestampText => _timestamp.ToString("HH:mm");

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class ChatMessageTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? UserTemplate { get; set; }

        public DataTemplate? AiTemplate { get; set; }

        public DataTemplate? SystemTemplate { get; set; }

        public DataTemplate? NotificationTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is not ChatMessage message)
            {
                return null;
            }

            return message.Kind switch
            {
                ChatMessageKind.User => UserTemplate,
                ChatMessageKind.AI => AiTemplate,
                ChatMessageKind.System => SystemTemplate,
                ChatMessageKind.Notification => NotificationTemplate,
                _ => null
            };
        }
    }
}
