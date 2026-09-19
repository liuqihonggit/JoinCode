namespace AotSafety.Generator.Infrastructure;

/// <summary>
/// 诊断描述符工厂 — 从 [AnalyzerRule] 特性提取元数据,创建 DiagnosticDescriptor。
/// 消除每个规则手动 new DiagnosticDescriptor(...) 的样板代码。
/// </summary>
public static class RuleDescriptorFactory {
    public static DiagnosticDescriptor Create(Type ruleType) {
        var attr = ruleType.GetCustomAttribute<AnalyzerRuleAttribute>()
            ?? throw new InvalidOperationException(
                $"Type {ruleType.FullName} missing [AnalyzerRule] attribute.");
        return new DiagnosticDescriptor(
            attr.Id,
            attr.Title,
            attr.Description,
            attr.Category,
            attr.Severity,
            attr.IsEnabledByDefault,
            attr.HelpLinkUri);
    }

    public static DiagnosticDescriptor Create<T>() where T : IAnalyzerRule {
        return Create(typeof(T));
    }
}
