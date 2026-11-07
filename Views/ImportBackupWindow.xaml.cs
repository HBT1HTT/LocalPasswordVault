using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace LocalPasswordVault.Views
{
    public partial class ImportBackupWindow : Window
    {
        private string? _selectedPath;
        private string? _masterPassword;

        public ImportBackupWindow()
        {
            InitializeComponent();
        }

        public string? SelectedFilePath => _selectedPath;

        /// <summary>取走主密码并清空输入框，减少密码在界面上的停留时间。</summary>
        public string? TakeMasterPassword()
        {
            var password = _masterPassword;
            _masterPassword = null;
            PasswordInput.Password = string.Empty;
            return password;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择加密备份文件",
                Filter = "加密备份 (*.vault)|*.vault|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (dialog.ShowDialog(this) == true)
            {
                PathText.Text = dialog.FileName;
                ErrorText.Visibility = Visibility.Collapsed;
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var path = PathText.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowError("请先选择要导入的备份文件。");
                return;
            }

            if (!File.Exists(path))
            {
                ShowError("所选备份文件不存在，请重新选择。");
                return;
            }

            var password = PasswordInput.Password;
            if (string.IsNullOrWhiteSpace(password))
            {
                ShowError("请输入备份文件的主密码。");
                return;
            }

            _selectedPath = path;
            _masterPassword = password;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _masterPassword = null;
            PasswordInput.Password = string.Empty;
            DialogResult = false;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
