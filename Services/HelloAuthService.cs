using Windows.Security.Credentials.UI;

namespace LocalPasswordVault.Services
{
    /// <summary>
    /// 只负责唤起 Windows 自带的 Windows Hello 验证窗口，并返回"通过 / 不通过"结果。
    /// 不读取、不保存、不模拟 Windows 登录密码或 PIN。
    /// </summary>
    public static class HelloAuthService
    {
        /// <summary>检查当前设备上 Windows Hello / PIN 是否可用。</summary>
        public static async Task<bool> IsAvailableAsync()
        {
            try
            {
                var availability = await UserConsentVerifier.CheckAvailabilityAsync();
                return availability == UserConsentVerifierAvailability.Available;
            }
            catch (Exception ex)
            {
                Log.Error("检查 Windows Hello 可用性失败", ex);
                return false;
            }
        }

        /// <summary>唤起系统验证窗口。只有系统返回 Verified 才视为通过。</summary>
        public static async Task<bool> VerifyAsync(string message)
        {
            try
            {
                var result = await UserConsentVerifier.RequestVerificationAsync(message);
                return result == UserConsentVerificationResult.Verified;
            }
            catch (Exception ex)
            {
                Log.Error("调用 Windows Hello 验证失败", ex);
                return false;
            }
        }
    }
}
