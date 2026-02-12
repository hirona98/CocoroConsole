using CocoroConsole.Utilities;
using System.Windows;

namespace CocoroConsole.Windows
{
    public partial class SimpleProgressDialog : Window
    {
        private const string SimpleProgressDialogPlacementKey = "SimpleProgressDialog";

        public SimpleProgressDialog()
        {
            InitializeComponent();

            // 進捗ダイアログ位置を復元し、以降の移動を記録する
            WindowPlacementManager.AttachAndRestore(this, SimpleProgressDialogPlacementKey);
        }
    }
}
