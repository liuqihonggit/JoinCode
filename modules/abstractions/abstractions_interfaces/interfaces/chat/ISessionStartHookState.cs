namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 会话级 SessionStart hook 执行标记（ADR 0165 / TASK135 B2）— 每代只跑一次；
/// 会话翻页后新代允许再跑，显式跑过 hook 的入口（/clear）可标记该代已跑，避免重复。
/// </summary>
public interface ISessionStartHookState {
    /// <summary>取得本代 SessionStart 的执行权 — 同代只成功一次；sessionId 变化即换代，允许新代再跑。</summary>
    /// <param name="sessionId">本回合所属会话代次。</param>
    /// <returns>true 表示调用方获得执行权。</returns>
    bool TryBegin(string sessionId);

    /// <summary>标记该代已跑过 —— 显式执行过 SessionStart 的入口调用，避免新代首个查询重复执行。</summary>
    /// <param name="sessionId">已跑过 hook 的会话代次。</param>
    void MarkRan(string sessionId);
}
