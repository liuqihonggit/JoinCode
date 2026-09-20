namespace AotSafety.Generator;

/// <summary>
/// 死代码检测分析器主入口 — 从 RuleRegistry 获取 AnalyzerId="DeadCode" 的规则。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeadCodeRules : DiagnosticAnalyzer {
    private const string AnalyzerId = "DeadCode";

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
