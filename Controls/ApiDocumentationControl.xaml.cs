using CocoroConsole.Services;
using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;

namespace CocoroConsole.Controls
{
    public partial class ApiDocumentationControl : UserControl
    {
        private static readonly Brush DocumentationForeground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));

        public ApiDocumentationControl()
        {
            InitializeComponent();
        }

        public async Task InitializeAsync()
        {
            UpdateConsoleAccessTokenText();
            DocsSectionsPanel.Children.Clear();
            try
            {
                var appSettings = AppSettings.Instance;
                using var apiClient = new OtomeKairoApiClient(
                    appSettings.GetOtomeKairoBaseUrl(),
                    appSettings.OtomeKairoBearerToken);
                var docs = await apiClient.GetApiDocumentationAsync();
                foreach (var section in docs.Sections)
                {
                    AddDocumentSection(
                        string.IsNullOrWhiteSpace(section.Title) ? section.SectionId : section.Title,
                        ReplaceDisplayPlaceholders(section.BodyText));
                }
            }
            catch (Exception ex)
            {
                AddDocumentSection(
                    "ドキュメント",
                    $"OtomeKairo から API ドキュメントを取得できませんでした。\n{ex.Message}",
                    isExpanded: true);
            }
        }

        private void UpdateConsoleAccessTokenText()
        {
            var token = AppSettings.Instance.OtomeKairoBearerToken;
            ConsoleAccessTokenTextBox.Text = string.IsNullOrWhiteSpace(token) ? "未取得" : token;
        }

        private void AddDocumentSection(string title, string body, bool isExpanded = false)
        {
            var textBox = new TextBox
            {
                IsReadOnly = true,
                BorderThickness = new System.Windows.Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Foreground = DocumentationForeground,
                Padding = new System.Windows.Thickness(2),
                TextWrapping = System.Windows.TextWrapping.Wrap,
                AcceptsReturn = true,
                FontFamily = new FontFamily("Consolas"),
                Margin = new System.Windows.Thickness(0, 10, 0, 0),
                Text = body,
            };
            var expander = new Expander
            {
                Header = title,
                Foreground = DocumentationForeground,
                IsExpanded = isExpanded,
                Margin = new System.Windows.Thickness(0, 10, 0, 0),
                Content = textBox,
            };
            DocsSectionsPanel.Children.Add(expander);
        }

        private static string ReplaceDisplayPlaceholders(string body)
        {
            var baseUrl = AppSettings.Instance.GetOtomeKairoBaseUrl();
            return (body ?? string.Empty).Replace("{BASE_URL}", baseUrl, StringComparison.Ordinal);
        }
    }
}
