using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;

namespace LocalPasswordVault.Services
{
    /// <summary>
    /// 单实例保护：按当前 Windows 用户 SID 命名 Mutex 与激活管道，
    /// 不同 Windows 用户的实例互不影响，同一用户的第二个实例会被拒绝。
    /// 注意：这只防止同一用户同时打开多个窗口各自解密数据，无法阻止内存读取类工具。
    /// </summary>
    public sealed class SingleInstanceGuard : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly string _pipeName;
        private volatile bool _listening;

        private SingleInstanceGuard(Mutex mutex, string pipeName)
        {
            _mutex = mutex;
            _pipeName = pipeName;
        }

        /// <summary>尝试成为唯一实例；已有实例在运行时返回 null。</summary>
        public static SingleInstanceGuard? TryAcquire()
        {
            var sid = CurrentUserSid();
            var mutexName = $@"Global\LocalPasswordVault_{sid}";
            var mutex = new Mutex(true, mutexName, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return null;
            }

            return new SingleInstanceGuard(mutex, $@"LocalPasswordVault.Activate.{sid}");
        }

        /// <summary>通知已运行的实例把窗口切到前台。连接失败返回 false。</summary>
        public static bool SignalExistingInstance()
        {
            try
            {
                // 连接本身即为信号，不写入数据：服务端收到连接后会立即断开，写数据可能引发 IOException
                using var client = new NamedPipeClientStream(".", $@"LocalPasswordVault.Activate.{CurrentUserSid()}", PipeDirection.Out);
                client.Connect(1500);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("连接已运行实例失败", ex);
                return false;
            }
        }

        /// <summary>在后台线程监听激活请求；收到请求后在 UI 线程调用回调。</summary>
        public void StartActivationListener(Action onActivate)
        {
            if (_listening)
            {
                return;
            }

            _listening = true;
            var thread = new Thread(() =>
            {
                while (_listening)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1);
                        server.WaitForConnection();
                        // 收到任意连接即视为“请把窗口切到前台”
                        onActivate();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("单实例激活监听异常", ex);
                        Thread.Sleep(300);
                    }
                }
            })
            {
                IsBackground = true,
                Name = "LocalPasswordVault.ActivationListener",
            };
            thread.Start();
        }

        public void Dispose()
        {
            _listening = false;
            _mutex.Dispose();
        }

        private static string CurrentUserSid()
        {
            try
            {
                var sid = WindowsIdentity.GetCurrent().User?.Value;
                if (string.IsNullOrEmpty(sid) is false)
                {
                    return sid;
                }
            }
            catch
            {
                // 取不到 SID 时退回用户名，仍然保证“同一用户只能一个实例”
            }

            return Environment.UserName;
        }
    }
}
