using CocoroConsole.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    /// <summary>
    /// ExternalServicesSettingsControl.xaml の相互作用ロジック
    /// </summary>
    public partial class ExternalServicesSettingsControl : UserControl
    {
        public ExternalServicesSettingsControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                NotificationApiDetailsTextBox.Text = GetNotificationApiDetails();
                DirectRequestApiDetailsTextBox.Text = GetMetaRequestApiDetails();

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"外部サービス設定の初期化エラー: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 通知API詳細テキストを取得（エンドポイント/ボディ/レスポンス/使用例を含む）
        /// </summary>
        private string GetNotificationApiDetails()
        {
            var cocoroGhostPort = AppSettings.Instance.CocoroGhostPort;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("エンドポイント:");
            sb.AppendLine($"POST http://127.0.0.1:{cocoroGhostPort}/api/notification");
            sb.AppendLine();
            sb.AppendLine("ヘッダー:");
            sb.AppendLine("Authorization: Bearer <TOKEN>");
            sb.AppendLine("Content-Type: application/json; charset=utf-8");
            sb.AppendLine();
            sb.AppendLine("リクエストボディ (JSON):");
            sb.AppendLine("{");
            sb.AppendLine("  \"memory_id\": \"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx\",  // オプション（省略時は /api/settings の active_embedding_preset_id が使われる）");
            sb.AppendLine("  \"source_system\": \"gmail\",");
            sb.AppendLine("  \"title\": \"件名\",");
            sb.AppendLine("  \"body\": \"本文\",");
            sb.AppendLine("  \"images\": [  // オプション");
            sb.AppendLine("    {\"type\": \"external\", \"base64\": \"...\"}");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("レスポンス:");
            sb.AppendLine("HTTP/1.1 200 OK");
            sb.AppendLine("{ \"unit_id\": 23456 }");
            sb.AppendLine();
            sb.AppendLine("備考:");
            sb.AppendLine("- images[].base64 は data:image/...;base64, を除いた生のBase64文字列を指定してください");
            sb.AppendLine();
            sb.AppendLine("使用例 (cURL):");
            sb.AppendLine($"curl -X POST http://127.0.0.1:{cocoroGhostPort}/api/notification \\");
            sb.AppendLine("  -H \"Authorization: Bearer <TOKEN>\" \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine("  -d '{\"source_system\":\"gmail\",\"title\":\"件名\",\"body\":\"本文\"}'");
            sb.AppendLine();
            sb.AppendLine("使用例 (PowerShell):");
            sb.AppendLine("Invoke-RestMethod -Method Post `");
            sb.AppendLine($"  -Uri \"http://127.0.0.1:{cocoroGhostPort}/api/notification\" `");
            sb.AppendLine("  -Headers @{ Authorization = \"Bearer <TOKEN>\" } `");
            sb.AppendLine("  -ContentType \"application/json; charset=utf-8\" `");
            sb.AppendLine("  -Body '{\"source_system\":\"gmail\",\"title\":\"件名\",\"body\":\"本文\"}'");
            return sb.ToString();
        }

        /// <summary>
        /// メタ要求API詳細テキストを取得（エンドポイント/ボディ/レスポンス/使用例を含む）
        /// </summary>
        private string GetMetaRequestApiDetails()
        {
            var cocoroGhostPort = AppSettings.Instance.CocoroGhostPort;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("エンドポイント:");
            sb.AppendLine($"POST http://127.0.0.1:{cocoroGhostPort}/api/meta_request");
            sb.AppendLine();
            sb.AppendLine("ヘッダー:");
            sb.AppendLine("Authorization: Bearer <TOKEN>");
            sb.AppendLine("Content-Type: application/json; charset=utf-8");
            sb.AppendLine();
            sb.AppendLine("リクエストボディ (JSON):");
            sb.AppendLine("{");
            sb.AppendLine("  \"memory_id\": \"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx\",  // オプション（省略時は /api/settings の active_embedding_preset_id が使われる）");
            sb.AppendLine("  \"instruction\": \"任意の指示文\",");
            sb.AppendLine("  \"payload_text\": \"任意のプロンプトやメッセージ\",");
            sb.AppendLine("  \"images\": [  // オプション");
            sb.AppendLine("    {\"type\": \"external\", \"base64\": \"...\"}");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("レスポンス:");
            sb.AppendLine("HTTP/1.1 200 OK");
            sb.AppendLine("{ \"unit_id\": 34567 }");
            sb.AppendLine();
            sb.AppendLine("備考:");
            sb.AppendLine("- images[].base64 は data:image/...;base64, を除いた生のBase64文字列を指定してください");
            sb.AppendLine();
            sb.AppendLine("使用例 (cURL):");
            sb.AppendLine($"curl -X POST http://127.0.0.1:{cocoroGhostPort}/api/meta_request \\");
            sb.AppendLine("  -H \"Authorization: Bearer <TOKEN>\" \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine("  -d '{\"instruction\":\"要約して感想も述べて\",\"payload_text\":\"～ニュース内容～\"}'");
            sb.AppendLine();
            sb.AppendLine("使用例 (PowerShell):");
            sb.AppendLine("Invoke-RestMethod -Method Post `");
            sb.AppendLine($"  -Uri \"http://127.0.0.1:{cocoroGhostPort}/api/meta_request\" `");
            sb.AppendLine("  -Headers @{ Authorization = \"Bearer <TOKEN>\" } `");
            sb.AppendLine("  -ContentType \"application/json; charset=utf-8\" `");
            sb.AppendLine("  -Body '{\"instruction\":\"要約して感想も述べて\",\"payload_text\":\"～ニュース内容～\"}'");
            return sb.ToString();
        }
    }
}
