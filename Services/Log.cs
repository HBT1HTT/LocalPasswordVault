using System.IO;

namespace LocalPasswordVault.Services
{
    /// <summary>
    /// 只记录非敏感运行信息。调用方不得传入账号、密码、附加信息等明文内容。
    /// </summary>
    public static class Log
    {
        private static readonly string LogDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalPasswordVault");

        private static readonly string LogPath = Path.Combine(LogDirectory, "app.log");
        private static readonly object SyncRoot = new();

        public static void Info(string message) => Write("INFO", message);

        public static void Error(string context, Exception exception)
        {
            // 只写异常类型，不写异常消息 / 堆栈，避免敏感内容进入日志
            Write("ERROR", $"{context}: {exception.GetType().Name}");
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(
                        LogPath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // 日志写入失败不影响主流程
            }
        }
    }
}
