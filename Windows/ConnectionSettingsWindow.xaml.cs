using CocoroConsole.Services;
using System;
using System.Windows;
using System.Windows.Media;

namespace CocoroConsole.Windows
{
    /// <summary>
    /// OtomeKairoへ接続する前段だけを扱うローカル設定画面。
    /// </summary>
    public partial class ConnectionSettingsWindow : Window
    {
        private readonly OtomeKairoConnectionBootstrapper _bootstrapper = new();
        private readonly string _currentServerUrl;
        private readonly string _currentAccessToken;

        public ConnectionSettingsWindow(
            string currentServerUrl,
            string currentAccessToken,
            string? initialErrorMessage = null,
            bool exitsApplicationOnClose = false)
        {
            InitializeComponent();
            _currentServerUrl = currentServerUrl;
            _currentAccessToken = currentAccessToken;
            ServerUrlTextBox.Text = currentServerUrl;
            StatusText.Text = initialErrorMessage ?? string.Empty;
            CancelButton.Content = exitsApplicationOnClose ? "終了" : "キャンセル";
            Loaded += (_, _) =>
            {
                ServerUrlTextBox.Focus();
                ServerUrlTextBox.SelectAll();
            };
        }

        internal OtomeKairoConnectionResult? ConnectionResult { get; private set; }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true);
            StatusText.Foreground = Brushes.DimGray;
            StatusText.Text = "OtomeKairoへ接続しています...";

            try
            {
                ConnectionResult = await _bootstrapper.ConnectAsync(
                    ServerUrlTextBox.Text,
                    _currentServerUrl,
                    _currentAccessToken);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                StatusText.Foreground = Brushes.Firebrick;
                StatusText.Text = ex is OperationCanceledException
                    ? "OtomeKairoへの接続を中断しました。"
                    : ex.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool isBusy)
        {
            ServerUrlTextBox.IsEnabled = !isBusy;
            ConnectButton.IsEnabled = !isBusy;
            CancelButton.IsEnabled = !isBusy;
        }
    }
}
