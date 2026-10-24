namespace AotSafety.Generator;

/// <summary>
/// MCP 参数描述验证分析器主入口 — 从 RuleRegistry 获取 AnalyzerId="McpParam" 的规则。
/// 检查 [McpToolParameter] 必须有 Description 或 WellKnownParam，不对就编译报错。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class McpParameterRules : DiagnosticAnalyzer {
    private const string AnalyzerId = "McpParam";

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
