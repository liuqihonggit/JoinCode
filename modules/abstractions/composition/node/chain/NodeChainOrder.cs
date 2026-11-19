namespace JoinCode.Abstractions.Composition;

/// <summary>
/// 节点链排序 — 让 profile 的消费点声明成为执行顺序的唯一来源。
/// <para>
/// TASK100：此前守卫/修正器的顺序来自 <c>orders.yaml</c> 的列表序号，
/// 由实现类的 <c>Priority</c> 属性换算，改顺序要重编译，profile 完全看不见这条链。
/// 现在顺序取自 <c>profiles/*.yaml</c> 的消费点（contract: guard），
/// <c>orders.yaml</c> 退化为生成期校验表。
/// </para>
/// <para>
/// 降级策略是**安全优先**：profile 缺失（单测、无宿主场景）时退回 Priority 降序；
/// profile 声明了但实现不存在 → 直接抛异常（不能静默漏掉一条守卫）；
/// 实现存在但未声明 → 追加到链尾并打点（不能因为漏登记就让守卫失效）。
/// </para>
/// </summary>
public static class NodeChainOrder {
    /// <summary>
    /// 按 profile 消费点声明排序节点。
    /// </summary>
    /// <typeparam name="T">节点类型（守卫/修正器等）。</typeparam>
    /// <param name="items">DI 收集到的实现集合。</param>
    /// <param name="profiles">Profile 管理器；为 null 时退回 Priority 降序。</param>
    /// <param name="consumerId">消费点 ID（如 command-guard-chain）。</param>
    /// <param name="nameOf">取实现名（用于与 kebab 节点 ID 比对）。</param>
    /// <param name="priorityOf">取实现自带位次（降级排序用）。</param>
    /// <param name="what">链的中文名（错误提示用）。</param>
    /// <returns>排好序的节点数组。</returns>
    public static T[] Order<T>(
        IEnumerable<T> items,
        IProfileManager? profiles,
        string consumerId,
        Func<T, string> nameOf,
        Func<T, int> priorityOf,
        string what) {
        var all = items as T[] ?? items.ToArray();
        if (all.Length == 0) return all;

        if (profiles is null
            || !profiles.Current.Consumers.TryGetValue(consumerId, out var consumer)
            || consumer.Nodes.Count == 0) {
            return all.OrderByDescending(priorityOf).ToArray();
        }

        var ordered = new List<T>(all.Length);
        var seen = new HashSet<T>();
        foreach (var node in consumer.Nodes.OrderByDescending(static n => n.Priority)) {
            var expected = KebabToPascal(node.Id);
            var match = all.FirstOrDefault(item => string.Equals(nameOf(item), expected, StringComparison.OrdinalIgnoreCase));
            if (match is null) {
                throw new InvalidOperationException(
                    $"{what} '{node.Id}' 已在 profile 消费点 '{consumerId}' 声明，但没有任何实现与之匹配。" +
                    $"期望实现名: {expected}。" +
                    $"已加载实现: {string.Join(", ", all.Select(nameOf).Order(StringComparer.Ordinal))}。" +
                    $"正确写法: 实现名用 PascalCase 对应 kebab 节点 ID，或从 profile 删除该声明。");
            }
            if (seen.Add(match)) ordered.Add(match);
        }

        foreach (var extra in all) {
            if (!seen.Add(extra)) continue;
            JoinCode.Abstractions.Utils.Diagnostics.Diag.WriteLine($"[ORDER] {consumerId}: '{nameOf(extra)}' 未在 profile 声明，已追加到链尾（请补 profile 声明）");
            ordered.Add(extra);
        }

        return ordered.ToArray();
    }

    /// <summary>kebab-case 节点 ID → PascalCase 实现名（git-commit-guard → GitCommitGuard）。</summary>
    /// <param name="id">kebab 形式的节点 ID。</param>
    /// <returns>PascalCase 名称。</returns>
    internal static string KebabToPascal(string id) =>
        string.Concat(id.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
