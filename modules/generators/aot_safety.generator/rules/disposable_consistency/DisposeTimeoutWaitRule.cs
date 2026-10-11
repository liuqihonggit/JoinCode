namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC9203: 释放一致性 — 释放函数内禁止超时/阻塞等待。
/// 释放是必须完成的操作，超时无意义——无论如何都要释放。
/// 检测 Dispose/DisposeAsync/Close/StopAsync/ShutdownAsync 等方法体内的:
///   .Wait()/.Wait(int)/.Wait(TimeSpan) — Task 阻塞等待
///   .WaitAsync(TimeSpan) — 超时等待
///   Thread.Join(int)/Thread.Join(TimeSpan) — 超时等待线程
///   Task.WhenAny(task, Task.Delay(...)) — 超时竞赛
///   .GetAwaiter().GetResult() — 同步阻塞
/// </summary>
[AnalyzerRule(
    AnalyzerId = "DisposableConsistency",
    Id = "JCC9203",
    Title = "释放一致性: 释放函数内禁止超时/阻塞等待",
    Description = "释放方法 '{0}' 内出现阻塞/超时等待模式 '{1}'。释放是必须完成的操作，超时无意义。改为: 1) 直接 await task.ConfigureAwait(false); 2) 用 CancellationToken.None; 3) fire-and-forget 后台释放 _ = DisposeAsync().AsTask(); 4) 全部异步化改造统一到 DisposeAsync。",
    Category = "Reliability",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Dispose/Close/StopAsync methods must not block or wait with timeout. Release is unconditional — timeout is meaningless. Forbidden patterns: .Wait()/.Wait(ts), .WaitAsync(ts), Thread.Join(ts), Task.WhenAny+Task.Delay, .GetAwaiter().GetResult(). Fix: await task.ConfigureAwait(false) with CancellationToken.None, or fire-and-forget _ = DisposeAsync().AsTask().")]
public sealed class DisposeTimeoutWaitRule : AnalyzerRuleBase<DisposeTimeoutWaitRule> {
    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        var method = (MethodDeclarationSyntax)ctx.Node;
        if (!AotSafetyHelpers.IsDisposeMethodName(method.Identifier.ValueText)) return;
        var body = AotSafetyHelpers.GetMethodBody(method);
        if (body is null) return;

        foreach (var inv in body.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (ctx.CancellationToken.IsCancellationRequested) return;
            if (GetBlockingPattern(inv) is { } pattern) {
                var methodName = method.Identifier.ValueText;
                ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, inv.GetLocation(), methodName, pattern));
            }
        }
    }

    private static string? GetBlockingPattern(InvocationExpressionSyntax inv) {
        if (GetAwaiterPatternDetector.IsGetAwaiterGetResultPattern(inv))
            return ".GetAwaiter().GetResult()";

        if (inv.Expression is not MemberAccessExpressionSyntax ma) {
            if (IsTaskWhenAnyWithDelay(inv)) return "Task.WhenAny(task, Task.Delay)";
            return null;
        }

        var methodName = ma.Name.Identifier.ValueText.AsSpan();

        if (methodName.SequenceEqual("Wait".AsSpan()))
            return ".Wait()";

        if (methodName.SequenceEqual("WaitAsync".AsSpan()))
            return ".WaitAsync(timeout)";

        if (methodName.SequenceEqual("Join".AsSpan()) && IsThreadJoin(ma))
            return "Thread.Join(timeout)";

        if (methodName.SequenceEqual("WhenAny".AsSpan()) && IsTaskWhenAnyWithDelay(inv))
            return "Task.WhenAny(task, Task.Delay)";

        return null;
    }

    private static bool IsThreadJoin(MemberAccessExpressionSyntax ma) {
        return ma.Expression switch {
            IdentifierNameSyntax id => id.Identifier.ValueText == "Thread",
            MemberAccessExpressionSyntax inner when inner.Name is IdentifierNameSyntax innerId =>
                innerId.Identifier.ValueText == "Thread",
            _ => false,
        };
    }

    private static bool IsTaskWhenAnyWithDelay(InvocationExpressionSyntax inv) {
        if (inv.Expression is MemberAccessExpressionSyntax ma &&
            ma.Name.Identifier.ValueText != "WhenAny")
            return false;
        if (inv.Expression is IdentifierNameSyntax id &&
            id.Identifier.ValueText != "WhenAny")
            return false;

        foreach (var arg in inv.ArgumentList.Arguments) {
            if (ContainsTaskDelay(arg.Expression)) return true;
        }
        return false;
    }

    private static bool ContainsTaskDelay(ExpressionSyntax expr) {
        foreach (var inv in expr.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()) {
            if (inv.Expression is MemberAccessExpressionSyntax ma &&
                ma.Name.Identifier.ValueText == "Delay") {
                if (ma.Expression is IdentifierNameSyntax typeId &&
                    typeId.Identifier.ValueText == "Task")
                    return true;
            }
        }
        return false;
    }
}
