namespace Core.Hosting;

/// <summary>
/// 服务状态转换规则 — 集中定义 ServiceStatus 所有合法转换
/// <para>原 ServiceHost 直接赋值 _serviceStatuses[name]=status 无校验,现统一提取为转换表</para>
/// <para>Stopped→Starting, Starting→Running/Failed, Running→Stopping/Failed, Stopping→Stopped/Failed, Failed→Starting/Stopped</para>
/// </summary>
public static class ServiceStateTransitions
{
    private static readonly FrozenDictionary<ServiceStatus, FrozenSet<ServiceStatus>> Transitions =
        new Dictionary<ServiceStatus, FrozenSet<ServiceStatus>>
        {
            [ServiceStatus.Stopped] = new HashSet<ServiceStatus>
            {
                ServiceStatus.Starting
            }.ToFrozenSet(),

            [ServiceStatus.Starting] = new HashSet<ServiceStatus>
            {
                ServiceStatus.Running,
                ServiceStatus.Failed
            }.ToFrozenSet(),

            [ServiceStatus.Running] = new HashSet<ServiceStatus>
            {
                ServiceStatus.Stopping,
                ServiceStatus.Failed
            }.ToFrozenSet(),

            [ServiceStatus.Stopping] = new HashSet<ServiceStatus>
            {
                ServiceStatus.Stopped,
                ServiceStatus.Failed
            }.ToFrozenSet(),

            [ServiceStatus.Failed] = new HashSet<ServiceStatus>
            {
                ServiceStatus.Starting,
                ServiceStatus.Stopped
            }.ToFrozenSet()
        }.ToFrozenDictionary();

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法
    /// </summary>
    public static bool CanTransitionTo(ServiceStatus current, ServiceStatus target)
    {
        if (current == target)
        {
            return true;
        }

        return Transitions.TryGetValue(current, out var targets) && targets.Contains(target);
    }

    /// <summary>
    /// 是否为终态 — Stopped 为稳定终态，Failed 为可恢复终态
    /// </summary>
    public static bool IsTerminal(ServiceStatus state) => state == ServiceStatus.Stopped;
}
