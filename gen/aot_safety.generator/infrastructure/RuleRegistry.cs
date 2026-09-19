namespace AotSafety.Generator.Infrastructure;

/// <summary>
/// 规则注册表 — 反射扫描程序集,发现所有 [AnalyzerRule] 标记的 IAnalyzerRule 实现,
/// 用 Dictionary&lt;string, IAnalyzerRule&gt; (map) 持有,key = 规则 Id,value = 规则实例。
/// 分析器通过 Map/All/Get 获取规则,无需手动逐个注册。
/// </summary>
public static class RuleRegistry {
    private static readonly IReadOnlyDictionary<string, IAnalyzerRule> RuleMap = BuildRuleMap();
    private static readonly IReadOnlyList<IAnalyzerRule> RuleList = RuleMap.Values.ToList();

    /// <summary>
    /// 规则 map — key = [AnalyzerRule].Id (如 "JCC3008"),value = 规则实例。
    /// </summary>
    public static IReadOnlyDictionary<string, IAnalyzerRule> Map => RuleMap;

    /// <summary>
    /// 所有规则列表 — 顺序遍历注册。
    /// </summary>
    public static IReadOnlyList<IAnalyzerRule> All => RuleList;

    /// <summary>
    /// 按 Id 查找规则 — 供主分析器或测试按需获取特定规则。
    /// </summary>
    public static IAnalyzerRule? Get(string id) {
        return RuleMap.TryGetValue(id, out var rule) ? rule : null;
    }

    private static IReadOnlyDictionary<string, IAnalyzerRule> BuildRuleMap() {
        var assembly = typeof(IAnalyzerRule).Assembly;
        var map = new Dictionary<string, IAnalyzerRule>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes()) {
            if (type.IsAbstract || type.IsInterface) continue;
            if (type.GetCustomAttribute<AnalyzerRuleAttribute>() is not { } attr) continue;
            if (!typeof(IAnalyzerRule).IsAssignableFrom(type)) continue;
            var rule = (IAnalyzerRule)Activator.CreateInstance(type)!;
            map[attr.Id] = rule;
        }
        return map;
    }
}
