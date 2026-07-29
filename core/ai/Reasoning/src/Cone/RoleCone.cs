namespace JoinCode.Reasoning.Cone;

/// <summary>
/// 角色视锥 — 每个角色的有限上下文窗口，管理片段的折叠与展开
/// </summary>
public sealed class RoleCone
{
    /// <summary>
    /// 角色名称
    /// </summary>
    public required AgentRole RoleName { get; init; }

    /// <summary>
    /// 视锥窗口大小 — 同时可见的最大片段数
    /// </summary>
    public int MaxVisibleFragments { get; init; } = 5;

    /// <summary>
    /// 折叠分支数阈值 — 分支数超过此值时自动生成桥接摘要
    /// </summary>
    public int FoldBranchThreshold { get; init; } = 3;

    /// <summary>
    /// 折叠深度阈值 — 深度超过此值时自动折叠
    /// </summary>
    public int FoldDepthThreshold { get; init; } = 5;

    /// <summary>
    /// 当前激活的片段ID列表（有序，按载入顺序）
    /// </summary>
    public List<string> ActiveFragmentIds { get; } = [];

    /// <summary>
    /// 所有历史片段（可能被折叠）
    /// </summary>
    public Dictionary<string, ObservationFragment> AllFragments { get; } = [];

    /// <summary>
    /// 添加新片段到视锥（自动管理折叠）
    /// </summary>
    public void AddFragment(ObservationFragment fragment)
    {
        fragment.LoadOrder = AllFragments.Count + 1;
        AllFragments[fragment.FragmentId] = fragment;
        ActiveFragmentIds.Add(fragment.FragmentId);

        if (ActiveFragmentIds.Count > MaxVisibleFragments)
        {
            FoldOldestFragment();
        }
    }

    /// <summary>
    /// 折叠最旧片段 — 生成摘要但保留数据
    /// </summary>
    public void FoldOldestFragment()
    {
        if (ActiveFragmentIds.Count == 0) return;

        var oldestId = ActiveFragmentIds[0];
        if (!AllFragments.TryGetValue(oldestId, out var oldest)) return;

        oldest.FoldedSummary = FormFoldedSummary(oldest);
        oldest.IsExpanded = false;

        ActiveFragmentIds.RemoveAt(0);
    }

    /// <summary>
    /// 按条件展开某个片段（渐进式披露）
    /// </summary>
    public ObservationFragment? ExpandFragment(string fragmentId, string triggerCondition)
    {
        if (!AllFragments.TryGetValue(fragmentId, out var fragment))
            return null;

        if (string.IsNullOrEmpty(fragment.ExpandCondition) ||
            fragment.ExpandCondition.Contains(triggerCondition, StringComparison.OrdinalIgnoreCase) ||
            triggerCondition == "*")
        {
            fragment.IsExpanded = true;
            if (!ActiveFragmentIds.Contains(fragmentId))
            {
                ActiveFragmentIds.Add(fragmentId);
            }
            return fragment;
        }

        return null;
    }

    /// <summary>
    /// 获取当前视锥的LLM友好输入（只含可见片段）
    /// </summary>
    public string GetConeContext()
    {
        var visible = AllFragments
            .Where(kv => kv.Value.IsExpanded || ActiveFragmentIds.Contains(kv.Key))
            .OrderBy(kv => kv.Value.LoadOrder)
            .Select(kv => kv.Value);

        return string.Join("\n---\n", visible.Select(f =>
            $"ID:{f.FragmentId}\n" +
            $"角色:{f.RoleChain}\n" +
            $"摘要:{f.FoldedSummary}\n" +
            $"指纹结论:{f.Fingerprint.OutputConclusion}\n" +
            $"置信度:{f.Fingerprint.Confidence:F2}\n" +
            $"原始文:{(f.RawText.Length > 50 ? f.RawText[..50] + "..." : f.RawText)}"));
    }

    /// <summary>
    /// 获取当前视锥中所有激活片段的结论列表
    /// </summary>
    public IReadOnlyList<string> GetActiveConclusions()
    {
        return ActiveFragmentIds
            .Where(id => AllFragments.ContainsKey(id))
            .Select(id => AllFragments[id].Fingerprint.OutputConclusion)
            .ToList();
    }

    private static string FormFoldedSummary(ObservationFragment fragment)
    {
        return $"[折叠] {fragment.Fingerprint.OutputConclusion} -> 置信度:{fragment.Fingerprint.Confidence:F2}";
    }
}
