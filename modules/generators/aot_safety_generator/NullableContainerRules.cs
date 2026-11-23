namespace AotSafety.Generator;

/// <summary>
/// 可空容器检测分析器主入口 — 从 RuleRegistry 获取 AnalyzerId="NullableContainer" 的规则。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullableContainerRules : DiagnosticAnalyzer {
    private const string AnalyzerId = "NullableContainer";

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
