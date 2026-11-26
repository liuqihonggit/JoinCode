namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 当前 agent 的工具域快照 — 由 broker 按会话 token 返回，供发现侧裁剪（ADR 0168 阶段 4）。
/// <para>空允许集合表示未收紧；拒绝集合优先。发现裁剪只是显示一致，执行判定仍由 broker fail-closed。</para>
/// </summary>
public sealed record CliToolScopeSnapshot {
    /// <summary>允许工具（协议名，可能与 CLI 工具 ID 风格不同，比较时归一化）。</summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = [];

    /// <summary>拒绝工具；支持 <c>*</c> 全拒。</summary>
    public IReadOnlyList<string> DeniedTools { get; init; } = [];
}

/// <summary>
/// 工具域匹配器 — 与 broker <c>EvaluateScopeOnly</c> 同一归一化口径（去前导 /、去下划线、小写）。
/// <para>供 tree/find/help 与首轮组目录共用，避免各处各写一套「算不算允许」。</para>
/// </summary>
public sealed class CliToolScopeMatcher {
    private readonly HashSet<string> _allowed;
    private readonly HashSet<string> _denied;

    /// <summary>从快照构造；null 或空规则都视为未收紧。</summary>
    /// <param name="snapshot">工具域快照，null 表示未进入授权会话。</param>
    public CliToolScopeMatcher(CliToolScopeSnapshot? snapshot) {
        _allowed = (snapshot?.AllowedTools ?? []).Select(Normalize).ToHashSet(StringComparer.Ordinal);
        _denied = (snapshot?.DeniedTools ?? []).Select(Normalize).ToHashSet(StringComparer.Ordinal);
        HasRestriction = _allowed.Count > 0 || _denied.Count > 0;
    }

    /// <summary>是否存在任何收紧规则；false 时 <see cref="IsVisible"/> 恒为 true。</summary>
    public bool HasRestriction { get; }

    /// <summary>判断工具是否对当前 agent 可见。</summary>
    /// <param name="toolId">工具 ID。</param>
    /// <param name="aliases">可选别名，按等价可调用处理。</param>
    /// <returns>可见为 true。</returns>
    public bool IsVisible(string toolId, IReadOnlyList<string>? aliases = null) {
        if (!HasRestriction) return true;
        if (_denied.Contains("*") || _denied.Contains(Normalize(toolId))) return false;
        if (aliases is not null && aliases.Any(alias => _denied.Contains(Normalize(alias)))) return false;
        if (_allowed.Count == 0) return true;
        return _allowed.Contains(Normalize(toolId))
            || (aliases is not null && aliases.Any(alias => _allowed.Contains(Normalize(alias))));
    }

    /// <summary>统计一组工具中可见的数量。</summary>
    /// <param name="toolIds">工具 ID 集合。</param>
    /// <returns>可见数。</returns>
    public int CountVisible(IReadOnlyList<string> toolIds) =>
        HasRestriction ? toolIds.Count(id => IsVisible(id)) : toolIds.Count;

    private static string Normalize(string name) =>
        name.Trim().TrimStart('/').Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
}

/// <summary>
/// 按工具域快照裁剪 CLI 节点目录，使 tree/find/help 与执行门禁消费同一有效域。
/// </summary>
public static class CliToolScopeFilter {
    /// <summary>裁剪节点集合；快照为 null（无会话/未收紧）时原样返回。</summary>
    /// <param name="nodes">已按 profile 过滤的节点。</param>
    /// <param name="snapshot">工具域快照，null 表示不裁剪。</param>
    /// <returns>只含可见工具及非空分组。</returns>
    public static IReadOnlyList<CliToolDescriptor> Apply(
        IReadOnlyList<CliToolDescriptor> nodes,
        CliToolScopeSnapshot? snapshot) {
        ArgumentNullException.ThrowIfNull(nodes);
        if (snapshot is null) return nodes;

        var matcher = new CliToolScopeMatcher(snapshot);
        if (!matcher.HasRestriction) return nodes;

        return nodes
            .Select(node => node.HasToolEntries
                ? node with {
                    Tools = node.Tools.Where(entry => matcher.IsVisible(entry.Id, entry.Aliases)).ToArray()
                }
                : node)
            .Where(node => !node.HasToolEntries
                ? matcher.IsVisible(node.Id)
                : node.Tools.Length > 0)
            .ToArray();
    }
}
