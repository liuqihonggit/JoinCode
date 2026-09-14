namespace JoinCode.Reasoning.Engine;

/// <summary>
/// 推理引擎 — 基于 DAG 的三权分立结构化推理核心
/// 集成有限视锥、5维客观权重、链式传播、贝叶斯更新、证据URL验证
/// </summary>
public sealed class ReasoningEngine : IReasoningEngine
{
    private readonly Dag<ReasoningPayload> _dag = new();
    private readonly Dictionary<AgentRole, ReasoningAgent> _agents;
    private readonly ILogger<ReasoningEngine> _logger;
    private readonly ReasoningOptions _options;
    private DateTime? _lastRunAt;

    private int _adversarialRoundCount;
    private int _tokensUsed;
    private int _roundsBudget;
    private int _tokensBudget;

    private readonly ConeOrchestrator _coneOrchestrator = new();
    private readonly BayesianEvidenceUpdater _bayesianUpdater = new();
    private EvidenceUrlVerifier? _urlVerifier;

    /// <summary>
    /// 初始化推理引擎实例
    /// </summary>
    /// <param name="agents">参与推理的 Agent 集合（控方、辩方、法官）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="options">推理配置选项，默认使用 Panda 预设</param>
    public ReasoningEngine(
        IEnumerable<ReasoningAgent> agents,
        ILogger<ReasoningEngine> logger,
        ReasoningOptions? options = null)
    {
        _logger = logger;
        _options = options ?? ReasoningOptions.Panda;
        _agents = agents.ToDictionary(a => a.Role, a => a);
        _roundsBudget = _options.MaxAdversarialRounds;
        _tokensBudget = _options.MaxTokens;

        _coneOrchestrator.RegisterRole(AgentRole.Prosecutor, _options.ConeWindowSize);
        _coneOrchestrator.RegisterRole(AgentRole.Defender, _options.ConeWindowSize);
        _coneOrchestrator.RegisterRole(AgentRole.Judge, _options.ConeWindowSize + 1);
    }

    /// <summary>
    /// 暴露内部 DAG 供 Agent 只读访问
    /// </summary>
    public Dag<ReasoningPayload> Dag => _dag;

    /// <summary>
    /// 视锥调度器
    /// </summary>
    public ConeOrchestrator ConeOrchestrator => _coneOrchestrator;

    /// <summary>
    /// 贝叶斯证据更新器
    /// </summary>
    public BayesianEvidenceUpdater BayesianUpdater => _bayesianUpdater;

    /// <summary>
    /// 设置URL验证器（需要HttpClient，延迟注入）
    /// </summary>
    public void SetUrlVerifier(EvidenceUrlVerifier verifier)
    {
        _urlVerifier = verifier;
    }

    /// <summary>
    /// 异步添加假定数据项并触发对抗推理流程
    /// </summary>
    /// <param name="assumptions">待添加的假定数据项列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task AddAssumptionsAsync(IReadOnlyList<DataItem> assumptions, CancellationToken ct)
    {
        foreach (var item in assumptions)
        {
            if (item.State != DataState.Assumption)
                throw new InvalidOperationException($"[RSN001] 数据项必须以Assumption状态进入，当前: {item.State}");

            if (_options.IsNodeLimitReached(_dag.Nodes.Count))
                throw new InvalidOperationException($"[RSN002] 已达到节点数上限 ({_options.MaxNodes})，无法添加更多假定");

            if (_dag.Nodes.Values.Any(n => n.Payload.Content == item.Content && n.Payload.State != DataState.Rejected))
                throw new InvalidOperationException($"[RSN003] 内容已存在: {item.Content}");

            var payload = new ReasoningPayload
            {
                Id = item.Id,
                Type = ReasoningNodeType.Assumption,
                Content = item.Content,
                State = DataState.Assumption,
                Source = item.Source,
                SubmittedBy = item.SubmittedBy,
            };

            var node = new DagNode<ReasoningPayload> { Id = item.Id, Payload = payload };
            var result = _dag.AddNode(node);
            if (!result.Success)
                throw new InvalidOperationException(result.ErrorMessage ?? "[RSN004] 添加节点失败");

            _logger.LogInformation("[假定] {Content} (ID:{Id})", item.Content, item.Id);

            foreach (AgentRole role in Enum.GetValues<AgentRole>())
            {
                var cone = _coneOrchestrator.GetRole(role);
                if (cone is not null)
                {
                    var fragment = _coneOrchestrator.CreateFragmentFromItem(role, item);
                    cone.AddFragment(fragment);
                }
            }
        }

        await RunAdversarialProcessAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取 DAG 中所有数据项
    /// </summary>
    /// <returns>所有数据项的可枚举集合</returns>
    public IEnumerable<DataItem> GetAllItems()
    {
        return _dag.Nodes.Values
            .Select(n => PayloadToDataItem(n.Payload));
    }

    /// <summary>
    /// 获取 DAG 中所有证据记录
    /// </summary>
    /// <returns>所有证据记录的可枚举集合</returns>
    public IEnumerable<EvidenceRecord> GetAllEvidence()
    {
        return _dag.Nodes.Values
            .Where(n => n.Payload.Type == ReasoningNodeType.Evidence)
            .Select(n => PayloadToEvidence(n.Payload));
    }

    /// <summary>
    /// 获取已确认为事实状态的数据项
    /// </summary>
    /// <returns>事实状态数据项的可枚举集合</returns>
    public IEnumerable<DataItem> GetFacts()
    {
        return _dag.Nodes.Values
            .Where(n => n.Payload.State == DataState.Fact)
            .Select(n => PayloadToDataItem(n.Payload));
    }

    /// <summary>
    /// 向指定假定添加证据，建立证据边并更新贝叶斯信念
    /// </summary>
    /// <param name="evidence">证据记录</param>
    /// <param name="claimId">目标假定节点标识</param>
    public void AddEvidence(EvidenceRecord evidence, string claimId)
    {
        if (_options.IsNodeLimitReached(_dag.Nodes.Count))
        {
            _logger.LogWarning("已达到节点数上限 ({MaxNodes})，无法添加更多证据", _options.MaxNodes);
            return;
        }

        if (!_dag.Nodes.ContainsKey(claimId))
        {
            _logger.LogWarning("目标假定不存在: {ClaimId}", claimId);
            return;
        }

        var existingEvidenceCount = _dag.Edges.Values
            .Count(e => e.ToId == claimId && (e.Label == "SUPPORTS" || e.Label == "REFUTES"));
        if (_options.IsEvidenceLimitReached(existingEvidenceCount))
        {
            _logger.LogWarning("假定 {ClaimId} 已达到证据数上限 ({MaxEvidence})，无法添加更多证据",
                claimId, _options.MaxEvidencePerClaim);
            return;
        }

        var payload = new ReasoningPayload
        {
            Id = evidence.Id,
            Type = ReasoningNodeType.Evidence,
            Content = evidence.Content,
            State = DataState.Verified,
            Category = evidence.Category,
            TrustLevel = evidence.TrustLevel,
            SubmittedBy = evidence.SubmittedBy,
            Source = evidence.Source,
            SourceUrl = evidence.SourceUrl,
            Weight = evidence.Weight,
        };

        var node = new DagNode<ReasoningPayload> { Id = evidence.Id, Payload = payload };
        var addResult = _dag.AddNode(node);
        if (!addResult.Success)
        {
            _logger.LogWarning("添加证据节点失败: {Error}", addResult.ErrorMessage);
            return;
        }

        var edgeLabel = evidence.SubmittedBy == AgentRole.Defender ? "REFUTES" : "SUPPORTS";
        var edge = new DagEdge
        {
            FromId = evidence.Id,
            ToId = claimId,
            Label = edgeLabel,
            Weight = evidence.Weight * (int)evidence.TrustLevel / 100.0,
        };

        var edgeResult = _dag.AddEdge(edge);
        if (!edgeResult.Success)
        {
            _logger.LogWarning("添加证据边失败: {Error} (可能产生环)", edgeResult.ErrorMessage);
            _dag.RemoveNode(evidence.Id);
            return;
        }

        _logger.LogInformation("[{Role}] 提交证据: {Content} (信任度:{Trust})", evidence.SubmittedBy, evidence.Content, evidence.TrustLevel);

        var cone = _coneOrchestrator.GetRole(evidence.SubmittedBy);
        if (cone is not null)
        {
            var fragment = _coneOrchestrator.CreateFragmentFromEvidence(evidence.SubmittedBy, evidence);
            cone.AddFragment(fragment);
        }

        _bayesianUpdater.UpdateFromEvidence(evidence);
    }

    /// <summary>
    /// 向指定假定添加反证（委托给 AddEvidence，由提交者角色决定边的 SUPPORTS/REFUTES 标签）
    /// </summary>
    /// <param name="evidence">反证证据记录</param>
    /// <param name="claimId">目标假定节点标识</param>
    public void AddCounterEvidence(EvidenceRecord evidence, string claimId)
    {
        AddEvidence(evidence, claimId);
    }

    /// <summary>
    /// 执行一轮完整的对抗推理流程：控方推理 → 视锥传递 → 辩方推理 → 视锥传递 → 法官裁决 → 证据URL验证
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task RunAdversarialProcessAsync(CancellationToken ct)
    {
        if (IsBudgetExhausted())
        {
            var budget = GetBudgetStatus();
            _logger.LogWarning("预算已耗尽 — 轮次:{RoundsUsed}/{RoundsBudget} token:{TokensUsed}/{TokensBudget}，请使用 ContinueAsync 续费",
                budget.RoundsUsed, budget.RoundsBudget, budget.TokensUsed, budget.TokensBudget);
            return;
        }

        _adversarialRoundCount++;
        RecordTokenUsage(_options.RoundOverheadTokens);

        var context = new ReasoningContext
        {
            AllItems = GetAllItems(),
            AllEvidence = GetAllEvidence(),
            Dag = _dag,
            Options = _options,
            ConeOrchestrator = _coneOrchestrator,

        };

        if (_agents.TryGetValue(AgentRole.Prosecutor, out var prosecutor))
        {
            var prosAction = await prosecutor.ReasonAsync(context, ct).ConfigureAwait(false);
            ApplyAgentAction(prosAction);
        }

        if (_coneOrchestrator.GetRole(AgentRole.Defender) is { } defCone &&
            _coneOrchestrator.GetRole(AgentRole.Prosecutor) is { } prosCone)
        {
            var latestProsFragments = prosCone.ActiveFragmentIds.Take(3).ToList();
            foreach (var fragId in latestProsFragments)
            {
                _coneOrchestrator.TransferFragment(AgentRole.Prosecutor, AgentRole.Defender, fragId);
            }
        }

        context = new ReasoningContext
        {
            AllItems = GetAllItems(),
            AllEvidence = GetAllEvidence(),
            Dag = _dag,
            Options = _options,
            ConeOrchestrator = _coneOrchestrator,

        };

        if (_agents.TryGetValue(AgentRole.Defender, out var defender))
        {
            var defAction = await defender.ReasonAsync(context, ct).ConfigureAwait(false);
            ApplyAgentAction(defAction);
        }

        if (_coneOrchestrator.GetRole(AgentRole.Judge) is { } judgeCone &&
            _coneOrchestrator.GetRole(AgentRole.Defender) is { } defCone2)
        {
            var latestDefFragments = defCone2.ActiveFragmentIds.Take(3).ToList();
            foreach (var fragId in latestDefFragments)
            {
                _coneOrchestrator.TransferFragment(AgentRole.Defender, AgentRole.Judge, fragId);
            }
        }

        context = new ReasoningContext
        {
            AllItems = GetAllItems(),
            AllEvidence = GetAllEvidence(),
            Dag = _dag,
            Options = _options,
            ConeOrchestrator = _coneOrchestrator,

        };

        if (_agents.TryGetValue(AgentRole.Judge, out var judge))
        {
            var judgeAction = await judge.ReasonAsync(context, ct).ConfigureAwait(false);
            ApplyVerdicts(judgeAction.Verdicts);
        }

        if (_urlVerifier is not null)
        {
            await VerifyAllEvidenceLinksAsync().ConfigureAwait(false);
        }

        _lastRunAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 续费预算并继续执行对抗推理流程
    /// </summary>
    /// <param name="refillMode">预算续费模式，默认使用配置中的 DefaultRefillMode</param>
    /// <param name="extraRounds">额外轮次预算，默认使用配置中的 DefaultRefillRounds</param>
    /// <param name="extraTokens">额外 Token 预算，默认使用配置中的 DefaultRefillTokens</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ContinueAsync(BudgetRefillMode? refillMode = null, int? extraRounds = null, int? extraTokens = null, CancellationToken ct = default)
    {
        var mode = refillMode ?? _options.DefaultRefillMode;
        var rounds = extraRounds ?? _options.DefaultRefillRounds;
        var tokens = extraTokens ?? _options.DefaultRefillTokens;

        switch (mode)
        {
            case BudgetRefillMode.RoundsOnly:
                _roundsBudget += rounds;
                _logger.LogInformation("[续费] 轮次预算 +{Rounds} → {Total}", rounds, _roundsBudget);
                break;
            case BudgetRefillMode.TokensOnly:
                _tokensBudget += tokens;
                _logger.LogInformation("[续费] Token预算 +{Tokens} → {Total}", tokens, _tokensBudget);
                break;
            case BudgetRefillMode.Both:
                _roundsBudget += rounds;
                _tokensBudget += tokens;
                _logger.LogInformation("[续费] 轮次 +{Rounds} → {TotalRounds}, Token +{Tokens} → {TotalTokens}",
                    rounds, _roundsBudget, tokens, _tokensBudget);
                break;
            case BudgetRefillMode.Default:
                var budget = GetBudgetStatus();
                if (budget.IsRoundsExhausted)
                {
                    _roundsBudget += rounds;
                    _logger.LogInformation("[续费] 轮次预算 +{Rounds} → {Total}", rounds, _roundsBudget);
                }
                if (budget.IsTokensExhausted)
                {
                    _tokensBudget += tokens;
                    _logger.LogInformation("[续费] Token预算 +{Tokens} → {Total}", tokens, _tokensBudget);
                }
                break;
        }

        await RunAdversarialProcessAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取推理引擎当前状态的汇总信息
    /// </summary>
    /// <returns>包含各类节点计数、最后运行时间和预算状态的汇总对象</returns>
    public ReasoningSummary GetSummary() => new()
    {
        TotalAssumptions = _dag.Nodes.Values.Count(n => n.Payload.State == DataState.Assumption),
        TotalVerified = _dag.Nodes.Values.Count(n => n.Payload.State == DataState.Verified),
        TotalFacts = _dag.Nodes.Values.Count(n => n.Payload.State == DataState.Fact),
        TotalRejected = _dag.Nodes.Values.Count(n => n.Payload.State == DataState.Rejected),
        TotalPendingEvidence = _dag.Nodes.Values.Count(n => n.Payload.State == DataState.PendingEvidence),
        TotalEvidence = _dag.Nodes.Values.Count(n => n.Payload.Type == ReasoningNodeType.Evidence),
        LastRunAt = _lastRunAt,
        Budget = GetBudgetStatus(),
    };

    /// <summary>
    /// 获取当前预算使用状态
    /// </summary>
    /// <returns>包含已用轮次、轮次预算、已用 Token 和 Token 预算的状态对象</returns>
    public BudgetStatus GetBudgetStatus() => new()
    {
        RoundsUsed = _adversarialRoundCount,
        RoundsBudget = _roundsBudget,
        TokensUsed = _tokensUsed,
        TokensBudget = _tokensBudget,
    };

    /// <summary>
    /// 证据失效传播 — 降级指定节点，沿 DAG 反向传播到下游
    /// </summary>
    public void PropagateEvidenceFailure(string evidenceId)
    {
        if (!_dag.Nodes.TryGetValue(evidenceId, out var node)) return;

        node.Payload.TrustLevel = TrustLevel.Unreliable;
        node.Payload.State = DataState.Rejected;
        node.Version++;

        var affected = _dag.GetAffectedSubgraph(evidenceId);
        foreach (var affectedNode in affected)
        {
            if (affectedNode.Id == evidenceId) continue;
            if (affectedNode.Payload.Type == ReasoningNodeType.Verdict)
            {
                affectedNode.Payload.State = DataState.PendingEvidence;
                affectedNode.Payload.Confidence = _options.DowngradedConfidence;
                affectedNode.Version++;
                _logger.LogInformation("[传播] 裁决降级: {Content}", affectedNode.Payload.Content);
            }
        }

        _bayesianUpdater.UpdateBelief(evidenceId, 0.1, 0.05);
    }

    /// <summary>
    /// 增量重算 — 只重算受影响的子图
    /// </summary>
    public IReadOnlyList<ReasoningPayload> IncrementalRecompute(string changedNodeId)
    {
        var affected = _dag.GetAffectedSubgraph(changedNodeId);
        return affected.Select(n => n.Payload).ToList();
    }

    /// <summary>
    /// 获取视锥冲突检测结果
    /// </summary>
    public ConeConflictResult DetectConeConflict(AgentRole roleA, AgentRole roleB)
    {
        return _coneOrchestrator.DetectConeConflict(roleA, roleB);
    }

    /// <summary>
    /// 展开指定角色的指定片段
    /// </summary>
    public ObservationFragment? ExpandFragment(AgentRole role, string fragmentId, string triggerCondition)
    {
        var cone = _coneOrchestrator.GetRole(role);
        return cone?.ExpandFragment(fragmentId, triggerCondition);
    }

    private bool IsBudgetExhausted()
    {
        return _adversarialRoundCount >= _roundsBudget || _tokensUsed >= _tokensBudget;
    }

    private void RecordTokenUsage(int tokens)
    {
        _tokensUsed += tokens;
    }

    private void ApplyAgentAction(AgentAction action)
    {
        if (action.TokensUsed > 0)
        {
            RecordTokenUsage(action.TokensUsed);
        }

        foreach (var evidence in action.Evidence)
        {
            foreach (var claimId in action.AffectedClaimIds)
            {
                AddEvidence(evidence, claimId);
            }
        }

        foreach (var counter in action.CounterEvidence)
        {
            foreach (var claimId in action.AffectedClaimIds)
            {
                AddCounterEvidence(counter, claimId);
            }
        }

        foreach (var doubt in action.Doubts)
        {
            _logger.LogInformation("[{Role}] 质疑: {Doubt}", action.AgentRole, doubt);
        }
    }

    private void ApplyVerdicts(IReadOnlyList<Verdict> verdicts)
    {
        foreach (var verdict in verdicts)
        {
            if (!_dag.Nodes.TryGetValue(verdict.ClaimId, out var claimNode)) continue;

            var verdictPayload = new ReasoningPayload
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = ReasoningNodeType.Verdict,
                Content = verdict.Reason ?? string.Empty,
                State = verdict.Decision == VerdictDecision.Accept ? DataState.Fact : DataState.Rejected,
                Confidence = verdict.Confidence,
                SubmittedBy = AgentRole.Judge,
            };

            var verdictNode = new DagNode<ReasoningPayload> { Id = verdictPayload.Id, Payload = verdictPayload };
            _dag.AddNode(verdictNode);

            var edge = new DagEdge
            {
                FromId = verdict.ClaimId,
                ToId = verdictPayload.Id,
                Label = "DECIDES",
                Weight = _options.VerdictEdgeWeight,
            };
            _dag.AddEdge(edge);

            switch (verdict.Decision)
            {
                case VerdictDecision.Accept:
                    claimNode.Payload.State = DataState.Fact;
                    claimNode.Payload.Confidence = verdict.Confidence;
                    claimNode.Payload.VerifiedAt = DateTime.UtcNow;
                    claimNode.Payload.VerifiedBy = "法官裁决";
                    claimNode.Version++;
                    _logger.LogInformation("[法官] 接受: {Content} (置信度:{Confidence}%)", claimNode.Payload.Content, claimNode.Payload.Confidence);
                    break;
                case VerdictDecision.Reject:
                    claimNode.Payload.State = DataState.Rejected;
                    claimNode.Payload.Confidence = _options.RejectedConfidence;
                    claimNode.Version++;
                    _logger.LogInformation("[法官] 驳回: {Content}", claimNode.Payload.Content);
                    break;
                case VerdictDecision.Pending:
                    claimNode.Payload.State = DataState.PendingEvidence;
                    claimNode.Version++;
                    _logger.LogInformation("[法官] 等待更多证据: {Content}", claimNode.Payload.Content);
                    break;
                case VerdictDecision.PartiallyAccept:
                    claimNode.Payload.State = DataState.Verified;
                    claimNode.Payload.Confidence = verdict.Confidence;
                    claimNode.Payload.VerifiedAt = DateTime.UtcNow;
                    claimNode.Payload.VerifiedBy = "法官部分接受";
                    claimNode.Version++;
                    _logger.LogInformation("[法官] 部分接受: {Content} (置信度:{Confidence}%)", claimNode.Payload.Content, claimNode.Payload.Confidence);
                    break;
            }
        }
    }

    private async Task VerifyAllEvidenceLinksAsync()
    {
        if (_urlVerifier is null) return;

        var evidences = GetAllEvidence()
            .Where(e => !string.IsNullOrEmpty(e.SourceUrl) && !e.IsUrlVerified)
            .ToList();

        if (evidences.Count == 0) return;

        var results = await _urlVerifier.VerifyAllAsync(evidences).ConfigureAwait(false);

        var nodeByUrl = _dag.Nodes.Values
            .Where(n => !string.IsNullOrEmpty(n.Payload.SourceUrl))
            .ToLookup(n => n.Payload.SourceUrl!, StringComparer.Ordinal);

        foreach (var result in results)
        {
            if (!result.IsValid)
            {
                var evidenceNode = nodeByUrl[result.Url].FirstOrDefault();
                if (evidenceNode is not null)
                {
                    evidenceNode.Payload.TrustLevel = TrustLevel.Unreliable;
                    evidenceNode.Version++;
                    _logger.LogWarning("[验证] 证据降级: {Url} - {Error}", result.Url, result.Error);
                }
            }
        }
    }

    private static DataItem PayloadToDataItem(ReasoningPayload p) => new()
    {
        Id = p.Id,
        Content = p.Content,
        State = p.State,
        Source = p.Source,
        Confidence = p.Confidence,
        CreatedAt = p.CreatedAt,
        VerifiedAt = p.VerifiedAt,
        VerifiedBy = p.VerifiedBy,
        SubmittedBy = p.SubmittedBy,
    };

    private static EvidenceRecord PayloadToEvidence(ReasoningPayload p) => new()
    {
        Id = p.Id,
        Content = p.Content,
        Category = p.Category ?? EvidenceCategory.Documentary,
        TrustLevel = p.TrustLevel ?? TrustLevel.Moderate,
        SubmittedBy = p.SubmittedBy ?? AgentRole.Prosecutor,
        Source = p.Source,
        SourceUrl = p.SourceUrl,
        Weight = p.Weight,
    };

    /// <summary>
    /// 重置推理引擎 — 清空 DAG、恢复预算、重置视锥
    /// </summary>
    public void Reset()
    {
        var nodeIds = _dag.Nodes.Keys.ToList();
        foreach (var id in nodeIds)
        {
            _dag.RemoveNode(id);
        }

        _adversarialRoundCount = 0;
        _tokensUsed = 0;
        _roundsBudget = _options.MaxAdversarialRounds;
        _tokensBudget = _options.MaxTokens;
        _lastRunAt = null;

        foreach (AgentRole role in Enum.GetValues<AgentRole>())
        {
            _coneOrchestrator.RegisterRole(role, _options.ConeWindowSize);
        }

        _logger.LogInformation("[重置] 推理引擎已重置 — DAG清空，预算恢复，视锥重置");
    }
}
