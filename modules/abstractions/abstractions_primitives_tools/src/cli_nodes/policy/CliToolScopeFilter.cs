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

        var allowed = snapshot.AllowedTools.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var denied = snapshot.DeniedTools.Select(Normalize).ToHashSet(StringComparer.Ordinal);

        return nodes
            .Select(node => node.HasToolEntries
                ? node with {
                    Tools = node.Tools.Where(entry => IsVisible(entry.Id, entry.Aliases, allowed, denied)).ToArray()
                }
                : node)
            .Where(node => !node.HasToolEntries
                ? IsVisible(node.Id, [], allowed, denied)
                : node.Tools.Length > 0)
            .ToArray();
    }

    private static bool IsVisible(
        string id,
        IReadOnlyList<string> aliases,
        HashSet<string> allowed,
        HashSet<string> denied) {
        if (denied.Contains("*")
            || denied.Contains(Normalize(id))
            || aliases.Any(alias => denied.Contains(Normalize(alias)))) {
            return false;
        }
        if (allowed.Count == 0) return true;
        return allowed.Contains(Normalize(id)) || aliases.Any(alias => allowed.Contains(Normalize(alias)));
    }

    private static string Normalize(string name) =>
        name.Trim().TrimStart('/').Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
}
