namespace JoinCode.Reasoning.Engine;

/// <summary>
/// 推理引擎接口 — 基于 DAG 的结构化推理
/// </summary>
public interface IReasoningEngine
{
    /// <summary>
    /// 添加假定
    /// </summary>
    Task AddAssumptionsAsync(IReadOnlyList<DataItem> assumptions, CancellationToken ct);

    /// <summary>
    /// 获取所有数据项
    /// </summary>
    IReadOnlyList<DataItem> GetAllItems();

    /// <summary>
    /// 获取所有证据
    /// </summary>
    IReadOnlyList<EvidenceRecord> GetAllEvidence();

    /// <summary>
    /// 获取已确认的事实
    /// </summary>
    IReadOnlyList<DataItem> GetFacts();

    /// <summary>
    /// 添加证据
    /// </summary>
    void AddEvidence(EvidenceRecord evidence, string claimId);

    /// <summary>
    /// 添加反驳证据
    /// </summary>
    void AddCounterEvidence(EvidenceRecord evidence, string claimId);

    /// <summary>
    /// 执行三权分立对抗流程
    /// </summary>
    Task RunAdversarialProcessAsync(CancellationToken ct);

    /// <summary>
    /// 继续推理 — 预算耗尽后续费并继续执行对抗流程
    /// </summary>
    Task ContinueAsync(BudgetRefillMode? refillMode = null, int? extraRounds = null, int? extraTokens = null, CancellationToken ct = default);

    /// <summary>
    /// 获取引擎状态摘要
    /// </summary>
    ReasoningSummary GetSummary();

    /// <summary>
    /// 获取当前预算状态
    /// </summary>
    BudgetStatus GetBudgetStatus();

    /// <summary>
    /// 证据失效传播 — 降级指定节点，沿 DAG 反向传播到下游
    /// </summary>
    void PropagateEvidenceFailure(string evidenceId);

    /// <summary>
    /// 增量重算 — 只重算受影响的子图
    /// </summary>
    IReadOnlyList<ReasoningPayload> IncrementalRecompute(string changedNodeId);

    /// <summary>
    /// 获取视锥冲突检测结果
    /// </summary>
    ConeConflictResult DetectConeConflict(AgentRole roleA, AgentRole roleB);

    /// <summary>
    /// 展开指定角色的指定片段（渐进式披露）
    /// </summary>
    ObservationFragment? ExpandFragment(AgentRole role, string fragmentId, string triggerCondition);

    /// <summary>
    /// 设置证据URL验证器
    /// </summary>
    void SetUrlVerifier(EvidenceUrlVerifier verifier);

    /// <summary>
    /// 重置推理引擎 — 清空 DAG 和预算
    /// </summary>
    void Reset();
}
