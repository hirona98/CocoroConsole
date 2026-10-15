using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Linq;
using System.Collections.Specialized;
using System.Diagnostics;
using CocoroConsole.Windows;
using CocoroConsole.Services;
using CocoroConsole.Utilities;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace CocoroConsole.Controls
{
    /// <summary>
    /// チャットコントロール（バブルデザイン）
    /// </summary>
    public partial class ChatControl : UserControl
    {
        public event EventHandler<string>? MessageSent;

        // 添付画像データ（Base64形式のdata URL、API仕様に合わせて最大1枚）
        private List<string> _attachedImageDataUrls = new List<string>();
        private List<BitmapSource> _attachedImageSources = new List<BitmapSource>();
        private const int MaxImageCount = 1;
        private const float MinimumMicrophoneDbfs = -50.0f;
        private static readonly Brush WaitingMicrophoneLevelBrush =
            new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
        private static readonly Brush SpeakingMicrophoneLevelBrush =
            new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        private double _normalizedMicrophoneLevel;

        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();

        private readonly TimeSpan _continuousMessageThreshold = TimeSpan.FromSeconds(10);

        public ChatControl()
        {
            InitializeComponent();

            // ペーストイベントハンドラを追加
            DataObject.AddPastingHandler(MessageTextBox, OnPaste);
        }

        /// <summary>
        /// メッセージ入力テキストボックスにフォーカスを設定
        /// </summary>
        public void FocusMessageTextBox()
        {
            MessageTextBox.Focus();
        }

        /// <summary>
        /// ユーザーメッセージを送信
        /// </summary>
        private void SendMessage()
        {
            // --- 送信ボタン無効時は送信しない（Enter送信の抜け道を塞ぐ） ---
            if (SendButton != null && !SendButton.IsEnabled)
            {
                return;
            }

            string message = MessageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(message) && _attachedImageSources.Count == 0)
                return;

            // メッセージ送信イベント発火（UIへの追加はMainWindowで行う）
            MessageSent?.Invoke(this, message);

            // テキストボックスをクリア
            MessageTextBox.Clear();
        }

        /// <summary>
        /// ユーザーメッセージをUIに追加
        /// </summary>
        public void AddUserMessage(
            string displayName,
            string message,
            List<BitmapSource>? imageSources = null)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("「あなたの名前」が未設定です。", nameof(displayName));
            }

            Messages.Add(new ChatMessage(
                ChatMessageKind.User,
                displayName.Trim(),
                message,
                DateTime.Now,
                imageSources?.ToList()));
            ScrollChatToEnd();
        }

        /// <summary>
        /// AIレスポンスをUIに追加
        /// </summary>
        public void AddAiMessage(string displayName, string message)
        {
            AddAiMessage(displayName, message, forceNewBubble: false);
        }

        /// <summary>
        /// AIレスポンスをUIに追加
        /// </summary>
        /// <param name="forceNewBubble">直前がAIでも新しい吹き出しを強制するか</param>
        public void AddAiMessage(string displayName, string message, bool forceNewBubble)
        {
            var normalizedDisplayName = RequireAiDisplayName(displayName);
            var cleanMessage = RemoveFaceTags(message);
            if (!forceNewBubble && TryGetContinuousAiMessage(normalizedDisplayName) is ChatMessage continuous)
            {
                AppendToMessage(continuous, cleanMessage);
                return;
            }

            Messages.Add(new ChatMessage(
                ChatMessageKind.AI,
                normalizedDisplayName,
                cleanMessage,
                DateTime.Now));
            ScrollChatToEnd();
        }

        /// <summary>
        /// AIレスポンスを画像付きでUIに追加
        /// </summary>
        public void AddAiMessage(string displayName, string message, string? imageBase64)
        {
            var normalizedDisplayName = RequireAiDisplayName(displayName);
            BitmapSource? image = null;
            var text = string.Empty;
            if (!string.IsNullOrEmpty(imageBase64))
            {
                try
                {
                    image = DecodeBase64Image(imageBase64);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"画像表示エラー: {ex.Message}");
                    text = "[画像を表示できませんでした]";
                }
            }

            if (!string.IsNullOrEmpty(message))
            {
                var cleanMessage = RemoveFaceTags(message);
                text = text.Length == 0 ? cleanMessage : text + Environment.NewLine + cleanMessage;
            }

            Messages.Add(new ChatMessage(
                ChatMessageKind.AI,
                normalizedDisplayName,
                text,
                DateTime.Now,
                image == null ? null : new List<BitmapSource> { image }));
            ScrollChatToEnd();
        }

        /// <summary>
        /// 最後のAIメッセージにテキストを追記
        /// </summary>
        public void AppendToLastAiMessage(string additionalText)
        {
            if (Messages.Count == 0 || Messages[Messages.Count - 1].Kind != ChatMessageKind.AI)
            {
                return;
            }

            AppendToMessage(Messages[Messages.Count - 1], RemoveFaceTags(additionalText));
        }

        public void UpdateStreamingAiMessage(string displayName, string content)
        {
            var normalizedDisplayName = displayName.Trim();
            if (Messages.Count == 0 ||
                Messages[Messages.Count - 1].Kind != ChatMessageKind.AI ||
                !string.Equals(Messages[Messages.Count - 1].DisplayName, normalizedDisplayName, StringComparison.Ordinal))
            {
                AddAiMessage(displayName, content, forceNewBubble: true);
                return;
            }

            Messages[Messages.Count - 1].Text = RemoveFaceTags(content);
            ScrollChatToEnd();
        }

        /// <summary>
        /// システムエラーメッセージをUIに追加（中央のグレー枠に表示）
        /// </summary>
        public void AddSystemErrorMessage(string message)
        {
            Messages.Add(new ChatMessage(
                ChatMessageKind.System,
                string.Empty,
                message,
                DateTime.Now));
            ScrollChatToEnd();
        }

        /// <summary>
        /// 通知メッセージをUIに追加（中央のグレー枠に白文字で表示）
        /// </summary>
        public void AddNotificationMessage(string from, string message, List<BitmapSource>? imageSources = null)
        {
            Messages.Add(new ChatMessage(
                ChatMessageKind.Notification,
                string.Empty,
                $"[{from}]\n{message}",
                DateTime.Now,
                imageSources?.ToList()));
            ScrollChatToEnd();
        }

        /// <summary>
        /// 視覚キャプチャ画像を表示
        /// </summary>
        public void AddVisualCaptureImage(string imageBase64)
        {
            try
            {
                var image = DecodeBase64Image(imageBase64);
                Messages.Add(new ChatMessage(
                    ChatMessageKind.System,
                    string.Empty,
                    "[視覚キャプチャ画像]",
                    DateTime.Now,
                    new List<BitmapSource> { image }));
                ScrollChatToEnd();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"デスクトップモニタリング画像の表示エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// チャット履歴をクリア
        /// </summary>
        public void ClearChat()
        {
            Messages.Clear();
        }

        private void AppendToMessage(ChatMessage message, string additionalText)
        {
            message.Text += additionalText;
            message.Timestamp = DateTime.Now;
            ScrollChatToEnd();
        }

        private ChatMessage? TryGetContinuousAiMessage(string displayName)
        {
            if (Messages.Count == 0)
            {
                return null;
            }

            var last = Messages[Messages.Count - 1];
            if (last.Kind != ChatMessageKind.AI)
            {
                return null;
            }

            if (!string.Equals(last.DisplayName, displayName, StringComparison.Ordinal))
            {
                return null;
            }

            if (DateTime.Now - last.Timestamp > _continuousMessageThreshold)
            {
                return null;
            }

            return last;
        }

        private static string RequireAiDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("人格設定のプリセット名が未設定です。", nameof(displayName));
            }

            return displayName.Trim();
        }

        /// <summary>
        /// 表情タグを削除
        /// </summary>
        private static string RemoveFaceTags(string message)
        {
            return Regex.Replace(message, @"\[face:[^\]]+\]", "").Trim();
        }

        private static BitmapSource DecodeBase64Image(string imageBase64)
        {
            var imageBytes = Convert.FromBase64String(imageBase64);
            using (var stream = new MemoryStream(imageBytes))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = stream;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }

        private void ScrollChatToEnd()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                ChatScrollViewer.ScrollToEnd();
            }));
        }

        /// <summary>
        /// 送信ボタンクリックハンドラ
        /// </summary>
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        /// <summary>
        /// テキストボックスのキー入力ハンドラ（Enterキーで送信、Shift+Enterで改行）
        /// </summary>
        private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Shift+Enterの場合は改行を挿入
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    // デフォルト動作（改行挿入）を許可
                    return;
                }
                // Enterのみの場合はメッセージ送信
                else
                {
                    e.Handled = true;
                    SendMessage();
                }
            }
            // Ctrl+Vの場合は画像ペーストを処理
            else if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (Clipboard.ContainsImage())
                {
                    var image = Clipboard.GetImage();
                    if (image != null)
                    {
                        AddImageFromBitmapSource(image);
                        e.Handled = true;
                    }
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files.Count > 0)
                    {
                        foreach (var filePath in files)
                        {
                            if (!string.IsNullOrEmpty(filePath))
                            {
                                AddImageFromFile(filePath);
                            }
                        }
                        e.Handled = true;
                    }
                }
            }
        }

        /// <summary>
        /// グリッド全体のドラッグエンターイベントハンドラ
        /// </summary>
        private void Grid_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        /// <summary>
        /// グリッド全体のドラッグオーバーイベントハンドラ
        /// </summary>
        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        /// <summary>
        /// グリッド全体のドラッグリーブイベントハンドラ
        /// </summary>
        private void Grid_DragLeave(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        /// <summary>
        /// グリッド全体のドロップイベントハンドラ
        /// </summary>
        private void Grid_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    foreach (string filePath in files)
                    {
                        AddImageFromFile(filePath);
                    }
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                var image = e.Data.GetData(DataFormats.Bitmap) as BitmapSource;
                if (image != null)
                {
                    AddImageFromBitmapSource(image);
                }
            }
            e.Handled = true;
        }

        /// <summary>
        /// TextBoxのPreviewDragEnterイベントハンドラ
        /// </summary>
        private void TextBox_PreviewDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        /// <summary>
        /// TextBoxのPreviewDragOverイベントハンドラ
        /// </summary>
        private void TextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        /// <summary>
        /// TextBoxのPreviewDropイベントハンドラ
        /// </summary>
        private void TextBox_PreviewDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string filePath in files)
                {
                    AddImageFromFile(filePath);
                }
                e.Handled = true;
            }
            else if (e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                var image = e.Data.GetData(DataFormats.Bitmap) as BitmapSource;
                if (image != null)
                {
                    AddImageFromBitmapSource(image);
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// ペーストイベントハンドラ
        /// </summary>
        private void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Bitmap))
            {
                var image = e.DataObject.GetData(DataFormats.Bitmap) as BitmapSource;
                if (image != null)
                {
                    AddImageFromBitmapSource(image);
                    e.CancelCommand();
                }
            }
            else if (e.DataObject.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.DataObject.GetData(DataFormats.FileDrop);
                foreach (string filePath in files)
                {
                    AddImageFromFile(filePath);
                }
                e.CancelCommand();
            }
        }

        /// <summary>
        /// ファイルから画像を追加
        /// </summary>
        private void AddImageFromFile(string filePath)
        {
            try
            {
                // 画像数の上限チェック
                if (_attachedImageSources.Count >= MaxImageCount)
                {
                    MessageBox.Show($"画像は最大{MaxImageCount}枚まで添付できます。", "制限", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // サポートされる画像形式を確認
                string[] supportedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
                string extension = Path.GetExtension(filePath).ToLower();

                if (!supportedExtensions.Contains(extension))
                {
                    MessageBox.Show("サポートされていない画像形式です。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var bitmap = new BitmapImage(new Uri(filePath));
                AddImageFromBitmapSource(bitmap);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"画像の読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// BitmapSourceから画像を追加
        /// </summary>
        private void AddImageFromBitmapSource(BitmapSource bitmapSource)
        {
            try
            {
                // 画像数の上限チェック
                if (_attachedImageSources.Count >= MaxImageCount)
                {
                    MessageBox.Show($"画像は最大{MaxImageCount}枚まで添付できます。", "制限", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 画像をBase64エンコード
                string imageDataUrl = ConvertToDataUrl(bitmapSource);

                // リストに追加
                _attachedImageDataUrls.Add(imageDataUrl);
                _attachedImageSources.Add(bitmapSource);

                // プレビューを更新
                UpdateImagePreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"画像の処理に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// BitmapSourceをBase64形式のdata URLに変換
        /// </summary>
        private string ConvertToDataUrl(BitmapSource bitmapSource)
        {
            using (var memoryStream = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                encoder.Save(memoryStream);

                byte[] imageBytes = memoryStream.ToArray();
                string base64String = Convert.ToBase64String(imageBytes);
                return $"data:image/png;base64,{base64String}";
            }
        }

        /// <summary>
        /// 画像プレビューを更新
        /// </summary>
        private void UpdateImagePreview()
        {
            // プレビューパネルをクリア
            ImagePreviewPanel.Children.Clear();

            if (_attachedImageSources.Count == 0)
            {
                // 画像がない場合はプレースホルダーを表示
                ImagePreviewBorder.Visibility = Visibility.Collapsed;
                ImagePlaceholderText.Visibility = Visibility.Visible;
                ImagePreviewScrollViewer.Visibility = Visibility.Collapsed;
            }
            else
            {
                // 添付画像をプレビューに表示
                for (int i = 0; i < _attachedImageSources.Count; i++)
                {
                    var imageSource = _attachedImageSources[i];
                    int imageIndex = i; // ラムダ式でキャプチャするためのローカル変数

                    // 各画像のコンテナ
                    var imageContainer = new Border
                    {
                        BorderBrush = new SolidColorBrush(Colors.Gray),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Margin = new Thickness(2),
                        Background = new SolidColorBrush(Colors.White),
                        Width = 80,
                        Height = 80
                    };

                    // グリッドで画像と削除ボタンを重ねる
                    var grid = new Grid();

                    // 画像要素
                    var image = new Image
                    {
                        Source = imageSource,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(2),
                        Cursor = Cursors.Hand
                    };

                    // 画像クリックで拡大表示
                    image.MouseLeftButtonUp += (s, e) =>
                    {
                        var previewWindow = new Windows.ImagePreviewWindow(imageSource);
                        previewWindow.Show();
                    };

                    // 個別削除ボタン
                    var deleteButton = new Button
                    {
                        Content = "×",
                        Width = 16,
                        Height = 16,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(2),
                        Background = new SolidColorBrush(Color.FromArgb(170, 160, 0, 0)),
                        Foreground = new SolidColorBrush(Colors.White),
                        BorderThickness = new Thickness(0),
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Cursor = Cursors.Hand
                    };

                    // 個別削除ボタンのクリックイベント
                    deleteButton.Click += (s, e) =>
                    {
                        RemoveImageBySource(imageSource);
                    };

                    grid.Children.Add(image);
                    grid.Children.Add(deleteButton);
                    imageContainer.Child = grid;

                    ImagePreviewPanel.Children.Add(imageContainer);
                }

                // プレビューエリアを表示
                ImagePlaceholderText.Visibility = Visibility.Collapsed;
                ImagePreviewScrollViewer.Visibility = Visibility.Visible;
                ImagePreviewBorder.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 指定したBitmapSourceの画像を削除
        /// </summary>
        private void RemoveImageBySource(BitmapSource imageSource)
        {
            int index = _attachedImageSources.IndexOf(imageSource);
            if (index >= 0)
            {
                _attachedImageDataUrls.RemoveAt(index);
                _attachedImageSources.RemoveAt(index);
                UpdateImagePreview();
            }
        }

        /// <summary>
        /// 添付画像データを取得してクリア
        /// </summary>
        public List<string> GetAndClearAttachedImages()
        {
            var imageDataUrls = new List<string>(_attachedImageDataUrls);
            if (_attachedImageDataUrls.Count > 0)
            {
                _attachedImageDataUrls.Clear();
                _attachedImageSources.Clear();
                UpdateImagePreview();
            }
            return imageDataUrls;
        }

        /// <summary>
        /// 添付画像のBitmapSourceリストを取得
        /// </summary>
        public List<BitmapSource> GetAttachedImageSources()
        {
            return new List<BitmapSource>(_attachedImageSources);
        }

        /// <summary>
        /// 実効入力元のマイク音量と発話判定を表示します。
        /// </summary>
        /// <param name="dbfs">現在の入力音量（dBFS）</param>
        /// <param name="speaking">VADが発話中と判定しているか</param>
        /// <param name="active">音声入力が利用可能か</param>
        public void UpdateMicrophoneLevel(float? dbfs, bool speaking, bool active)
        {
            var normalizedLevel = 0.0;
            if (active && dbfs.HasValue && float.IsFinite(dbfs.Value))
            {
                normalizedLevel = Math.Clamp(
                    (dbfs.Value - MinimumMicrophoneDbfs) / -MinimumMicrophoneDbfs,
                    0.0f,
                    1.0f);
            }

            _normalizedMicrophoneLevel = normalizedLevel;
            UpdateMicrophoneLevelHeight();
            VoiceLevelBar.Fill = active && speaking
                ? SpeakingMicrophoneLevelBrush
                : WaitingMicrophoneLevelBrush;
        }

        /// <summary>
        /// 入力欄の高さに合わせて、角丸を保ったまま音量バーの高さを更新します。
        /// </summary>
        private void UpdateMicrophoneLevelHeight()
        {
            VoiceLevelBar.Height = VoiceLevelBorder.ActualHeight * _normalizedMicrophoneLevel;
        }

        /// <summary>
        /// 入力欄の伸縮後も現在の音量比率を維持します。
        /// </summary>
        private void VoiceLevelBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateMicrophoneLevelHeight();
        }

        /// <summary>
        /// 送信ボタンの有効/無効を設定
        /// </summary>
        /// <param name="isEnabled">ボタンを有効にするかどうか</param>
        public void UpdateSendButtonEnabled(bool isEnabled)
        {
            SendButton.IsEnabled = isEnabled;
        }

        /// <summary>
        /// 吹き出しを右クリックしたとき、本文をコピーする。
        /// </summary>
        private async void Bubble_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ChatMessage message)
            {
                return;
            }

            try
            {
                e.Handled = true;

                if (string.IsNullOrWhiteSpace(message.Text))
                {
                    ShowTransientTooltip(element, "コピーするテキストがありません");
                    return;
                }

                var copyResult = await ClipboardPasteOverride.SetTextAsync(message.Text);
                if (copyResult == ClipboardPasteOverride.CopyResult.Copied)
                {
                    ShowTransientTooltip(element, "コピーしました");
                }
                else if (copyResult == ClipboardPasteOverride.CopyResult.Failed)
                {
                    ShowTransientTooltip(element, "コピーに失敗しました");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Clipboard copy failed: {ex}");
                ShowTransientTooltip(element, "コピーに失敗しました");
            }
        }

        private void ChatImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            BitmapSource? source = null;
            if (sender is Image image && image.Source is BitmapSource imageSource)
            {
                source = imageSource;
            }
            else if (sender is Border border && border.Child is Image child && child.Source is BitmapSource childSource)
            {
                source = childSource;
            }

            if (source == null)
            {
                return;
            }

            e.Handled = true;
            var previewWindow = new ImagePreviewWindow(source);
            previewWindow.Show();
        }

        /// <summary>
        /// 一時的なツールチップをマウス位置付近に表示
        /// </summary>
        private void ShowTransientTooltip(UIElement target, string message, int milliseconds = 1500)
        {
            var tooltip = new ToolTip
            {
                Content = message,
                PlacementTarget = target,
                Placement = PlacementMode.Mouse,
                StaysOpen = true,
                Background = new SolidColorBrush(Color.FromArgb(240, 50, 50, 50)),
                Foreground = Brushes.White,
                Padding = new Thickness(8, 4, 8, 4)
            };

            tooltip.IsOpen = true;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                tooltip.IsOpen = false;
                tooltip.PlacementTarget = null;
            };
            timer.Start();
        }
    }
}
