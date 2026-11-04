namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 工具生命周期杀手 — 按 agentId 杀死其所有工具子进程（ADR 0152 session 级别工具生命周期 map）
/// <para>
/// 由 <c>SessionToolMap</c> 实现，注入到 <c>SubAgentStallDefenseCoordinator</c>（ADR 0106 卡死防护），
/// 在子代理被确认卡死/崩溃且决定放弃（L4 压缩/退出）时调用，防止孤儿进程。
/// </para>
/// </summary>
public interface IToolLifecycleKiller {
    /// <summary>
    /// 杀死指定子代理的所有工具子进程，返回成功杀死的进程数。
    /// </summary>
    /// <param name="agentId">子代理 ID。</param>
    /// <returns>成功杀死的进程数。</returns>
    int KillByAgent(string agentId);
}
