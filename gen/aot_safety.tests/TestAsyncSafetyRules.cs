namespace AotSafety.Tests;

/// <summary>
/// 测试辅助分析器 — 包装 AsyncSafetyRules 的规则注册,但允许测试手动设置 ProjectType。
/// 解决 CSharpAnalyzerTest 的 AnalyzerConfigFiles 不传递 build_property.XXX 的问题。
/// 用法: TestAsyncSafetyRules.ProjectType = ProjectType.Library; 然后用作分析器。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestAsyncSafetyRules : DiagnosticAnalyzer {
    public static ProjectType ProjectType { get; set; } = ProjectType.Unknown;

    private static readonly IReadOnlyList<IAnalyzerRule> Rules = RuleRegistry.GetListByAnalyzer("AsyncSafety");
    private static readonly IReadOnlyList<DiagnosticDescriptor> AllDescriptors =
        Rules.SelectMany(r => r.Descriptors).ToArray();

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.CreateRange(AllDescriptors);

    public override void Initialize(AnalysisContext context) {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(compilationContext => {
            var projectContext = new ProjectContext { ProjectType = ProjectType };

            foreach (var rule in Rules) {
                rule.Register(compilationContext, projectContext);
            }
        });
    }
}
