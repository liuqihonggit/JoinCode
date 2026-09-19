namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC2002: Console.ReadKey() 必须包裹 IsInputRedirected 检查。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AsyncSafety",
    Id = "JCC2002",
    Title = "交互输入: Console.ReadKey() 必须包裹 IsInputRedirected 检查",
    Description = "Console.ReadKey() 在输入重定向环境（测试、CI）中会无限阻塞。必须先检查 Console.IsInputRedirected，在非交互模式下使用替代路径。",
    Category = "InteractiveSafety",
    Severity = DiagnosticSeverity.Warning,
    HelpLinkUri = "测试环境中 Console.IsInputRedirected 可能为 false, 但 TestEnvironmentDetector.IsTestEnvironment 为 true. ReadKey 会无限等待导致卡死. 正确模式: if (Console.IsInputRedirected || TestEnvironmentDetector.IsTestEnvironment) { alternative } else { Console.ReadKey(); }.")]
public sealed class ConsoleReadKeyRule : AnalyzerRuleBase<ConsoleReadKeyRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;
        InteractiveInputAnalyzer.Analyze(ctx, Descriptor, "ReadKey");
    }
}
