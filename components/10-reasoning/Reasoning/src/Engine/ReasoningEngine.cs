namespace JoinCode.Reasoning.Engine;

/// <summary>
/// 推理引擎 — 基于 DAG 的三权分立结构化推理核心
/// </summary>
public sealed class ReasoningEngine : IReasoningEngine
{
    private readonly Dag<ReasoningPayload> _dag = new();
    private readonly Dictionary<AgentRole, IReasoningAgent> _agents;
    private readonly ILogger<ReasoningEngine> _logger;
    private DateTime? _lastRunAt;

    public ReasoningEngine(
        IEnumerable<IReasoningAgent> agents,
        ILogger<ReasoningEngine> logger)
    {
        _logger = logger;
        _agents = agents.ToDictionary(a => a.Role, a => a);
    }

    /// <summary>
    /// 暴露内部 DAG 供 Agent 只读访问
    /// </summary>
    public Dag<ReasoningPayload> Dag => _dag;

    public async Task AddAssumptionsAsync(IReadOnlyList<DataItem> assumptions, CancellationToken ct)
    {
        foreach (var item in assumptions)
        {
            if (item.State != DataState.Assumption)
                throw new InvalidOperationException($"数据项必须以Assumption状态进入，当前: {item.State}");

            if (_dag.Nodes.Values.Any(n => n.Payload.Content == item.Content && n.Payload.State != DataState.Rejected))
                throw new InvalidOperationException($"内容已存在: {item.Content}");

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
                throw new InvalidOperationException(result.ErrorMessage ?? "添加节点失败");

            _logger.LogInformation("[假定] {Content} (ID:{Id})", item.Content, item.Id);
        }

        await RunAdversarialProcessAsync(ct).ConfigureAwait(false);
    }

    public IReadOnlyList<DataItem> GetAllItems()
    {
        return _dag.Nodes.Values
            .Select(n => PayloadToDataItem(n.Payload))
            .ToList();
    }

    public IReadOnlyList<EvidenceRecord> GetAllEvidence()
    {
        return _dag.Nodes.Values
            .Where(n => n.Payload.Type == ReasoningNodeType.Evidence)
            .Select(n => PayloadToEvidence(n.Payload))
            .ToList();
    }

    public IReadOnlyList<DataItem> GetFacts()
    {
        return _dag.Nodes.Values
            .Where(n => n.Payload.State == DataState.Fact)
            .Select(n => PayloadToDataItem(n.Payload))
            .ToList();
    }

    public void AddEvidence(EvidenceRecord evidence, string claimId)
    {
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
    }

    public void AddCounterEvidence(EvidenceRecord evidence, string claimId)
    {
        AddEvidence(evidence, claimId);
    }

    public async Task RunAdversarialProcessAsync(CancellationToken ct)
    {
        var context = new ReasoningContext
        {
            AllItems = GetAllItems(),
            AllEvidence = GetAllEvidence(),
            Dag = _dag,
        };

        if (_agents.TryGetValue(AgentRole.Prosecutor, out var prosecutor))
        {
            var prosAction = await prosecutor.ReasonAsync(context, ct).ConfigureAwait(false);
            ApplyAgentAction(prosAction);
        }

        context = new ReasoningContext
        {
            AllItems = GetAllItems(),
            AllEvidence = GetAllEvidence(),
            Dag = _dag,
        };

        if (_agents.TryGetValue(AgentRole.Defender, out var defender))
        {
            var defAction = await defender.ReasonAsync(context, ct).ConfigureAwait(false);
            ApplyAgentAction(defAction);
        }

        context = new ReasoningContext
        {
            AllItems = GetAllItems(),
            AllEvidence = GetAllEvidence(),
            Dag = _dag,
        };

        if (_agents.TryGetValue(AgentRole.Judge, out var judge))
        {
            var judgeAction = await judge.ReasonAsync(context, ct).ConfigureAwait(false);
            ApplyVerdicts(judgeAction.Verdicts);
        }

        _lastRunAt = DateTime.UtcNow;
    }

    public ReasoningSummary GetSummary() => new()
    {
        TotalAssumptions = _dag.Nodes.Values.Count(n => n.Payload.State == DataState.Assumption),
        TotalVerified = _dag.Nodes.Values.Count(n => n.Payload.State == DataState.Verified),
        TotalFacts = _dag.Nodes.Values.Count(n => n.Payload.State == DataState.Fact),
        TotalRejected = _dag.Nodes.Values.Count(n => n.Payload.State == DataState.Rejected),
        TotalPendingEvidence = _dag.Nodes.Values.Count(n => n.Payload.State == DataState.PendingEvidence),
        TotalEvidence = _dag.Nodes.Values.Count(n => n.Payload.Type == ReasoningNodeType.Evidence),
        LastRunAt = _lastRunAt,
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
                affectedNode.Payload.Confidence = 50;
                affectedNode.Version++;
                _logger.LogInformation("[传播] 裁决降级: {Content}", affectedNode.Payload.Content);
            }
        }
    }

    /// <summary>
    /// 增量重算 — 只重算受影响的子图
    /// </summary>
    public IReadOnlyList<ReasoningPayload> IncrementalRecompute(string changedNodeId)
    {
        var affected = _dag.GetAffectedSubgraph(changedNodeId);
        return affected.Select(n => n.Payload).ToList();
    }

    private void ApplyAgentAction(AgentAction action)
    {
        foreach (var evidence in action.Evidence)
        {
            AddEvidence(evidence, action.AffectedClaimIds.FirstOrDefault() ?? string.Empty);
        }

        foreach (var counter in action.CounterEvidence)
        {
            AddCounterEvidence(counter, action.AffectedClaimIds.FirstOrDefault() ?? string.Empty);
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
                Weight = 1.0,
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
                    claimNode.Payload.Confidence = 10;
                    claimNode.Version++;
                    _logger.LogInformation("[法官] 驳回: {Content}", claimNode.Payload.Content);
                    break;
                case VerdictDecision.Pending:
                    claimNode.Payload.State = DataState.PendingEvidence;
                    claimNode.Version++;
                    _logger.LogInformation("[法官] 等待更多证据: {Content}", claimNode.Payload.Content);
                    break;
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
}
