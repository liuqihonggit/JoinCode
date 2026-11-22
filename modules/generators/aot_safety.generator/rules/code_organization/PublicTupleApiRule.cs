namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC10011: 公开 API 禁止元组 — 公开接口/回调签名/DTO 字段不得使用 <c>ValueTuple</c>，
/// 必须定义具名 <c>record</c>/<c>class</c> DTO；元组仅限方法内部临时计算。
/// <para>修复导向: 把 <c>(T1, T2)</c> 替换为具名 DTO；若只是内部临时变量，保持 <c>internal</c>/局部作用域即可。</para>
/// </summary>
[AnalyzerRule(
    AnalyzerId = "CodeOrganization",
    Id = "JCC10011",
    Title = "公开 API 禁止元组",
    Description = "公开成员 '{0}' 的签名包含元组类型 '{1}'。公开接口/回调签名/DTO 字段必须使用具名 record/class，元组仅限内部临时计算。",
    Category = "CodeStyle",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "Public API must not expose ValueTuple. Named record/class DTOs carry field names in metadata and docs; tuples erase names, break source readability, and make refactoring brittle. Keep tuples for private locals only.",
    IsCompilationEnd = false)]
public sealed class PublicTupleApiRule : AnalyzerRuleBase<PublicTupleApiRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        if (projectContext.IsTest) return;

        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        context.RegisterSymbolAction(AnalyzeEvent, SymbolKind.Event);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context) {
        if (context.CancellationToken.IsCancellationRequested) return;
        if (context.Symbol is not IMethodSymbol method) return;
        if (method.IsImplicitlyDeclared) return;
        if (method.AssociatedSymbol is not null) return;
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.DelegateInvoke)) return;
        if (!IsExternallyVisible(method)) return;

        if (TryFindTuple(method.ReturnType, out var tupleType)) {
            Report(context, method, tupleType);
            return;
        }

        foreach (var parameter in method.Parameters) {
            if (TryFindTuple(parameter.Type, out tupleType)) {
                Report(context, method, tupleType);
                return;
            }
        }
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context) {
        if (context.CancellationToken.IsCancellationRequested) return;
        if (context.Symbol is not IPropertySymbol property) return;
        if (property.IsImplicitlyDeclared || !IsExternallyVisible(property)) return;
        if (TryFindTuple(property.Type, out var tupleType)) {
            Report(context, property, tupleType);
        }
    }

    private static void AnalyzeField(SymbolAnalysisContext context) {
        if (context.CancellationToken.IsCancellationRequested) return;
        if (context.Symbol is not IFieldSymbol field) return;
        if (field.IsImplicitlyDeclared || field.IsConst || !IsExternallyVisible(field)) return;
        if (TryFindTuple(field.Type, out var tupleType)) {
            Report(context, field, tupleType);
        }
    }

    private static void AnalyzeEvent(SymbolAnalysisContext context) {
        if (context.CancellationToken.IsCancellationRequested) return;
        if (context.Symbol is not IEventSymbol @event) return;
        if (@event.IsImplicitlyDeclared || !IsExternallyVisible(@event)) return;
        if (TryFindTuple(@event.Type, out var tupleType)) {
            Report(context, @event, tupleType);
        }
    }

    private static void Report(SymbolAnalysisContext context, ISymbol symbol, string tupleType) {
        var location = symbol.Locations.FirstOrDefault(static item => item.IsInSource);
        if (location is null) return;
        context.ReportDiagnostic(Diagnostic.Create(Descriptor, location, symbol.Name, tupleType));
    }

    /// <summary>
    /// 判定符号是否构成对外可见 API：自身与所有外层类型都必须是 public/protected。
    /// </summary>
    private static bool IsExternallyVisible(ISymbol symbol) {
        if (!IsVisibleAccessibility(symbol.DeclaredAccessibility)) return false;

        var containingType = symbol.ContainingType;
        while (containingType is not null) {
            if (!IsVisibleAccessibility(containingType.DeclaredAccessibility)) return false;
            containingType = containingType.ContainingType;
        }

        return true;
    }

    private static bool IsVisibleAccessibility(Accessibility accessibility) {
        return accessibility is Accessibility.Public
            or Accessibility.Protected
            or Accessibility.ProtectedOrInternal;
    }

    /// <summary>
    /// 递归查找类型中的元组（含泛型实参、数组元素），深度上限防止异常类型结构导致栈溢出。
    /// </summary>
    private static bool TryFindTuple(ITypeSymbol? type, out string tupleType) {
        return TryFindTuple(type, depth: 0, out tupleType);
    }

    private static bool TryFindTuple(ITypeSymbol? type, int depth, out string tupleType) {
        tupleType = "";
        if (type is null || depth > 6) return false;

        switch (type) {
            case INamedTypeSymbol named when named.IsTupleType:
                tupleType = named.ToDisplayString();
                return true;
            case INamedTypeSymbol named:
                foreach (var argument in named.TypeArguments) {
                    if (TryFindTuple(argument, depth + 1, out tupleType)) return true;
                }
                return false;
            case IArrayTypeSymbol array:
                return TryFindTuple(array.ElementType, depth + 1, out tupleType);
            default:
                return false;
        }
    }
}
