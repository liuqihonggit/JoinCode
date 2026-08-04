namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 环境探测服务接口 — 探测运行环境能力，为Shell工具提供执行器选择依据
/// </summary>
public interface IEnvironmentProbeService
{
    Task<EnvironmentReport> ProbeEnvironmentAsync(bool forceRescan = false, CancellationToken ct = default);
    string NormalizePath(string rawPath, string targetFormat = "auto");
    Task<IReadOnlyDictionary<string, ExecutorScore>> GetExecutorScoresAsync(CancellationToken ct = default);
}
