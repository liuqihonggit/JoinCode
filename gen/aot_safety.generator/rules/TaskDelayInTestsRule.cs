namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3010/JCC3011/JCC3012: 测试中 Task.Delay 真实等待。仅 Test 项目触发。
/// 多描述符规则 — 3 个 descriptor 对应 int/TimeSpan/未知参数类型,直接实现 IAnalyzerRule。
/// [AnalyzerRule] 标记主 Id (JCC3010),另外两个 descriptor 手动创建。
/// </summary>
[AnalyzerRule(
    Id = "JCC3010",
    Title = "测试性能: Task.Delay({0}ms) 真实等待",
    Description = "测试中 Task.Delay({0}ms) 真实等待。用 FakeTimeProvider.Advance() 推进时间，或 SemaphoreSlim 信号替代盲等。",
    Category = "AsyncCorrectness",
    Severity = DiagnosticSeverity.Warning,
    HelpLinkUri = "Use FakeTimeProvider.Advance() or SemaphoreSlim signal instead of Task.Delay in tests.")]
public sealed class TaskDelayInTestsRule : IAnalyzerRule {
    private static readonly DiagnosticDescriptor RuleInt = RuleDescriptorFactory.Create<TaskDelayInTestsRule>();

    private static readonly DiagnosticDescriptor RuleTimeSpan = new(
        "JCC3011",
        "测试性能: Task.Delay({0}) 真实等待 {1}ms",
        "测试中 Task.Delay({0}) 真实等待 {1}ms。用 FakeTimeProvider.Advance() 推进时间，或 SemaphoreSlim 信号替代盲等。",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        true,
        "Use FakeTimeProvider.Advance() or SemaphoreSlim signal instead of Task.Delay in tests.");

    private static readonly DiagnosticDescriptor RuleUnknown = new(
        "JCC3012",
        "测试性能: Task.Delay 真实等待",
        "测试中 Task.Delay 真实等待。用 FakeTimeProvider.Advance() 推进时间，或 SemaphoreSlim 信号替代盲等。",
        "AsyncCorrectness",
        DiagnosticSeverity.Warning,
        true,
        "Use FakeTimeProvider.Advance() or SemaphoreSlim signal instead of Task.Delay in tests.");

    public IReadOnlyList<DiagnosticDescriptor> Descriptors { get; } = new[] { RuleInt, RuleTimeSpan, RuleUnknown };

    public void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        if (!projectContext.IsTest) return;
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (GuardChain.Create().RequireNotCancellationRequested(ctx.CancellationToken).Failed) return;

        var invocation = (InvocationExpressionSyntax)ctx.Node;

        var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var containingType = symbol.ContainingType;
        if (containingType is null) return;
        if (containingType.Name != "Task" || symbol.Name != "Delay") return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count > 0) {
            var firstArg = args[0].Expression;
            if (firstArg is LiteralExpressionSyntax literal &&
                literal.Token.Value is int delayMs &&
                delayMs <= 1) {
                return;
            }

            if (symbol.Parameters.Length > 0) {
                var firstParamType = symbol.Parameters[0].Type;
                if (firstParamType.SpecialType == SpecialType.System_Int32) {
                    if (firstArg is LiteralExpressionSyntax intLiteral &&
                        intLiteral.Token.Value is int ms) {
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            RuleInt,
                            invocation.GetLocation(),
                            ms.ToString()));
                        return;
                    }

                    ctx.ReportDiagnostic(Diagnostic.Create(
                        RuleUnknown,
                        invocation.GetLocation()));
                    return;
                } else if (firstParamType.Name == "TimeSpan") {
                    if (firstArg is InvocationExpressionSyntax tsInvocation) {
                        var tsSymbol = ctx.SemanticModel.GetSymbolInfo(tsInvocation).Symbol as IMethodSymbol;
                        if (tsSymbol?.Name == "FromMilliseconds" &&
                            tsInvocation.ArgumentList.Arguments.Count > 0 &&
                            tsInvocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax msLiteral &&
                            msLiteral.Token.Value is int tsMs) {
                            ctx.ReportDiagnostic(Diagnostic.Create(
                                RuleTimeSpan,
                                invocation.GetLocation(),
                                $"TimeSpan.FromMilliseconds({tsMs})",
                                tsMs.ToString()));
                            return;
                        }

                        if (tsSymbol?.Name == "FromSeconds" &&
                            tsInvocation.ArgumentList.Arguments.Count > 0 &&
                            tsInvocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax secLiteral) {
                            var secValue = secLiteral.Token.Value;
                            var secMs = secValue switch {
                                int s => s * 1000,
                                double d => (int)(d * 1000),
                                float f => (int)(f * 1000),
                                _ => -1
                            };
                            if (secMs > 0) {
                                ctx.ReportDiagnostic(Diagnostic.Create(
                                    RuleTimeSpan,
                                    invocation.GetLocation(),
                                    $"TimeSpan.FromSeconds({secValue})",
                                    secMs.ToString()));
                                return;
                            }
                        }
                    }

                    ctx.ReportDiagnostic(Diagnostic.Create(
                        RuleUnknown,
                        invocation.GetLocation()));
                    return;
                }
            }

            ctx.ReportDiagnostic(Diagnostic.Create(
                RuleUnknown,
                invocation.GetLocation()));
        } else {
            ctx.ReportDiagnostic(Diagnostic.Create(
                RuleUnknown,
                invocation.GetLocation()));
        }
    }
}
