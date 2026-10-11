namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC4007: 死锁风险 — 锁范围内有同步 Console.Error 输出,stderr 管道满时阻塞导致锁无法释放。
/// </summary>
/// <remarks>
/// 事故根因: 锁持有者在 using(lock.TryLock()) 块内调 Console.Error.WriteLine,
/// CI 中 stderr 管道缓冲区满时阻塞,锁持有者卡在 I/O 无法释放锁,
/// 其他线程等待锁超时,最终靠外部超时杀进程。
/// 正确做法: 锁范围内用 AsyncStderrWriter.Enqueue(非阻塞)替代 Console.Error.WriteLine。
/// </remarks>
[AnalyzerRule(
    AnalyzerId = "Concurrency",
    Id = "JCC4007",
    Title = "死锁风险: 锁范围内有同步 Console.Error 输出",
    Description = "锁范围内(using(lock.TryLock())块内)调用 Console.Error.Write/WriteLine/Flush,当 stderr 管道缓冲区满时阻塞,导致锁持有者卡在 I/O 无法释放锁,其他线程等待锁超时,最终靠外部超时杀进程。应改用 AsyncStderrWriter.Enqueue(非阻塞)。",
    Category = "DeadlockSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "锁范围内禁止同步 Console.Error 输出。根因: stderr 管道满时 Console.Error.WriteLine 阻塞,锁持有者卡在 I/O,其他线程等待锁超时。正确做法: 改用 AsyncStderrWriter.Enqueue(msg) 非阻塞入队。")]
public sealed class LockScopeConsoleErrorRule : AnalyzerRuleBase<LockScopeConsoleErrorRule> {
    private static readonly HashSet<string> LockMethodNames = new() {
        "TryLock", "LockOrCrash", "TryLockWithRetry",
        "TryLockAsync", "LockOrCrashAsync", "TryLockWithRetryAsync",
    };

    private static readonly HashSet<string> ConsoleErrorMethods = new() {
        "Write", "WriteLine", "Flush",
    };

    public override void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeUsingStatement, SyntaxKind.UsingStatement);
    }

    private static void AnalyzeUsingStatement(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        var usingStmt = (UsingStatementSyntax)ctx.Node;
        if (usingStmt.Statement is not BlockSyntax block) return;
        if (!IsLockAcquisition(usingStmt.Expression, ctx.SemanticModel)) return;
        CheckConsoleErrorInBlock(block, ctx);
    }

    private static bool IsLockAcquisition(ExpressionSyntax? expr, SemanticModel semanticModel) {
        if (expr is null) return false;
        var invocation = expr as InvocationExpressionSyntax
            ?? (expr as AssignmentExpressionSyntax)?.Right as InvocationExpressionSyntax;
        if (invocation is null) return false;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return false;
        var methodName = memberAccess.Name.Identifier.ValueText;
        if (!LockMethodNames.Contains(methodName)) return false;
        var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null) return false;
        return symbol.ContainingType?.Name == "AsyncLock";
    }

    private static void CheckConsoleErrorInBlock(BlockSyntax block, SyntaxNodeAnalysisContext ctx) {
        foreach (var stmt in block.Statements) {
            CheckConsoleErrorInNode(stmt, ctx);
        }
    }

    private static void CheckConsoleErrorInNode(SyntaxNode node, SyntaxNodeAnalysisContext ctx) {
        foreach (var invocation in node.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
            if (IsConsoleErrorCall(invocation, ctx.SemanticModel)) {
                ctx.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation()));
            }
        }
    }

    private static bool IsConsoleErrorCall(InvocationExpressionSyntax invocation, SemanticModel semanticModel) {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return false;
        var methodName = memberAccess.Name.Identifier.ValueText;
        if (!ConsoleErrorMethods.Contains(methodName)) return false;
        if (memberAccess.Expression is not MemberAccessExpressionSyntax consoleError) return false;
        if (consoleError.Name.Identifier.ValueText != "Error") return false;
        if (consoleError.Expression is not IdentifierNameSyntax consoleId) return false;
        return consoleId.Identifier.ValueText == "Console";
    }
}
