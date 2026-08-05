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
    /// 路径门控 — 根据当前平台和目标执行器类型转换路径格式
    /// </summary>
    /// <param name="rawPath">LLM 输出的原始路径</param>
    /// <param name="actuator">目标系统执行器</param>
    /// <returns>转换后的路径</returns>
    string GatePath(string rawPath, ISystemActuator actuator);

    /// <summary>
    /// 命令路径.门控 — 扫描命令字符串中的路径片段并转换为指定格式
    /// </summary>
    /// <param name="command">命令字符串</param>
    /// <param name="actuator">目标系统执行器</param>
    /// <returns>路径转换后的命令字符串</returns>
    string GateCommandPaths(string command, ISystemActuator actuator);
}
