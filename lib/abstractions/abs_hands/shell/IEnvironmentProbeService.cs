namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 环境探测服务接口 — 探测运行环境能力，为Shell工具提供执行器选择依据
/// </summary>
public interface IEnvironmentProbeService {
    /// <summary>异步探测运行环境。</summary>
    Task<EnvironmentReport> ProbeEnvironmentAsync(bool forceRescan = false, CancellationToken ct = default);
    /// <summary>异步获取执行器评分字典。</summary>
    Task<IReadOnlyDictionary<string, ExecutorScore>> GetExecutorScoresAsync(CancellationToken ct = default);
}