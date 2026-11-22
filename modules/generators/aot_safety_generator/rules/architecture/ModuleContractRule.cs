namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC12001: 域契约缺失或非法 — 18 个契约域必须声明合法 <c>JccDomain</c>，
/// <c>JccAllowedDomains</c> 只能包含已知域；测试项目与非交付工具项目豁免。
/// <para>修复导向: 在域级 Directory.Build.props 写
/// <c>&lt;JccDomain&gt;llm&lt;/JccDomain&gt;</c>，allowlist 只列真实允许依赖的域；
/// 非契约项目不要把 JccDomain 当装饰属性填。</para>
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AotSafety",
    Id = "JCC12001",
    Title = "域契约缺失或非法",
    Description = "项目域契约非法：{0}。应在域级 Directory.Build.props 声明 18 个合法域之一的 JccDomain；JccAllowedDomains 只能包含已知域。测试项目与非交付工具项目豁免。",
    Category = "Architecture",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "A contract project must declare <JccDomain> with one of the 18 known domains and keep <JccAllowedDomains> limited to known domains. Test projects and non-deliverable tools are exempt.",
    IsCompilationEnd = true)]
public sealed class ModuleContractRule : AnalyzerRuleBase<ModuleContractRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        if (projectContext.IsTest) return;
        if (!ArchitectureDomainCatalog.IsContractProject(projectContext.ProjectDirectory)) return;

        context.RegisterSyntaxNodeAction(static _ => { }, SyntaxKind.CompilationUnit);
        context.RegisterCompilationEndAction(analysisContext => Analyze(analysisContext, projectContext));
    }

    private static void Analyze(CompilationAnalysisContext context, ProjectContext projectContext) {
        if (context.CancellationToken.IsCancellationRequested) return;

        var problems = new List<string>();
        var domain = projectContext.Domain.Trim();
        if (domain.Length == 0) {
            problems.Add("缺少 JccDomain");
        } else if (!ArchitectureDomainCatalog.IsKnown(domain)) {
            problems.Add($"未知 JccDomain '{domain}'");
        }

        foreach (var allowed in ArchitectureDomainCatalog.SplitDomains(projectContext.AllowedDomains)) {
            if (!ArchitectureDomainCatalog.IsKnown(allowed)) {
                problems.Add($"JccAllowedDomains 含未知域 '{allowed}'");
            }
        }

        if (problems.Count == 0) return;

        var tree = context.Compilation.SyntaxTrees.FirstOrDefault();
        if (tree is null) return;
        var location = tree.GetRoot(context.CancellationToken).GetFirstToken().GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(Descriptor, location, string.Join("; ", problems)));
    }
}
