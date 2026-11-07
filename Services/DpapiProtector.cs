using System.Security.Cryptography;
using System.Text;

namespace LocalPasswordVault.Services
{
    /// <summary>
    /// DPAPI（CurrentUser 作用域）加解密封装，仅存储服务使用，界面层不直接接触 DPAPI。
    /// </summary>
    public static class DpapiProtector
    {
        public static byte[] Protect(string plainText)
        {
            var bytes = Encoding.UTF8.GetBytes(plainText ?? string.Empty);
            return ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        }

        public static string Unprotect(byte[] cipher)
        {
            var bytes = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
