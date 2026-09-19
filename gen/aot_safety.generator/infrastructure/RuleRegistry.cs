namespace AotSafety.Generator.Infrastructure;

/// <summary>
/// 规则注册表 — 反射扫描程序集,发现所有 [AnalyzerRule] 标记的 IAnalyzerRule 实现。
/// 用两层 Dictionary (map) 持有:
///   外层 key = AnalyzerId (如 "AsyncSafety"),value = 该分析器的规则 map
///   内层 key = 规则 Id (如 "JCC3008"),value = 规则实例
/// 分析器通过 GetByAnalyzer(analyzerId) 获取自己的规则,避免注册其他分析器的规则。
/// </summary>
public static class RuleRegistry {
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IAnalyzerRule>> RulesByAnalyzer = BuildRulesByAnalyzer();
    private static readonly IReadOnlyList<IAnalyzerRule> AllRules = RulesByAnalyzer.Values.SelectMany(m => m.Values).ToList();

    /// <summary>
    /// 所有规则列表 — 供 SupportedDiagnostics 收集。
    /// </summary>
    public static IReadOnlyList<IAnalyzerRule> All => AllRules;

    /// <summary>
    /// 按 AnalyzerId 获取规则 map — key = 规则 Id,value = 规则实例。
    /// </summary>
    public static IReadOnlyDictionary<string, IAnalyzerRule> GetByAnalyzer(string analyzerId) {
        return RulesByAnalyzer.TryGetValue(analyzerId, out var map)
            ? map
            : new Dictionary<string, IAnalyzerRule>(StringComparer.Ordinal);
    }

    /// <summary>
    /// 按 AnalyzerId 获取规则列表。
    /// </summary>
    public static IReadOnlyList<IAnalyzerRule> GetListByAnalyzer(string analyzerId) {
        return GetByAnalyzer(analyzerId).Values.ToList();
    }

    /// <summary>
    /// 按 Id 查找规则 — 供测试按需获取特定规则。
    /// </summary>
    public static IAnalyzerRule? Get(string analyzerId, string ruleId) {
        var map = GetByAnalyzer(analyzerId);
        return map.TryGetValue(ruleId, out var rule) ? rule : null;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IAnalyzerRule>> BuildRulesByAnalyzer() {
        var assembly = typeof(IAnalyzerRule).Assembly;
        var byAnalyzer = new Dictionary<string, Dictionary<string, IAnalyzerRule>>(StringComparer.Ordinal);

        foreach (var type in assembly.GetTypes()) {
            if (type.IsAbstract || type.IsInterface) continue;
            if (type.GetCustomAttribute<AnalyzerRuleAttribute>() is not { } attr) continue;
            if (!typeof(IAnalyzerRule).IsAssignableFrom(type)) continue;

            var rule = (IAnalyzerRule)Activator.CreateInstance(type)!;

            if (!byAnalyzer.TryGetValue(attr.AnalyzerId, out var ruleMap)) {
                ruleMap = new Dictionary<string, IAnalyzerRule>(StringComparer.Ordinal);
                byAnalyzer[attr.AnalyzerId] = ruleMap;
            }

            if (ruleMap.ContainsKey(attr.Id)) {
                throw new InvalidOperationException(
                    $"Duplicate rule Id '{attr.Id}' in analyzer '{attr.AnalyzerId}'. Type: {type.FullName}");
            }

            ruleMap[attr.Id] = rule;
        }

        return byAnalyzer.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyDictionary<string, IAnalyzerRule>)kvp.Value,
            StringComparer.Ordinal);
    }
}
