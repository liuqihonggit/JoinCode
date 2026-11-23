namespace JoinCode.Abstractions.Configuration;

/// <summary>
/// 原生沙箱平台支持结论 — <see cref="Supported"/>=false 时 <see cref="Reason"/> 必须给出可操作原因。
/// </summary>
/// <param name="Supported">该平台是否存在本仓库可用的原生沙箱实现。</param>
/// <param name="Platform">平台标识（与 <c>sandbox.enabledPlatforms</c> 词表一致）。</param>
/// <param name="Reason">人类可读原因（支持时说明依赖，不支持时说明缺口）。</param>
public readonly record struct SandboxPlatformSupportInfo(bool Supported, string Platform, string Reason) {
    /// <summary>渲染为「平台：原因」 — 供 /sandbox status 与启动警告复用。</summary>
    public string Describe() => $"{Platform}: {Reason}";
}

/// <summary>
/// 沙箱原生平台真值 — 对齐基准「macOS/Linux 支持，Windows 不支持」并将 macOS 的 Seatbelt 缺口显式化。
/// <para><b>为什么单独一层</b>：<c>sandbox.enabledPlatforms</c> 只是企业白名单，
/// 不能代表当前代码是否真有该平台 provider；两者必须分开判定，否则 Windows 会被误报为「已支持」。</para>
/// </summary>
public static class SandboxPlatformSupport {
    /// <summary>评估平台原生支持 — Linux 有 bubblewrap；Windows 不支持；macOS 的 Seatbelt provider 尚未实现。</summary>
    /// <param name="platform">平台标识（<see cref="SandboxGate.CurrentPlatform"/>）。</param>
    /// <returns>支持结论与原因。</returns>
    public static SandboxPlatformSupportInfo Evaluate(string platform) {
        ArgumentException.ThrowIfNullOrEmpty(platform);

        if (string.Equals(platform, SandboxGate.PlatformLinux, StringComparison.OrdinalIgnoreCase)) {
            return new SandboxPlatformSupportInfo(
                Supported: true,
                platform,
                "Linux 原生沙箱（bubblewrap；需本机已安装 bwrap）");
        }

        if (string.Equals(platform, SandboxGate.PlatformWindows, StringComparison.OrdinalIgnoreCase)) {
            return new SandboxPlatformSupportInfo(
                Supported: false,
                platform,
                "Windows 不支持原生沙箱（基准同样将 Windows 置为不支持）");
        }

        if (string.Equals(platform, SandboxGate.PlatformMacOs, StringComparison.OrdinalIgnoreCase)) {
            return new SandboxPlatformSupportInfo(
                Supported: false,
                platform,
                "macOS Seatbelt provider 尚未实现，沙箱在该平台不可用");
        }

        return new SandboxPlatformSupportInfo(
            Supported: false,
            platform,
            $"未知平台 {platform} 没有可用的沙箱 provider");
    }

    /// <summary>平台是否具备原生沙箱实现 — 不含 <c>enabledPlatforms</c> 企业白名单判定。</summary>
    /// <param name="platform">平台标识。</param>
    /// <returns>支持返回 true。</returns>
    public static bool IsSupported(string platform) => Evaluate(platform).Supported;
}
