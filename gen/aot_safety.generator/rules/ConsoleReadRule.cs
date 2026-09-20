namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC2003: Console.Read() 必须包裹 IsInputRedirected 检查。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "AsyncSafety",
    Id = "JCC2003",
    Title = "交互输入: Console.Read() 必须包裹 IsInputRedirected 检查",
    Description = "Console.Read() 在输入重定向环境（测试、CI）中可能阻塞。必须先检查 Console.IsInputRedirected，在非交互模式下使用替代路径。",
    Category = "InteractiveSafety",
    Severity = DiagnosticSeverity.Warning,
    HelpLinkUri = "测试环境中 Console.IsInputRedirected 可能为 false, 但 TestEnvironmentDetector.IsTestEnvironment 为 true. Read 可能阻塞. 正确模式: if (Console.IsInputRedirected || TestEnvironmentDetector.IsTestEnvironment) { alternative } else { Console.Read(); }.")]
public sealed class ConsoleReadRule : AnalyzerRuleBase<ConsoleReadRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;
        InteractiveInputAnalyzer.Analyze(ctx, Descriptor, "Read");
    }
}
