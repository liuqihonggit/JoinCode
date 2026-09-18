
namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 服务注册条目 — 聚合工作流服务实例与其运行时状态
/// </summary>
/// <remarks>
/// 合并自 ServiceHost 中分离的 _services 与 _serviceStatuses 双字典,
/// 两者按 serviceName 成对操作,合并后单字典索引减少一次哈希查找。
/// </remarks>
public sealed class ServiceEntry
{
    /// <summary>
    /// 工作流服务实例
    /// </summary>
    public required IWorkflowService Service { get; init; }

    /// <summary>
    /// 服务当前状态 — 可变,由 ServiceHost 在生命周期转换中更新
    /// </summary>
    public ServiceStatus Status { get; set; } = ServiceStatus.Stopped;
}
