namespace JoinCode.Reasoning.Cone;

/// <summary>
/// 视锥调度器 — 在角色间传递"接口摘要"，管理跨视锥通信和冲突检测
/// </summary>
public sealed class ConeOrchestrator
{
    private readonly Dictionary<AgentRole, RoleCone> _roles = [];

    /// <summary>
    /// 跨角色传递时的置信度衰减系数
    /// </summary>
    public double TransferDecayFactor { get; init; } = 0.9;

    /// <summary>
    /// 注册角色视锥
    /// </summary>
    public void RegisterRole(AgentRole roleName, int maxWindowSize = 5)
    {
        _roles[roleName] = new RoleCone { RoleName = roleName, MaxVisibleFragments = maxWindowSize };
    }

    /// <summary>
    /// 获取角色视锥
    /// </summary>
    public RoleCone? GetRole(AgentRole roleName)
    {
        return _roles.GetValueOrDefault(roleName);
    }

    /// <summary>
    /// 获取所有已注册角色
    /// </summary>
    public IReadOnlyDictionary<AgentRole, RoleCone> GetAllRoles() => _roles;

    /// <summary>
    /// 角色A向角色B传递一个片段（跨视锥通信）— 传递的是摘要而非原文，模拟现实沟通损耗
    /// </summary>
    public ObservationFragment? TransferFragment(AgentRole fromRole, AgentRole toRole, string fragmentId)
    {
        if (!_roles.TryGetValue(fromRole, out var fromCone)) return null;
        if (!_roles.TryGetValue(toRole, out var toCone)) return null;
        if (!fromCone.AllFragments.TryGetValue(fragmentId, out var fragment)) return null;

        var transferred = new ObservationFragment
        {
            FragmentId = Guid.NewGuid().ToString("N")[..8],
            SourceItemId = fragment.SourceItemId,
            RoleChain = toRole,
            RawText = fragment.FoldedSummary,
            Fingerprint = new CognitiveFingerprint
            {
                EntryStimulus = $"来自{fromRole}的认知产物",
                ProcessingPath = $"跨角色传递：{fromRole}->{toRole}",
                OutputConclusion = fragment.Fingerprint.OutputConclusion,
                Confidence = fragment.Fingerprint.Confidence * TransferDecayFactor,
            },
            FoldedSummary = $"[来自{fromRole}] {fragment.FoldedSummary}",
            ExpandCondition = "cross_role_review",
            BackReferences = [fragmentId],
        };

        toCone.AddFragment(transferred);
        return transferred;
    }

    /// <summary>
    /// 检测角色间的视锥冲突 — 差异源于载入顺序不同
    /// </summary>
    public ConeConflictResult DetectConeConflict(AgentRole roleA, AgentRole roleB)
    {
        var coneA = _roles.GetValueOrDefault(roleA);
        var coneB = _roles.GetValueOrDefault(roleB);

        if (coneA is null || coneB is null)
        {
            return new ConeConflictResult { RoleA = roleA, RoleB = roleB, HasConflict = false };
        }

        var aConclusions = coneA.GetActiveConclusions();
        var bConclusions = coneB.GetActiveConclusions();

        return new ConeConflictResult
        {
            RoleA = roleA,
            RoleB = roleB,
            RoleAConclusions = aConclusions,
            RoleBConclusions = bConclusions,
            HasConflict = aConclusions.Count > 0 && bConclusions.Count > 0,
        };
    }

    /// <summary>
    /// 为指定角色从 DAG 数据项创建观察片段
    /// </summary>
    public ObservationFragment CreateFragmentFromItem(AgentRole role, DataItem item, string? entryStimulus = null)
    {
        return new ObservationFragment
        {
            SourceItemId = item.Id,
            RoleChain = role,
            RawText = item.Content,
            Fingerprint = new CognitiveFingerprint
            {
                EntryStimulus = entryStimulus ?? item.Content,
                ProcessingPath = $"{role}观察链",
                OutputConclusion = item.Content,
                Confidence = item.Confidence / 100.0,
                OpenQuestions = [],
            },
            FoldedSummary = $"[摘要] {item.Content} (置信度:{item.Confidence}%)",
            ExpandCondition = "cross_role_review",
        };
    }

    /// <summary>
    /// 为指定角色从证据记录创建观察片段
    /// </summary>
    public ObservationFragment CreateFragmentFromEvidence(AgentRole role, EvidenceRecord evidence)
    {
        return new ObservationFragment
        {
            SourceItemId = evidence.Id,
            RoleChain = role,
            RawText = evidence.Content,
            Fingerprint = new CognitiveFingerprint
            {
                EntryStimulus = evidence.Content,
                ProcessingPath = $"{role}证据链",
                OutputConclusion = evidence.Content,
                Confidence = (int)evidence.TrustLevel / 100.0,
                OpenQuestions = [],
            },
            FoldedSummary = $"[摘要] {evidence.Content} (信任度:{evidence.TrustLevel})",
            ExpandCondition = "cross_role_review",
        };
    }
}

/// <summary>
/// 视锥冲突检测结果
/// </summary>
public sealed class ConeConflictResult
{
    public required AgentRole RoleA { get; init; }
    public required AgentRole RoleB { get; init; }
    public IReadOnlyList<string> RoleAConclusions { get; init; } = [];
    public IReadOnlyList<string> RoleBConclusions { get; init; } = [];
    public bool HasConflict { get; init; }
}
