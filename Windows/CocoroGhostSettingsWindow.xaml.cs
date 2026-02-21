using CocoroConsole.Services;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;

namespace CocoroConsole.Windows
{
    /// <summary>
    /// CocoroGhost 接続設定専用ウィンドウ。
    /// </summary>
    public partial class CocoroGhostSettingsWindow : Window
    {
        // 設定サービス
        private readonly IAppSettings _appSettings;

        // 通信サービス（設定反映後の再取得に利用）
        private readonly ICommunicationService? _communicationService;

        public bool IsClosed { get; private set; } = false;

        public CocoroGhostSettingsWindow(ICommunicationService? communicationService)
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
            UseExternalCocoroGhostCheckBox.IsChecked = _appSettings.UseExternalCocoroGhost;
            CocoroGhostHostTextBox.Text = (_appSettings.CocoroGhostHost ?? string.Empty).Trim();

            // Bearer Token を反映（読み取り専用）
            BearerTokenTextBox.Text = _appSettings.CocoroGhostBearerToken ?? string.Empty;

            // 接続方式に応じて入力欄の有効/無効を切り替え
            UpdateConnectionUiState();
        }

        /// <summary>
        /// 接続方式に応じてUI状態を更新する。
        /// </summary>
        private void UpdateConnectionUiState()
        {
            // 外部接続時のみホスト入力を許可
            var useExternal = UseExternalCocoroGhostCheckBox.IsChecked ?? false;
            CocoroGhostHostTextBox.IsEnabled = useExternal;
        }

        /// <summary>
        /// 画面上の設定を保存する。
        /// </summary>
        private async Task<bool> ApplySettingsAsync()
        {
            // 入力値を取得
            var host = (CocoroGhostHostTextBox.Text ?? string.Empty).Trim();
            var useExternal = UseExternalCocoroGhostCheckBox.IsChecked ?? false;

            // 接続先ホストは空欄不可
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show(
                    "接続先ホストを入力してください。",
                    "入力エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                CocoroGhostHostTextBox.Focus();
                return false;
            }

            // エンドポイント変更有無を判定
            var currentHost = (_appSettings.CocoroGhostHost ?? string.Empty).Trim();
            var endpointChanged =
                !string.Equals(currentHost, host, StringComparison.OrdinalIgnoreCase) ||
                _appSettings.UseExternalCocoroGhost != useExternal;

            // 変更が無い場合は保存を行わない
            if (!endpointChanged)
            {
                return true;
            }

            // 設定値を保存
            _appSettings.CocoroGhostHost = host;
            _appSettings.UseExternalCocoroGhost = useExternal;
            _appSettings.SaveAppSettings();

            // 通信サービスの表示キャッシュを再取得
            if (endpointChanged && _communicationService != null)
            {
                await _communicationService.RefreshCocoroGhostSettingsAsync();
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
        private void UseExternalCocoroGhostCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // 接続方式に応じてホスト入力欄を切り替え
            UpdateConnectionUiState();
        }

        /// <summary>
        /// リンククリック時。
        /// </summary>
        private void CocoroGhostLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
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
