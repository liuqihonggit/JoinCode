namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 环境探测服务接口 — 探测运行环境能力，为Shell工具提供执行器选择依据
/// </summary>
public interface IEnvironmentProbeService
{
    Task<EnvironmentReport> ProbeEnvironmentAsync(bool forceRescan = false, CancellationToken ct = default);
    string NormalizePath(string rawPath, string targetFormat = "auto");
    Task<IReadOnlyDictionary<string, ExecutorScore>> GetExecutorScoresAsync(CancellationToken ct = default);

    /// <summary>
    /// 路径门控 — 根据当前平台和目标 Shell 类型转换路径格式
    /// <list type="bullet">
    ///   <item>Windows + Bash(Git Bash/WSL) → POSIX 格式: C:\Users\test → /c/Users/test</item>
    ///   <item>Windows + PowerShell/Cmd → Windows 格式: /c/Users/test → C:\Users\test</item>
    ///   <item>Linux/Mac + 任何 Shell → POSIX 格式</item>
    /// </list>
    /// </summary>
    /// <param name="rawPath">LLM 输出的原始路径</param>
    /// <param name="provider">目标 Shell 执行体</param>
    /// <returns>转换后的路径</returns>
    string GatePath(string rawPath, IShellProvider provider);

    /// <summary>
    /// 命令路径门控 — 扫描命令字符串中的路径片段并转换为指定格式
    /// 匹配 Windows 绝对路径和 POSIX 风格 Windows 路径，排除 URL 和环境变量
    /// </summary>
    /// <param name="command">Shell 命令字符串</param>
    /// <param name="provider">目标 Shell 执行体</param>
    /// <returns>路径转换后的命令字符串</returns>
    string GateCommandPaths(string command, IShellProvider provider);
}
