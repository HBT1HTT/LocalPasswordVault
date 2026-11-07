using System.Windows;
using LocalPasswordVault.Services;

namespace LocalPasswordVault
{
    public partial class App : Application
    {
        private SingleInstanceGuard? _instanceGuard;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _instanceGuard = SingleInstanceGuard.TryAcquire();
            if (_instanceGuard is null)
            {
                Log.Info("检测到已有实例在运行");
                // 已有实例在运行：只把它的窗口切到前台，不打开新界面，也不绕过其解锁状态。
                // 激活失败（例如管道未就绪）时给出明确提示后退出。
                if (!SingleInstanceGuard.SignalExistingInstance())
                {
                    MessageBox.Show(
                        "本地密码库已经在运行。",
                        "本地密码库",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                Shutdown();
                return;
            }

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
            var window = new MainWindow();
            _instanceGuard.StartActivationListener(window.ActivateFromExternalInstance);
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _instanceGuard?.Dispose();
            _instanceGuard = null;
            base.OnExit(e);
        }
    }
}
