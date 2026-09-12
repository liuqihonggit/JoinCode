
namespace Core.Bridge;

/// <summary>
/// Bridge 会话状态转换规则 — 集中定义所有合法转换的前置条件
/// <para>原 SessionRunner 各方法内联 if 检查分散，现统一提取为命名 guard 方法</para>
/// <para>不同动作对同一目标状态有不同 guard：Resume 仅 Suspended→Active，KeepAlive 允许非终态→Active</para>
/// </summary>
public static class BridgeSessionTransitions
{
    /// <summary>
    /// 是否为终态 — Closed 为终态，不可再转换
    /// </summary>
    public static bool IsTerminal(BridgeSessionStatus state) =>
        state == BridgeSessionStatus.Closed;

    /// <summary>
    /// 是否可挂起 — 仅 Active/Idle 可挂起
    /// </summary>
    public static bool CanSuspend(BridgeSessionStatus current) =>
        current is BridgeSessionStatus.Active or BridgeSessionStatus.Idle;

    /// <summary>
    /// 是否可恢复 — 仅 Suspended 可恢复
    /// </summary>
    public static bool CanResume(BridgeSessionStatus current) =>
        current == BridgeSessionStatus.Suspended;

    /// <summary>
    /// 是否可保活 — 非终态可保活（Closed 不可保活）
    /// </summary>
    public static bool CanKeepAlive(BridgeSessionStatus current) =>
        !IsTerminal(current);

    /// <summary>
    /// 是否可停止 — 非终态可停止（Closed 幂等跳过）
    /// </summary>
    public static bool CanStop(BridgeSessionStatus current) =>
        !IsTerminal(current);

    /// <summary>
    /// 是否可从快照恢复 — 非终态可恢复（Closed 不可恢复）
    /// </summary>
    public static bool CanRestore(BridgeSessionStatus current) =>
        !IsTerminal(current);
}
