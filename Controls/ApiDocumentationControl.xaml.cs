using CocoroConsole.Services;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CocoroConsole.Controls
{
    public partial class ApiDocumentationControl : UserControl
    {
        public ApiDocumentationControl()
        {
            InitializeComponent();
        }

        public async Task InitializeAsync()
        {
            try
            {
                NotificationApiTextBox.Text = GetNotificationApiDetails();
                MetaRequestApiTextBox.Text = GetMetaRequestApiDetails();

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"API説明の初期化エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GetNotificationApiDetails()
        {
            var port = AppSettings.Instance.CocoroGhostPort;
            var sb = new StringBuilder();
            sb.AppendLine("用途:");
            sb.AppendLine("- 外部プログラムから通知依頼を送る");
            sb.AppendLine("- 通知は /api/events/stream で接続中クライアントへ配信される（ブロードキャスト）");
            sb.AppendLine();

            sb.AppendLine("エンドポイント:");
            sb.AppendLine($"- POST http://127.0.0.1:{port}/api/v2/notification");
            sb.AppendLine();

            sb.AppendLine("認証:");
            sb.AppendLine("- Authorization: Bearer <TOKEN>");
            sb.AppendLine();

            sb.AppendLine("リクエストボディ (JSON):");
            sb.AppendLine("{");
            sb.AppendLine("  \"source_system\": \"アプリ名\",");
            sb.AppendLine("  \"text\": \"通知メッセージ\",");
            sb.AppendLine("  \"images\": [  // オプション（最大5枚）");
            sb.AppendLine("    \"data:image/jpeg;base64,/9j/4AAQ...\",  // 1枚目");
            sb.AppendLine("    \"data:image/png;base64,iVBORw0KGgo...\"  // 2枚目");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine("レスポンス:");
            sb.AppendLine("- HTTP/1.1 204 No Content");
            sb.AppendLine();

            sb.AppendLine("使用例 (cURL):");
            sb.AppendLine("# 1枚の画像を送る場合");
            sb.AppendLine($"curl.exe -X POST http://127.0.0.1:{port}/api/v2/notification \\");
            sb.AppendLine("  -H \"Authorization: Bearer cocoro_token\" \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine("  -d '{\"source_system\":\"MyApp\",\"text\":\"処理完了\",\"images\":[\"data:image/jpeg;base64,...\"]}'");
            sb.AppendLine();
            sb.AppendLine("# 複数枚の画像を送る場合");
            sb.AppendLine($"curl.exe -X POST http://127.0.0.1:{port}/api/v2/notification \\");
            sb.AppendLine("  -H \"Authorization: Bearer cocoro_token\" \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine("  -d '{\"source_system\":\"MyApp\",\"text\":\"結果\",\"images\":[\"data:image/jpeg;base64,...\",\"data:image/png;base64,...\"]}'");
            sb.AppendLine();

            sb.AppendLine("使用例 (PowerShell):");
            sb.AppendLine("# 複数枚の画像を送る場合");
            sb.AppendLine("Invoke-RestMethod -Method Post `");
            sb.AppendLine($"  -Uri \"http://127.0.0.1:{port}/api/v2/notification\" `");
            sb.AppendLine("  -Headers @{ Authorization = \"Bearer cocoro_token\" } `");
            sb.AppendLine("  -ContentType \"application/json; charset=utf-8\" `");
            sb.AppendLine("  -Body '{\"source_system\":\"MyApp\",\"text\":\"結果\",\"images\":[\"data:image/jpeg;base64,...\",\"data:image/png;base64,...\"]}'");
            return sb.ToString();
        }

        private static string GetMetaRequestApiDetails()
        {
            var port = AppSettings.Instance.CocoroGhostPort;
            var sb = new StringBuilder();
            sb.AppendLine("用途:");
            sb.AppendLine("- 外部プログラムからメタ要求(指示 + テキスト)を送る");
            sb.AppendLine();

            sb.AppendLine("エンドポイント:");
            sb.AppendLine($"- POST http://127.0.0.1:{port}/api/v2/meta-request");
            sb.AppendLine();

            sb.AppendLine("認証:");
            sb.AppendLine("- Authorization: Bearer <TOKEN>");
            sb.AppendLine();

            sb.AppendLine("リクエストボディ (JSON):");
            sb.AppendLine("{");
            sb.AppendLine("  \"instruction\": \"任意の指示\",");
            sb.AppendLine("  \"payload_text\": \"補足情報（任意）\",");
            sb.AppendLine("  \"images\": [  // オプション（最大5枚）");
            sb.AppendLine("    \"data:image/jpeg;base64,/9j/4AAQ...\",  // 1枚目");
            sb.AppendLine("    \"data:image/png;base64,iVBORw0KGgo...\"  // 2枚目");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine("レスポンス:");
            sb.AppendLine("- HTTP/1.1 204 No Content");
            sb.AppendLine();

            sb.AppendLine("使用例 (cURL):");
            sb.AppendLine($"curl.exe -X POST http://127.0.0.1:{port}/api/v2/meta-request \\");
            sb.AppendLine("  -H \"Authorization: Bearer cocoro_token\" \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine("  -d '{\"instruction\":\"これは直近1時間のニュースです。内容をユーザに説明し、感想も述べてください。\",\"payload_text\":\"～ニュース内容～\"}'");
            sb.AppendLine();

            sb.AppendLine("使用例 (PowerShell):");
            sb.AppendLine("Invoke-RestMethod -Method Post `");
            sb.AppendLine($"  -Uri \"http://127.0.0.1:{port}/api/v2/meta-request\" `");
            sb.AppendLine("  -Headers @{ Authorization = \"Bearer cocoro_token\" } `");
            sb.AppendLine("  -ContentType \"application/json; charset=utf-8\" `");
            sb.AppendLine("  -Body '{\"instruction\":\"これは直近1時間のニュースです。内容をユーザに説明し、感想も述べてください。\",\"payload_text\":\"～ニュース内容～\"}'");
            return sb.ToString();
        }
    }
}
