namespace NativeTui.Core.Repl;

/// <summary>
/// defaultShell 键解析器 — settings.current.defaultShell（"bash"/"powershell"）覆盖平台默认，
/// 对齐 TS resolveDefaultShell（默认 bash，不自动切 Windows PowerShell — 不破坏既有 bash hook 用户）。
/// <para>C# 平台适配差异：未配置时 Windows 用 cmd.exe（C# 既有决策，TS 默认 bash 全平台 — 登记差异）；
/// 显式 "bash"/"powershell" 在任何平台均生效（用户显式覆盖优先）。</para>
/// <para>纯函数（注入 shellName + isWindows，测试友好）；消费点 ReplShell BashDirectExecutor 装配。</para>
/// </summary>
public static class DefaultShellResolver
{
    /// <summary>defaultShell 键的 bash 值 — 对齐 TS 'bash'。</summary>
    public const string Bash = "bash";

    /// <summary>defaultShell 键的 powershell 值 — 对齐 TS 'powershell'。</summary>
    public const string PowerShell = "powershell";

    /// <summary>
    /// 解析 shell 可执行名与命令参数开关 — 显式 settings 值优先，未配置走平台默认。
    /// </summary>
    /// <param name="shellName">settings.current.defaultShell 值（"bash"/"powershell"/null/未知）。</param>
    /// <param name="isWindows">是否 Windows 平台（决定未配置时的回退）。</param>
    /// <returns>(shellFileName, commandFlag) — 如 ("bash", "-c") / ("pwsh", "-Command") / ("cmd.exe", "/c")。</returns>
    public static (string ShellFileName, string CommandFlag) Resolve(string? shellName, bool isWindows)
    {
        if (string.Equals(shellName, Bash, StringComparison.OrdinalIgnoreCase))
        {
            return (Bash, "-c");
        }

        if (string.Equals(shellName, PowerShell, StringComparison.OrdinalIgnoreCase))
        {
            return ("pwsh", "-Command");
        }

        // 未配置或未知值 → 平台默认（C# 既有决策：Windows cmd.exe / 非 Windows bash）
        return isWindows ? ("cmd.exe", "/c") : (Bash, "-c");
    }
}
