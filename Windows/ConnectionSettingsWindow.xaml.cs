using CocoroConsole.Services;
using System;
using System.Linq;
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
            PopulateServerUrlHistory(currentServerUrl);
            StatusText.Text = initialErrorMessage ?? string.Empty;
            CancelButton.Content = exitsApplicationOnClose ? "終了" : "キャンセル";
            Loaded += (_, _) =>
            {
                ServerUrlComboBox.Focus();
            };
        }

        internal OtomeKairoConnectionResult? ConnectionResult { get; private set; }

        private void PopulateServerUrlHistory(string currentServerUrl)
        {
            var history = AppSettings.Instance.ServerUrlHistory
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!string.IsNullOrWhiteSpace(currentServerUrl) &&
                !history.Any(url => string.Equals(
                    url,
                    currentServerUrl.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
            {
                history.Insert(0, currentServerUrl.Trim());
            }

            ServerUrlComboBox.ItemsSource = history;
            ServerUrlComboBox.Text = currentServerUrl ?? string.Empty;
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true);
            StatusText.Foreground = Brushes.DimGray;
            StatusText.Text = "OtomeKairoへ接続しています...";

            try
            {
                ConnectionResult = await _bootstrapper.ConnectAsync(
                    ServerUrlComboBox.Text,
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
            ServerUrlComboBox.IsEnabled = !isBusy;
            ConnectButton.IsEnabled = !isBusy;
            CancelButton.IsEnabled = !isBusy;
        }
    }
}
