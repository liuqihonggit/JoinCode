namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9305: Actor Handle 方法里 fire-and-forget 必须通过 RegisterInFlight 注册。
/// <para>DisposeAsync 只等 Consumer 退出,不等待 Handle 里 fire-and-forget 启动的 in-flight 任务。</para>
/// <para>未注册的任务在 Dispose 后仍可能跑,访问已释放资源或结果丢失(CI #691 根因)。</para>
/// <para>正确模式: RegisterInFlight(SomeAsync().AsTask()) 或 RegisterInFlight(Task.Run(...))。</para>
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Concurrency",
    Id = "JCC9305",
    Title = "Actor Handle fire-and-forget: 必须通过 RegisterInFlight 注册",
    Description = "Handle 方法里 fire-and-forget '{0}' 未通过 RegisterInFlight 注册。DisposeAsync 不等待未注册的 in-flight 任务,Dispose 后任务仍可能跑,访问已释放资源或结果丢失。应改为 RegisterInFlight(SomeAsync().AsTask())。",
    Category = "FireAndForgetSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Actor Handle 方法里 fire-and-forget 启动的任务必须通过 RegisterInFlight 注册,确保 DisposeAsync 等待 in-flight 任务完成。正确模式: RegisterInFlight(SomeAsync().AsTask())。CI #691 根因: BackgroundTaskActor.Handle 里 _ = ExecuteTaskAsync 是 fire-and-forget,DisposeAsync 不等待,导致 AnalyticsService 历史加载丢失。")]
public sealed class HandleFireAndForgetRule : AnalyzerRuleBase<HandleFireAndForgetRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var methodDecl = (MethodDeclarationSyntax)ctx.Node;

        if (methodDecl.Identifier.ValueText != "Handle") return;
        if (!methodDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword))) return;

        var body = methodDecl.Body;
        if (body is null) return;

        foreach (var stmt in body.DescendantNodes().OfType<ExpressionStatementSyntax>()) {
            if (AotSafetyHelpers.IsInsideLambdaOrLocalFunction(stmt, body)) continue;

            if (stmt.Expression is not AssignmentExpressionSyntax assignment) continue;
            if (assignment.Left is not IdentifierNameSyntax { Identifier.ValueText: "_" }) continue;

            var rightExpr = assignment.Right;
            var invocation = rightExpr as InvocationExpressionSyntax ?? FindInnermostInvocation(rightExpr);
            if (invocation is null) continue;

            var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (!AotSafetyHelpers.ReturnsTaskLike(symbol)) continue;

            var calledName = symbol!.ContainingType?.Name is { } tn ? $"{tn}.{symbol.Name}" : symbol.Name;
            ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation(), calledName));
        }
    }

    private static InvocationExpressionSyntax? FindInnermostInvocation(ExpressionSyntax expr) {
        if (expr is InvocationExpressionSyntax inv) {
            if (inv.Expression is MemberAccessExpressionSyntax memberAccess) {
                if (memberAccess.Expression is InvocationExpressionSyntax innerInv) {
                    return FindInnermostInvocation(innerInv) ?? innerInv;
                }
            }
            return inv;
        }
        return null;
    }
}
