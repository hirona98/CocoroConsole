using CocoroConsole.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;

namespace CocoroConsole.Windows
{
    /// <summary>
    /// OtomeKairo 接続設定専用ウィンドウ。
    /// </summary>
    public partial class OtomeKairoSettingsWindow : Window
    {
        // 設定サービス
        private readonly IAppSettings _appSettings;

        // 通信サービス（設定反映後の再取得に利用）
        private readonly ICommunicationService? _communicationService;

        public bool IsClosed { get; private set; } = false;

        public OtomeKairoSettingsWindow(ICommunicationService? communicationService)
        {
            InitializeComponent();

            // 依存サービスの参照を保持
            _appSettings = AppSettings.Instance;
            _communicationService = communicationService;

            // 現在の設定をUIへ反映
            LoadSettingsToUi();
        }

        /// <summary>
        /// 設定値をUIへ反映する。
        /// </summary>
        private void LoadSettingsToUi()
        {
            // 接続設定を反映
            UseExternalOtomeKairoCheckBox.IsChecked = _appSettings.UseExternalOtomeKairo;
            OtomeKairoHostTextBox.Text = (_appSettings.OtomeKairoHost ?? string.Empty).Trim();

            // Access Token を反映する
            BearerTokenTextBox.Text = _appSettings.OtomeKairoBearerToken ?? string.Empty;

            // 接続方式に応じて入力欄の有効/無効を切り替え
            UpdateConnectionUiState();
        }

        /// <summary>
        /// 接続方式に応じてUI状態を更新する。
        /// </summary>
        private void UpdateConnectionUiState()
        {
            // 外部接続時のみホスト入力を許可
            var useExternal = UseExternalOtomeKairoCheckBox.IsChecked ?? false;
            OtomeKairoHostTextBox.IsEnabled = useExternal;
        }

        /// <summary>
        /// 画面上の設定を保存する。
        /// </summary>
        private async Task<bool> ApplySettingsAsync()
        {
            // 入力値を取得
            var host = (OtomeKairoHostTextBox.Text ?? string.Empty).Trim();
            var useExternal = UseExternalOtomeKairoCheckBox.IsChecked ?? false;
            var bearerToken = (BearerTokenTextBox.Text ?? string.Empty).Trim();

            // 接続先ホストは空欄不可
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show(
                    "接続先ホストを入力してください。",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                OtomeKairoHostTextBox.Focus();
                return false;
            }

            // エンドポイント変更有無を判定
            var currentHost = (_appSettings.OtomeKairoHost ?? string.Empty).Trim();
            var endpointChanged =
                !string.Equals(currentHost, host, StringComparison.OrdinalIgnoreCase) ||
                _appSettings.UseExternalOtomeKairo != useExternal;
            var bearerTokenChanged = !string.Equals(_appSettings.OtomeKairoBearerToken ?? string.Empty, bearerToken, StringComparison.Ordinal);

            // 変更が無い場合は保存を行わない
            if (!endpointChanged && !bearerTokenChanged)
            {
                return true;
            }

            // 設定値を保存
            _appSettings.OtomeKairoHost = host;
            _appSettings.UseExternalOtomeKairo = useExternal;
            _appSettings.OtomeKairoBearerToken = bearerToken;
            _appSettings.SaveAppSettings();

            // 通信サービスの接続初期化をやり直す
            if (_communicationService != null)
            {
                await _communicationService.RefreshOtomeKairoSettingsAsync();
            }

            return true;
        }

        /// <summary>
        /// OK ボタン押下時。
        /// </summary>
        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // 保存成功時のみ閉じる
            if (await ApplySettingsAsync())
            {
                Close();
            }
        }

        /// <summary>
        /// キャンセル ボタン押下時。
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // 変更を保存せず閉じる
            Close();
        }

        /// <summary>
        /// 適用 ボタン押下時。
        /// </summary>
        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // 変更を保存して画面は閉じない
            await ApplySettingsAsync();
        }

        /// <summary>
        /// 外部利用チェック変更時。
        /// </summary>
        private void UseExternalOtomeKairoCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // 接続方式に応じてホスト入力欄を切り替え
            UpdateConnectionUiState();
        }

        /// <summary>
        /// リンククリック時。
        /// </summary>
        private void OtomeKairoLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // 既定ブラウザでURLを開く
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"URLを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            IsClosed = true;
            base.OnClosed(e);
        }
    }
}
