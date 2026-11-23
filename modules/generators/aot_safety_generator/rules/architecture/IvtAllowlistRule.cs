namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC12007: IVT 未登记/生产互开 — 生产程序集之间的 <c>InternalsVisibleTo</c> 必须登记在
/// <c>JccAllowInternalsVisibleTo</c>；面向测试程序集的 IVT 不在此规则范围。
/// <para>修复导向: 测试专用 IVT 保持其它名；生产互开必须写进域契约并附 reason/ADR，
/// 不允许默认把整个域的内部实现向兄弟生产程序集敞开。</para>
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AotSafety",
    Id = "JCC12007",
    Title = "IVT 未登记/生产互开",
    Description = "InternalsVisibleTo 目标 '{0}' 未在 JccAllowInternalsVisibleTo 登记；生产程序集不得默认互开内部可见性。测试程序集目标的 IVT 豁免。",
    Category = "Architecture",
    Severity = DiagnosticSeverity.Info,
    IsEnabledByDefault = true,
    HelpLinkUri = "Production-to-production InternalsVisibleTo must be listed in JccAllowInternalsVisibleTo with a reviewed reason. IVT aimed at test assemblies is exempt.",
    IsCompilationEnd = true)]
public sealed class IvtAllowlistRule : AnalyzerRuleBase<IvtAllowlistRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        if (projectContext.IsTest) return;
        if (!ArchitectureDomainCatalog.IsContractProject(projectContext.ProjectDirectory)) return;

        context.RegisterSyntaxNodeAction(static _ => { }, SyntaxKind.CompilationUnit);
        context.RegisterCompilationEndAction(analysisContext => Analyze(analysisContext, projectContext));
    }

    private static void Analyze(CompilationAnalysisContext context, ProjectContext projectContext) {
        if (context.CancellationToken.IsCancellationRequested) return;

        var allowed = ArchitectureDomainCatalog.SplitList(projectContext.AllowInternalsVisibleTo);
        var violations = new List<string>();
        foreach (var tree in context.Compilation.SyntaxTrees) {
            if (context.CancellationToken.IsCancellationRequested) return;

            var root = tree.GetRoot(context.CancellationToken);
            foreach (var attributeList in root.DescendantNodes().OfType<AttributeListSyntax>()) {
                if (!IsAssemblyTarget(attributeList)) continue;

                foreach (var attribute in attributeList.Attributes) {
                    if (!IsInternalsVisibleTo(attribute)) continue;
                    if (attribute.ArgumentList is null) continue;

                    var firstArgument = attribute.ArgumentList.Arguments.FirstOrDefault();
                    if (firstArgument?.Expression is not LiteralExpressionSyntax literal) continue;
                    if (!literal.IsKind(SyntaxKind.StringLiteralExpression)) continue;

                    var rawTarget = literal.Token.ValueText;
                    var target = rawTarget.Split(',')[0].Trim();
                    if (target.Length == 0) continue;
                    if (IsTestAssembly(target)) continue;
                    if (ContainsIgnoreCase(allowed, target)) continue;

                    violations.Add(target);
                }
            }
        }

        if (violations.Count == 0) return;

        var location = FindProjectSourceLocation(context.Compilation, context.CancellationToken);
        if (location is null) return;

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, location, string.Join(", ", violations)));
    }

    /// <summary>
    /// 生成 AssemblyInfo 位于 obj/ 下，其诊断会被 GeneratedCodeAnalysisFlags.None 过滤；
    /// 改为落在项目首个非生成源文件上，保证真实构建可见。
    /// </summary>
    private static Location? FindProjectSourceLocation(Compilation compilation, CancellationToken cancellationToken) {
        foreach (var tree in compilation.SyntaxTrees) {
            var path = tree.FilePath.Replace('\\', '/');
            if (path.IndexOf("/obj/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            return tree.GetRoot(cancellationToken).GetFirstToken().GetLocation();
        }
        return null;
    }

    private static bool IsAssemblyTarget(AttributeListSyntax attributeList) {
        return attributeList.Target is not null
            && attributeList.Target.Identifier.IsKind(SyntaxKind.AssemblyKeyword);
    }

    private static bool IsInternalsVisibleTo(AttributeSyntax attribute) {
        var name = attribute.Name.ToString();
        return name.EndsWith("InternalsVisibleTo", StringComparison.Ordinal)
            || name.EndsWith("InternalsVisibleToAttribute", StringComparison.Ordinal);
    }

    private static bool IsTestAssembly(string assemblyName) {
        return assemblyName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || assemblyName.EndsWith(".Test", StringComparison.OrdinalIgnoreCase)
            || assemblyName.IndexOf(".Tests.", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsIgnoreCase(IReadOnlyList<string> values, string candidate) {
        foreach (var value in values) {
            if (value.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
