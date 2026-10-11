namespace AotSafety.Generator;

/// <summary>
/// Disposable 一致性检测分析器主入口 — 从 RuleRegistry 获取 AnalyzerId="DisposableConsistency" 的规则。
/// 每个规则是独立类/文件,通过 [AnalyzerRule(AnalyzerId="DisposableConsistency")] 特性自动发现。
/// 覆盖: JCC9102-JCC9108, JCC9200-JCC9202。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DisposableConsistencyRules : DiagnosticAnalyzer {
    private const string AnalyzerId = "DisposableConsistency";

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
