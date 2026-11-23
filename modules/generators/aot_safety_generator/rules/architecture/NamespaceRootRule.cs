namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC12008: 命名空间根不符 — 契约项目的类型命名空间必须落在
/// <c>JccNamespaceRoots</c> 声明的前缀之下。
/// <para>修复导向: 先把域内命名空间归一，再在域级 Directory.Build.props 写
/// <c>&lt;JccNamespaceRoots&gt;JoinCode.Llm&lt;/JccNamespaceRoots&gt;</c>（分号分隔多根）；
/// 未声明根的项目跳过本规则，避免在 P4 归一前制造噪音。</para>
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AotSafety",
    Id = "JCC12008",
    Title = "命名空间根不符",
    Description = "类型 '{0}' 的命名空间 '{1}' 不在 JccNamespaceRoots 声明的前缀内。应把类型移入 '{2}' 之下，或先归一命名空间再更新契约。",
    Category = "Architecture",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "Contract projects must keep type namespaces under one of the JccNamespaceRoots prefixes. Declare multiple roots with semicolons; leave empty to skip during namespace normalisation.",
    IsCompilationEnd = false)]
public sealed class NamespaceRootRule : AnalyzerRuleBase<NamespaceRootRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        if (projectContext.IsTest) return;
        if (!ArchitectureDomainCatalog.IsContractProject(projectContext.ProjectDirectory)) return;

        var roots = ArchitectureDomainCatalog.SplitList(projectContext.NamespaceRoots);
        if (roots.Count == 0) return;

        context.RegisterSymbolAction(
            symbolContext => Analyze(symbolContext, roots),
            SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context, IReadOnlyList<string> roots) {
        if (context.CancellationToken.IsCancellationRequested) return;
        if (context.Symbol is not INamedTypeSymbol symbol) return;
        if (symbol.ContainingType is not null) return;

        var typeNamespace = symbol.ContainingNamespace is null || symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();
        if (MatchesAnyRoot(typeNamespace, roots)) return;

        var location = symbol.Locations.FirstOrDefault(static item => item.IsInSource);
        if (location is null) return;

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, location, symbol.Name, typeNamespace, string.Join("; ", roots)));
    }

    private static bool MatchesAnyRoot(string typeNamespace, IReadOnlyList<string> roots) {
        foreach (var rawRoot in roots) {
            var root = rawRoot.Trim().TrimEnd('.');
            if (root.Length == 0) continue;
            if (typeNamespace.Equals(root, StringComparison.Ordinal)) return true;
            if (typeNamespace.StartsWith(root + ".", StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
