namespace JoinCode.Abstractions.Composition.Profile;

/// <summary>
/// 消费点声明语义 — 「未声明消费点 = 全开；声明了 = 白名单」。
/// <para>
/// TASK100 S4：能力（capability）与命令表面（command-group）这类契约的共同点是
/// 「节点存在即生效」，与中间件「按顺序叠加」不同 —— 它们需要回答的问题只有一个是/否：
/// 这个节点在当前 profile 里被声明了吗。
/// </para>
/// <para>
/// 为什么把这套语义收在一处：若每个消费端各写一份默认值，最终必然出现
/// 「CLI 认为全开、引擎认为全关」这种两套事实。此处的规则是唯一的：
/// </para>
/// <list type="number">
/// <item>profile 里根本没有这个消费点 → 全开（向后兼容：老 profile 不必逐条补声明）。</item>
/// <item>消费点存在 → 只有列出的节点启用，其余视为「未启用」（白名单）。</item>
/// </list>
/// </summary>
public static class ConsumerDeclaration {
    /// <summary>
    /// 取消费点声明的节点 ID 集合。
    /// </summary>
    /// <param name="profile">已解析的 profile；<see langword="null"/> 表示尚无 profile 可用。</param>
    /// <param name="consumerId">消费点 ID。</param>
    /// <returns>声明的节点 ID 集合；消费点未声明时返回 <see langword="null"/>（语义 = 全开）。</returns>
    public static IReadOnlySet<string>? DeclaredIds(ResolvedProfile? profile, string consumerId) {
        ArgumentException.ThrowIfNullOrEmpty(consumerId);
        if (profile is null || !profile.Consumers.TryGetValue(consumerId, out var consumer))
            return null;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in consumer.Nodes)
            ids.Add(node.Id);
        return ids;
    }

    /// <summary>
    /// 判断某节点在当前 profile 的某消费点下是否启用。
    /// </summary>
    /// <param name="profile">已解析的 profile。</param>
    /// <param name="consumerId">消费点 ID。</param>
    /// <param name="nodeId">节点 ID（kebab-case）。</param>
    /// <returns>启用返回 <see langword="true"/>；消费点未声明时视为全开。</returns>
    public static bool IsEnabled(ResolvedProfile? profile, string consumerId, string nodeId) {
        ArgumentException.ThrowIfNullOrEmpty(nodeId);
        var declared = DeclaredIds(profile, consumerId);
        return declared is null || declared.Contains(nodeId);
    }

    /// <summary>
    /// 取消费点声明的节点顺序（priority 降序 = 先声明者优先）。
    /// </summary>
    /// <para>
    /// 供「顺序有意义」的消费点（如沙箱降级链）在没有 DI 实例时也能拿到一份确定的顺序；
    /// 消费点未声明时返回空数组，交由调用方退回默认顺序。
    /// </para>
    /// <param name="profile">已解析的 profile。</param>
    /// <param name="consumerId">消费点 ID。</param>
    /// <returns>priority 降序的节点 ID 数组。</returns>
    public static string[] OrderedIds(ResolvedProfile? profile, string consumerId) {
        ArgumentException.ThrowIfNullOrEmpty(consumerId);
        if (profile is null || !profile.Consumers.TryGetValue(consumerId, out var consumer))
            return [];

        return [.. consumer.Nodes.OrderByDescending(static node => node.Priority).Select(static node => node.Id)];
    }
}
