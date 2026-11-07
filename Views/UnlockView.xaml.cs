using System.Windows;
using System.Windows.Controls;
using LocalPasswordVault.Services;

namespace LocalPasswordVault.Views
{
    public partial class UnlockView : UserControl
    {
        private bool _verifying;
        private bool _autoStarted;

        public event EventHandler? Unlocked;

        public UnlockView()
        {
            InitializeComponent();
            Loaded += UnlockView_Loaded;
        }

        private async void UnlockView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_autoStarted)
            {
                return;
            }

            _autoStarted = true;
            await VerifyAsync();
        }

        private async void VerifyButton_Click(object sender, RoutedEventArgs e) => await VerifyAsync();

        private async Task VerifyAsync()
        {
            if (_verifying)
            {
                return;
            }

            _verifying = true;
            VerifyButton.IsEnabled = false;
            ErrorText.Visibility = Visibility.Collapsed;

            try
            {
                StatusText.Text = "正在检查 Windows Hello 是否可用…";
                var available = await HelloAuthService.IsAvailableAsync();
                if (!available)
                {
                    Log.Info("Windows Hello 不可用");
                    StatusText.Text = "Windows Hello 不可用，无法解锁。";
                    ShowError("当前 Windows 未配置 PIN 或 Windows Hello，请先在系统设置中配置后再使用。");
                    return;
                }

                StatusText.Text = "请在 Windows 验证窗口中完成身份验证…";
                var verified = await HelloAuthService.VerifyAsync("验证身份以打开本地密码库");
                if (verified)
                {
                    Log.Info("Windows Hello 验证通过");
                    StatusText.Text = "验证通过。";
                    Unlocked?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    Log.Info("Windows Hello 验证未通过或已取消");
                    StatusText.Text = "验证未通过。";
                    ShowError("验证未通过，请重试。");
                }
            }
            catch (Exception ex)
            {
                Log.Error("解锁流程异常", ex);
                StatusText.Text = "验证过程出现异常。";
                ShowError("验证未通过，请重试。");
            }
            finally
            {
                _verifying = false;
                VerifyButton.IsEnabled = true;
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
