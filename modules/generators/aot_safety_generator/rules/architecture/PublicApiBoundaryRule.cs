namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC12006: 公开类型越过 API 可见性 — 域默认 <c>JccApiVisibility=internal</c> 时，
/// 项目内不得声明对外可见的类型；确实需要对外时，在 csproj 显式覆盖为 <c>public</c>。
/// <para>修复导向: 优先把类型改为 <c>internal</c>；只有被跨域引用的 51 个对外项目才允许
/// <c>&lt;JccApiVisibility&gt;public&lt;/JccApiVisibility&gt;</c>，并在 ADR/评审中登记 API 边界。</para>
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AotSafety",
    Id = "JCC12006",
    Title = "公开类型越过 API 可见性",
    Description = "类型 '{0}' 在 JccApiVisibility=internal 的项目中声明为 public。域默认不对外暴露类型；需要对外时在 csproj 覆盖 JccApiVisibility=public 并登记 API 边界。",
    Category = "Architecture",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "A contract project with JccApiVisibility=internal must not declare public types. Mark the type internal, or opt the project into JccApiVisibility=public with an explicit API-boundary review.",
    IsCompilationEnd = false)]
public sealed class PublicApiBoundaryRule : AnalyzerRuleBase<PublicApiBoundaryRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        if (projectContext.IsTest) return;
        if (!ArchitectureDomainCatalog.IsContractProject(projectContext.ProjectDirectory)) return;
        if (!IsInternalApi(projectContext.ApiVisibility)) return;

        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context) {
        if (context.CancellationToken.IsCancellationRequested) return;
        if (context.Symbol is not INamedTypeSymbol symbol) return;
        if (!IsEffectivelyPublic(symbol)) return;

        var location = symbol.Locations.FirstOrDefault(static item => item.IsInSource);
        if (location is null) return;

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, location, symbol.Name));
    }

    private static bool IsInternalApi(string apiVisibility) {
        return apiVisibility.Trim().Equals("internal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEffectivelyPublic(INamedTypeSymbol symbol) {
        var current = symbol;
        while (current is not null) {
            if (current.DeclaredAccessibility != Accessibility.Public) return false;
            current = current.ContainingType;
        }
        return true;
    }
}
