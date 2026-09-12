namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// worktree 决策策略 — 两层决策：LLM 全局 enableWorktree + 节点 Variant==Code 自动开
/// 探索/审查只读不改不开 worktree，改代码节点需要物理隔离防冲突
/// </summary>
public interface IWorktreeDecisionPolicy
{
    /// <summary>
    /// 第一层：LLM 全局决策 — 根据任务难度判断是否开启 worktree
    /// </summary>
    /// <param name="todoCount">TODO 数量（任务分解后的子任务数）</param>
    /// <param name="hotFileCount">涉及热文件数</param>
    /// <param name="estimatedParallelism">预估并行度</param>
    /// <returns>true=开启 worktree（任务大多agent并行），false=小任务单agent顺序</returns>
    bool ShouldEnableWorktree(int todoCount, int hotFileCount, int estimatedParallelism);

    /// <summary>
    /// 第二层：节点类型自动判断 — 全局开 + Variant==Code 才开 worktree
    /// </summary>
    /// <param name="enableWorktree">第一层全局决策结果</param>
    /// <param name="variant">节点执行者变体</param>
    /// <returns>Worktree 或 None</returns>
    AgentIsolationMode Decide(bool enableWorktree, ExecutorVariant variant);
}
