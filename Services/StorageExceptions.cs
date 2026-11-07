namespace LocalPasswordVault.Services
{
    /// <summary>数据库无法打开 / 读取 / 写入时抛出的友好异常，消息中不含任何敏感信息。</summary>
    public sealed class StorageException : Exception
    {
        public StorageException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>DPAPI 解密失败（例如数据库来自其他 Windows 用户或电脑）。</summary>
    public sealed class EntryDecryptionException : Exception
    {
        public EntryDecryptionException(Exception? innerException = null)
            : base("无法解密该条目，可能数据库来自其他 Windows 用户或其他电脑。", innerException)
        {
        }
    }
}
