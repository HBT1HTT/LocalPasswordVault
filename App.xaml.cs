using System.Windows;
using LocalPasswordVault.Services;

namespace LocalPasswordVault
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (_, args) =>
            {
                // 只记录异常类型，异常消息可能包含数据库路径等信息，不写入日志
                Log.Error("界面线程未处理异常", args.Exception);
                args.Handled = true;
                MessageBox.Show(
                    "程序出现异常，当前操作已中止。请重新打开程序后再试。",
                    "本地密码库",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            };

            Log.Info("程序启动");
            new MainWindow().Show();
        }
    }
}
