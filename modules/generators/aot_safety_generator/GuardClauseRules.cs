namespace AotSafety.Generator;

/// <summary>
/// 卫语句检测分析器主入口 — 从 RuleRegistry 获取 AnalyzerId="GuardClause" 的规则。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GuardClauseRules : DiagnosticAnalyzer {
    private const string AnalyzerId = "GuardClause";

    private static readonly IReadOnlyList<IAnalyzerRule> Rules = RuleRegistry.GetListByAnalyzer(AnalyzerId);
    private static readonly IReadOnlyList<DiagnosticDescriptor> AllDescriptors =
        Rules.SelectMany(r => r.Descriptors).ToArray();

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.CreateRange(AllDescriptors);

    public override void Initialize(AnalysisContext context) {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(compilationContext => {
            var projectContext = ProjectContext.From(
                compilationContext.Compilation,
                compilationContext.Options.AnalyzerConfigOptionsProvider);

            foreach (var rule in Rules) {
                rule.Register(compilationContext, projectContext);
            }
        });
    }
}
