namespace AotSafety.Generator;

/// <summary>
/// AOT 安全分析器主入口 — 从 RuleRegistry 获取 AnalyzerId="AotSafety" 的规则,收集 descriptors 并注册。
/// 每个规则是独立类/文件,通过 [AnalyzerRule(AnalyzerId="AotSafety")] 特性自动发现。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AotSafetyRules : DiagnosticAnalyzer {
    private const string AnalyzerId = "AotSafety";

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
