namespace AotSafety.Generator.Rules;

/// <summary>
/// JCC3001/JCC3002: 即发即忘调用缺少 CancellationToken 保护。
/// 多描述符规则 — 共享 AnalyzeFireAndForget 逻辑。
/// </summary>
[AnalyzerRule(
    AnalyzerId = "Concurrency",
    Id = "JCC3001",
    Title = "即发即忘: 异步调用缺少 CancellationToken 保护",
    Description = "即发即忘调用 '{0}' 未传递 CancellationToken。异步操作在 Dispose 后可能继续执行导致 ObjectDisposedException。应传递 _disposeCts.Token 并添加 WaitAsync 超时保护。",
    Category = "FireAndForgetSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "即发即忘模式中如果异步方法接受 CancellationToken 参数但未传入, Dispose 后异步操作仍会继续执行, 可能访问已释放的资源. 正确模式: 添加 _disposeCts 字段, 传递 _disposeCts.Token, 添加 WaitAsync 超时, Dispose 中 _disposeCts.Cancel(), catch (OperationCanceledException) 静默处理.")]
[AnalyzerRule(
    AnalyzerId = "Concurrency",
    Id = "JCC3002",
    Title = "即发即忘: Task.Run 缺少 CancellationToken 参数",
    Description = "Task.Run 调用未传递 CancellationToken。任务在 Dispose 后可能继续执行导致异常。应传递 _disposeCts.Token 作为第二个参数。",
    Category = "FireAndForgetSafety",
    Severity = DiagnosticSeverity.Warning,
    IsEnabledByDefault = true,
    HelpLinkUri = "Task.Run 在即发即忘模式中必须传递 CancellationToken, 否则 Dispose 后任务无法取消.")]
public sealed class FireAndForgetRule : IAnalyzerRule {
    private static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> Map = RuleDescriptorFactory.CreateAll<FireAndForgetRule>();
    public IReadOnlyList<DiagnosticDescriptor> Descriptors { get; } = Map.Values.ToList();

    public void Register(CompilationStartAnalysisContext context, ProjectContext projectContext) {
        context.RegisterSyntaxNodeAction(AnalyzeFireAndForget, SyntaxKind.ExpressionStatement);
    }

    private static void AnalyzeFireAndForget(SyntaxNodeAnalysisContext ctx) {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        var exprStatement = (ExpressionStatementSyntax)ctx.Node;

        if (exprStatement.Expression is not AssignmentExpressionSyntax assignment)
            return;
        if (!assignment.Left.IsKind(SyntaxKind.IdentifierName))
            return;
        var leftIdentifier = (IdentifierNameSyntax)assignment.Left;
        if (leftIdentifier.Identifier.ValueText != "_")
            return;

        var rightExpr = assignment.Right;

        if (rightExpr is InvocationExpressionSyntax invocation) {
            var symbol = ctx.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol is null) return;

            var containingType = symbol.ContainingType;
            if (containingType is null) return;

            var typeName = containingType.Name;
            var methodName = symbol.Name;

            if (typeName == "Task" && methodName == "Run") {
                if (!HasCancellationTokenArgument(ctx, invocation, symbol)) {
                    ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC3002"], invocation.GetLocation()));
                }
                return;
            }

            if (IsAsyncMethod(symbol)) {
                if (!HasCancellationTokenArgument(ctx, invocation, symbol)) {
                    var displayStr = $"{typeName}.{methodName}";
                    ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC3001"], invocation.GetLocation(), displayStr));
                }
                return;
            }
        }

        var innerInvocation = FindInnermostInvocation(rightExpr);
        if (innerInvocation is not null) {
            var symbol = ctx.SemanticModel.GetSymbolInfo(innerInvocation).Symbol as IMethodSymbol;
            if (symbol is null) return;

            var containingType = symbol.ContainingType;
            if (containingType is null) return;

            if (containingType.Name == "Task" && symbol.Name == "Run") {
                if (!HasCancellationTokenArgument(ctx, innerInvocation, symbol)) {
                    ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC3002"], innerInvocation.GetLocation()));
                }
                return;
            }

            if (IsAsyncMethod(symbol) && !HasCancellationTokenArgument(ctx, innerInvocation, symbol)) {
                var displayStr = $"{containingType.Name}.{symbol.Name}";
                ctx.ReportDiagnostic(Diagnostic.Create(Map["JCC3001"], innerInvocation.GetLocation(), displayStr));
            }
        }
    }

    private static bool IsAsyncMethod(IMethodSymbol method) {
        var returnType = method.ReturnType;
        if (returnType is null) return false;

        var typeName = returnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return typeName.StartsWith("Task", StringComparison.Ordinal) ||
               typeName.StartsWith("ValueTask", StringComparison.Ordinal);
    }

    private static bool HasCancellationTokenArgument(SyntaxNodeAnalysisContext ctx, InvocationExpressionSyntax invocation, IMethodSymbol method) {
        var hasCancellationTokenParam = method.Parameters.Any(p => {
            var paramType = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return paramType == "CancellationToken";
        });

        if (!hasCancellationTokenParam) return true;

        var arguments = invocation.ArgumentList.Arguments;
        foreach (var arg in arguments) {
            var argType = ctx.SemanticModel.GetTypeInfo(arg.Expression).Type;
            if (argType is not null) {
                var argTypeName = argType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                if (argTypeName == "CancellationToken")
                    return true;
            }
        }

        return false;
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
