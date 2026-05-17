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
                ConversationApiTextBox.Text = GetConversationApiDetails();
                WakeApiTextBox.Text = GetWakeApiDetails();

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"API説明の初期化エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GetConversationApiDetails()
        {
            var baseUrl = AppSettings.Instance.GetOtomeKairoBaseUrl();
            var sb = new StringBuilder();
            sb.AppendLine("用途:");
            sb.AppendLine("- OtomeKairo に会話入力を送る");
            sb.AppendLine("- 応答は JSON envelope の data.result_kind と data.reply で返る");
            sb.AppendLine();

            sb.AppendLine("エンドポイント:");
            sb.AppendLine($"- POST {baseUrl}/api/conversation");
            sb.AppendLine();

            sb.AppendLine("認証:");
            sb.AppendLine("- Authorization: Bearer <TOKEN>");
            sb.AppendLine();

            sb.AppendLine("リクエストボディ (JSON):");
            sb.AppendLine("{");
            sb.AppendLine("  \"text\": \"こんにちは\",");
            sb.AppendLine("  \"images\": [\"data:image/png;base64,...\"],");
            sb.AppendLine("  \"client_context\": {");
            sb.AppendLine("    \"source\": \"CocoroConsole\",");
            sb.AppendLine("    \"client_id\": \"console-...\",");
            sb.AppendLine("    \"active_app\": \"Slack\",");
            sb.AppendLine("    \"window_title\": \"general | Slack\",");
            sb.AppendLine("    \"locale\": \"ja-JP\"");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("- images は省略可能、指定時は Data URI を最大1件送る");
            sb.AppendLine();

            sb.AppendLine("レスポンス:");
            sb.AppendLine("{");
            sb.AppendLine("  \"ok\": true,");
            sb.AppendLine("  \"data\": {");
            sb.AppendLine("    \"cycle_id\": \"cycle:...\",");
            sb.AppendLine("    \"result_kind\": \"reply\",");
            sb.AppendLine("    \"reply\": { \"text\": \"こんにちは\" }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine("使用例 (cURL):");
            sb.AppendLine($"curl.exe -k -X POST {baseUrl}/api/conversation \\");
            sb.AppendLine("  -H \"Authorization: Bearer <TOKEN>\" \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine("  -d '{\"text\":\"こんにちは\",\"images\":[\"data:image/png;base64,...\"],\"client_context\":{\"source\":\"CocoroConsole\",\"client_id\":\"console-...\",\"locale\":\"ja-JP\"}}'");
            sb.AppendLine();

            sb.AppendLine("使用例 (PowerShell):");
            sb.AppendLine("# 自己署名HTTPSのため、Windows PowerShell では curl.exe 推奨 / PowerShell 7 なら -SkipCertificateCheck を使用");
            sb.AppendLine("Invoke-RestMethod -Method Post `");
            sb.AppendLine($"  -Uri \"{baseUrl}/api/conversation\" `");
            sb.AppendLine("  -Headers @{ Authorization = \"Bearer <TOKEN>\" } `");
            sb.AppendLine("  -ContentType \"application/json; charset=utf-8\" `");
            sb.AppendLine("  -Body '{\"text\":\"こんにちは\",\"images\":[\"data:image/png;base64,...\"],\"client_context\":{\"source\":\"CocoroConsole\",\"client_id\":\"console-...\",\"locale\":\"ja-JP\"}}'");
            return sb.ToString();
        }

        private static string GetWakeApiDetails()
        {
            var baseUrl = AppSettings.Instance.GetOtomeKairoBaseUrl();
            var sb = new StringBuilder();
            sb.AppendLine("用途:");
            sb.AppendLine("- OtomeKairo に自律起床 1 サイクルの実行を依頼する");
            sb.AppendLine("- wake_policy が disabled の場合は noop を返す");
            sb.AppendLine();

            sb.AppendLine("エンドポイント:");
            sb.AppendLine($"- POST {baseUrl}/api/wake");
            sb.AppendLine();

            sb.AppendLine("認証:");
            sb.AppendLine("- Authorization: Bearer <TOKEN>");
            sb.AppendLine();

            sb.AppendLine("リクエストボディ (JSON):");
            sb.AppendLine("{");
            sb.AppendLine("  \"client_context\": {");
            sb.AppendLine("    \"source\": \"CocoroConsole\",");
            sb.AppendLine("    \"client_id\": \"console-...\",");
            sb.AppendLine("    \"active_app\": \"Slack\",");
            sb.AppendLine("    \"window_title\": \"general | Slack\",");
            sb.AppendLine("    \"locale\": \"ja-JP\"");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine("レスポンス:");
            sb.AppendLine("{");
            sb.AppendLine("  \"ok\": true,");
            sb.AppendLine("  \"data\": {");
            sb.AppendLine("    \"cycle_id\": \"cycle:...\",");
            sb.AppendLine("    \"result_kind\": \"noop\",");
            sb.AppendLine("    \"reply\": null");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine("使用例 (cURL):");
            sb.AppendLine($"curl.exe -k -X POST {baseUrl}/api/wake \\");
            sb.AppendLine("  -H \"Authorization: Bearer <TOKEN>\" \\");
            sb.AppendLine("  -H \"Content-Type: application/json\" \\");
            sb.AppendLine("  -d '{\"client_context\":{\"source\":\"CocoroConsole\",\"client_id\":\"console-...\",\"locale\":\"ja-JP\"}}'");
            sb.AppendLine();

            sb.AppendLine("使用例 (PowerShell):");
            sb.AppendLine("# 自己署名HTTPSのため、Windows PowerShell では curl.exe 推奨 / PowerShell 7 なら -SkipCertificateCheck を使用");
            sb.AppendLine("Invoke-RestMethod -Method Post `");
            sb.AppendLine($"  -Uri \"{baseUrl}/api/wake\" `");
            sb.AppendLine("  -Headers @{ Authorization = \"Bearer <TOKEN>\" } `");
            sb.AppendLine("  -ContentType \"application/json; charset=utf-8\" `");
            sb.AppendLine("  -Body '{\"client_context\":{\"source\":\"CocoroConsole\",\"client_id\":\"console-...\",\"locale\":\"ja-JP\"}}'");
            return sb.ToString();
        }
    }
}
