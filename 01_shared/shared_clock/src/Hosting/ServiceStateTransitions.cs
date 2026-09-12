namespace Core.Hosting;

/// <summary>
/// 服务状态转换规则 — 集中定义 ServiceStatus 所有合法转换
/// <para>原 ServiceHost 直接赋值 _serviceStatuses[name]=status 无校验,现统一提取为转换表</para>
/// <para>Stopped→Starting, Starting→Running/Failed, Running→Stopping/Failed, Stopping→Stopped/Failed, Failed→Starting/Stopped</para>
/// </summary>
public static class ServiceStateTransitions
{
    /// <summary>
    /// 状态转换位掩码表 — 索引为 (int)ServiceStatus，值为目标状态位掩码。
    /// 替代 FrozenDictionary&lt;ServiceStatus, FrozenSet&lt;ServiceStatus&gt;&gt;，O(1) 数组索引 + 位运算无哈希查找。
    /// </summary>
    private static readonly int[] Transitions =
    [
        /* Stopped=0 */ BitMask.Of(ServiceStatus.Starting),
        /* Starting=1 */ BitMask.Of(ServiceStatus.Running, ServiceStatus.Failed),
        /* Running=2 */ BitMask.Of(ServiceStatus.Stopping, ServiceStatus.Failed),
        /* Stopping=3 */ BitMask.Of(ServiceStatus.Stopped, ServiceStatus.Failed),
        /* Failed=4 */ BitMask.Of(ServiceStatus.Starting, ServiceStatus.Stopped)
    ];

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法
    /// </summary>
    public static bool CanTransitionTo(ServiceStatus current, ServiceStatus target)
    {
        if (current == target)
        {
            return true;
        }

        return BitMask.Contains(Transitions[(int)current], target);
    }

    /// <summary>
    /// 是否为终态 — Stopped 为稳定终态，Failed 为可恢复终态
    /// </summary>
    public static bool IsTerminal(ServiceStatus state) => state == ServiceStatus.Stopped;
}
