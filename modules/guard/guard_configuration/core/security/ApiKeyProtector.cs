namespace Guard.Configuration.Security;

/// <summary>
/// API Key 保护器 — C6: auth.json 明文存 key 修复
/// <para>
/// Windows: 使用 DPAPI (ProtectedData) 加密,密钥绑定当前 Windows 用户账户
/// 非 Windows: 拒绝加密,调用方应降级提示用户使用环境变量
/// </para>
/// <para>
/// 存储格式: "dpapi:&lt;base64-encrypted-bytes&gt;" 前缀标识加密格式
/// 无前缀的旧明文 key 在加载时原样返回(过渡期兼容)
/// </para>
/// </summary>
public static class ApiKeyProtector {
    private const string DpapiPrefix = "dpapi:";

    /// <summary>
    /// 加密 API Key — Windows 用 DPAPI,非 Windows 降级为明文+警告(不阻断功能)
    /// </summary>
    /// <param name="plaintext">明文 API Key</param>
    /// <returns>加密后的字符串("dpapi:" 前缀 + Base64),或非 Windows 明文原样返回</returns>
    public static string Encrypt(string plaintext) {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        if (!OperatingSystem.IsWindows())
            return plaintext;

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return DpapiPrefix + Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// 解密 API Key — 检测 "dpapi:" 前缀,有则解密,无则原样返回(旧明文兼容)
    /// </summary>
    /// <param name="stored">存储的值(可能带 "dpapi:" 前缀或明文)</param>
    /// <returns>明文 API Key</returns>
    public static string Decrypt(string stored) {
        if (string.IsNullOrEmpty(stored))
            return stored;

        if (!stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            return stored;

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "[AUTH-DECRYPT-NOT-SUPPORTED] auth.json 中的 API Key 使用 DPAPI 加密,"
                + "但当前平台非 Windows 无法解密。请在 Windows 上重新登录,或使用环境变量设置 API Key。");

        var base64 = stored.AsSpan(DpapiPrefix.Length);
        var encrypted = Convert.FromBase64String(base64.ToString());
        var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    /// <summary>
    /// 是否支持加密 — Windows 返回 true,其他平台 false
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows();
}
