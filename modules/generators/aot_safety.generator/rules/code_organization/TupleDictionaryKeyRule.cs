namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC10009: 代码规范 — 禁止元组做字典 key，改用字符串 key + record value。
/// 元组 key 无法按名称访问、可读性差、易写错顺序。string key + record value 可按属性名访问，类型安全。
/// 检测: Dictionary/(I/Frozen/Concurrent/Immutable/Sorted/ReadOnly/IReadOnly)Dictionary 的第一个泛型参数为元组类型。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC10009",
    Title = "代码规范: 禁止元组做字典 key",
    Description = "字典 {0} 使用元组作为 key，可读性差且易写错顺序。改用字符串 key + record value: 1) string key 可按名称访问; 2) record value 类型安全且可按属性名访问; 3) 避免 (a,b) 顺序错误导致的静默 bug。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Tuple dictionary keys are error-prone: 1) no named access, poor readability; 2) easy to swap (a,b) order causing silent bugs; 3) value tuples have surprising equality semantics. Prefer string key + record value for named, type-safe access.")]
public sealed class TupleDictionaryKeyRule : AnalyzerRuleBase<TupleDictionaryKeyRule> {
    private static readonly HashSet<string> DictionaryTypeNames = new(StringComparer.Ordinal)
    {
        "Dictionary", "IDictionary",
        "FrozenDictionary", "FrozenDictionary",
        "ConcurrentDictionary",
        "ImmutableDictionary",
        "SortedDictionary", "SortedList",
        "ReadOnlyDictionary", "IReadOnlyDictionary",
    };

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeGenericName, SyntaxKind.GenericName);
    }

    private static void AnalyzeGenericName(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        var genericName = (GenericNameSyntax)ctx.Node;
        if (!DictionaryTypeNames.Contains(genericName.Identifier.ValueText)) return;
        var typeArgs = genericName.TypeArgumentList.Arguments;
        if (typeArgs.Count < 2) return;
        if (typeArgs[0] is not TupleTypeSyntax) return;
        var fullTypeName = genericName.ToString();
        ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, genericName.GetLocation(), fullTypeName));
    }
}
