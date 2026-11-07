using System.Text.RegularExpressions;
using System.Windows;

namespace LocalPasswordVault.Views
{
    public partial class ExportBackupWindow : Window
    {
        private string? _masterPassword;

        public ExportBackupWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => PasswordInput.Focus();
        }

        /// <summary>取走主密码并清空输入框，减少密码在界面上的停留时间。</summary>
        public string? TakeMasterPassword()
        {
            var password = _masterPassword;
            _masterPassword = null;
            PasswordInput.Password = string.Empty;
            ConfirmInput.Password = string.Empty;
            return password;
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            var password = PasswordInput.Password;
            var confirm = ConfirmInput.Password;

            if (password.Length < 8)
            {
                ShowError("主密码至少需要 8 位。");
                return;
            }

            if (password.Length > 128)
            {
                ShowError("主密码过长，请控制在 128 位以内。");
                return;
            }

            if (!Regex.IsMatch(password, "[A-Za-z]") || !Regex.IsMatch(password, "[0-9]"))
            {
                ShowError("主密码必须同时包含字母和数字。");
                return;
            }

            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                ShowError("两次输入的主密码不一致。");
                return;
            }

            _masterPassword = password;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _masterPassword = null;
            PasswordInput.Password = string.Empty;
            ConfirmInput.Password = string.Empty;
            DialogResult = false;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
