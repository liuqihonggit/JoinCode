namespace AotSafety.Generator.Infrastructure;

/// <summary>
/// 规则注册表 — 反射扫描程序集,发现所有 [AnalyzerRule] 标记的 IAnalyzerRule 实现。
/// 用两层 Dictionary (map) 持有:
///   外层 key = AnalyzerId (如 "AsyncSafety"),value = 该分析器的规则 map
///   内层 key = 规则 Id (如 "JCC3008"),value = 规则实例
/// 多描述符规则的每个 Id 都映射到同一个规则实例。
/// 分析器通过 GetByAnalyzer(analyzerId) 获取自己的规则,避免注册其他分析器的规则。
/// </summary>
public static class RuleRegistry {
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IAnalyzerRule>> RulesByAnalyzer = BuildRulesByAnalyzer();
    private static readonly IReadOnlyList<IAnalyzerRule> AllRules = RulesByAnalyzer.Values.SelectMany(m => m.Values.Distinct()).ToList();

    public static IReadOnlyList<IAnalyzerRule> All => AllRules;

    public static IReadOnlyDictionary<string, IAnalyzerRule> GetByAnalyzer(string analyzerId) {
        return RulesByAnalyzer.TryGetValue(analyzerId, out var map)
            ? map
            : new Dictionary<string, IAnalyzerRule>(StringComparer.Ordinal);
    }

    public static IReadOnlyList<IAnalyzerRule> GetListByAnalyzer(string analyzerId) {
        return GetByAnalyzer(analyzerId).Values.Distinct().ToList();
    }

    public static IAnalyzerRule? Get(string analyzerId, string ruleId) {
        var map = GetByAnalyzer(analyzerId);
        return map.TryGetValue(ruleId, out var rule) ? rule : null;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IAnalyzerRule>> BuildRulesByAnalyzer() {
        var assembly = typeof(IAnalyzerRule).Assembly;
        var byAnalyzer = new Dictionary<string, Dictionary<string, IAnalyzerRule>>(StringComparer.Ordinal);
        var seenTypes = new HashSet<Type>();

        foreach (var type in assembly.GetTypes()) {
            if (type.IsAbstract || type.IsInterface) continue;
            var attrs = type.GetCustomAttributes<AnalyzerRuleAttribute>().ToArray();
            if (attrs.Length == 0) continue;
            if (!typeof(IAnalyzerRule).IsAssignableFrom(type)) continue;
            if (!seenTypes.Add(type)) continue;

            var rule = (IAnalyzerRule)Activator.CreateInstance(type)!;

            foreach (var attr in attrs) {
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
        }

        return byAnalyzer.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyDictionary<string, IAnalyzerRule>)kvp.Value,
            StringComparer.Ordinal);
    }
}
