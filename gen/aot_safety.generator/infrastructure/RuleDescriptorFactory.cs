namespace AotSafety.Generator.Infrastructure;

/// <summary>
/// 诊断描述符工厂 — 从 [AnalyzerRule] 特性提取元数据,创建 DiagnosticDescriptor。
/// 消除每个规则手动 new DiagnosticDescriptor(...) 的样板代码。
/// CreateAll 返回 Dictionary(Id → Descriptor),供多描述符规则按 Id 查找。
/// CreateFirst 返回第一个 descriptor,供单描述符规则基类使用。
/// </summary>
public static class RuleDescriptorFactory {
    public static IReadOnlyDictionary<string, DiagnosticDescriptor> CreateAll(Type ruleType) {
        var attrs = ruleType.GetCustomAttributes<AnalyzerRuleAttribute>();
        return attrs.ToDictionary(
            attr => attr.Id,
            attr => new DiagnosticDescriptor(
                attr.Id,
                attr.Title,
                attr.Description,
                attr.Category,
                attr.Severity,
                attr.IsEnabledByDefault,
                attr.HelpLinkUri,
                attr.IsCompilationEnd ? WellKnownDiagnosticTags.CompilationEnd : null),
            StringComparer.Ordinal);
    }

    public static IReadOnlyDictionary<string, DiagnosticDescriptor> CreateAll<T>() where T : IAnalyzerRule {
        return CreateAll(typeof(T));
    }

    public static DiagnosticDescriptor CreateFirst(Type ruleType) {
        return CreateAll(ruleType).Values.First();
    }

    public static DiagnosticDescriptor CreateFirst<T>() where T : IAnalyzerRule {
        return CreateFirst(typeof(T));
    }
}
