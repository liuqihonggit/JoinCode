namespace AotSafety.Shared.RuleDetectors;

/// <summary>
/// JCC3016 检测器 — 同步方法内异步调用未消费。
/// 纯检测逻辑，不依赖 DiagnosticAnalyzer API，分析器和 ast_cli 共享调用。
/// 判断一个 ExpressionStatement 是否是 JCC3016 违规，基于 SemanticModel + 类型系统。
/// </summary>
public static class SyncMethodAsyncCallDetector {

    /// <summary>
    /// 判断 ExpressionStatement 是否是 JCC3016 违规。
    /// 检测条件：非 async 方法内调用返回 Task/Task 集合的方法，且未通过 await/赋值消费。
    /// </summary>
    /// <param name="exprStatement">表达式语句节点。</param>
    /// <param name="semanticModel">语义模型。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>true 表示违规。</returns>
    public static bool IsViolation(
        ExpressionStatementSyntax exprStatement,
        SemanticModel semanticModel,
        CancellationToken ct) {
        if (ct.IsCancellationRequested) return false;

        // 1. 提取 invocation（裸语句或赋值右侧）
        var invocation = GetInvocation(exprStatement);
        if (invocation is null) return false;

        // 2. 检查是否已消费（await 上下文 / 赋值表达式）
        if (IsConsumed(exprStatement)) return false;

        // 3. 必须在方法内（非顶层语句）
        var enclosingMethod = exprStatement.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (enclosingMethod is null) return false;

        // 4. 跳过 async 方法（async 方法内未 await 是 JCC3017 的职责）
        if (enclosingMethod.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword))) return false;

        // 5. 方法符号必须存在
        var methodSymbol = semanticModel.GetDeclaredSymbol(enclosingMethod);
        if (methodSymbol is null) return false;

        // 6. 跳过 lambda/local function 内的调用
        if (enclosingMethod.Body is not null &&
            AotSafetyHelpers.IsInsideLambdaOrLocalFunction(exprStatement, enclosingMethod.Body)) return false;

        // 7. 被调用方法必须返回 Task-like 或 Task 集合
        var calledSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (!AotSafetyHelpers.ReturnsTaskOrTaskCollection(calledSymbol)) return false;

        // 8. 排除泛型方法实例化（返回类型是类型参数）
        if (IsGenericMethodInstantiation(calledSymbol)) return false;

        // 9. 排除 Task.Run（启动后台任务，await 会死锁）
        if (IsTaskRun(calledSymbol)) return false;

        return true;
    }

    /// <summary>
    /// 从 ExpressionStatement 提取 InvocationExpression。
    /// 支持裸语句（`Foo();`）和赋值右侧（`x = Foo();`）。
    /// </summary>
    public static InvocationExpressionSyntax? GetInvocation(ExpressionStatementSyntax exprStatement) {
        var invocation = exprStatement.Expression as InvocationExpressionSyntax;
        if (invocation is null) {
            if (exprStatement.Expression is AssignmentExpressionSyntax assign) {
                invocation = assign.Right as InvocationExpressionSyntax;
            }
        }
        return invocation;
    }

    /// <summary>
    /// 获取违规信息（外层方法名、被调用方法名），用于 Diagnostic 消息格式化。
    /// </summary>
    public static (string enclosingName, string calledName)? GetViolationInfo(
        ExpressionStatementSyntax exprStatement,
        SemanticModel semanticModel) {
        var invocation = GetInvocation(exprStatement);
        if (invocation is null) return null;

        var enclosingMethod = exprStatement.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (enclosingMethod is null) return null;

        var methodSymbol = semanticModel.GetDeclaredSymbol(enclosingMethod);
        if (methodSymbol is null) return null;

        var calledSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var enclosingName = methodSymbol.Name;
        var calledName = calledSymbol?.ContainingType?.Name is { } tn
            ? $"{tn}.{calledSymbol.Name}"
            : calledSymbol?.Name ?? string.Empty;
        return (enclosingName, calledName);
    }

    /// <summary>
    /// 判断表达式语句是否已消费（await 上下文 / 赋值表达式包括 discard）。
    /// </summary>
    private static bool IsConsumed(ExpressionStatementSyntax stmt) {
        if (AotSafetyHelpers.IsInsideAwait(stmt)) return true;
        if (stmt.Expression is AssignmentExpressionSyntax) return true;
        return false;
    }

    /// <summary>
    /// 排除泛型方法实例化（返回类型是类型参数，如 `Create<T>()` 返回 T）。
    /// </summary>
    private static bool IsGenericMethodInstantiation(IMethodSymbol? method) {
        if (method is null) return false;
        if (!method.IsGenericMethod) return false;
        return method.OriginalDefinition.ReturnType is ITypeParameterSymbol;
    }

    /// <summary>
    /// 排除 Task.Run（启动后台任务，调用方 await 会死锁）。
    /// </summary>
    private static bool IsTaskRun(IMethodSymbol? method) {
        if (method is null) return false;
        return method.ContainingType?.Name == "Task" && method.Name == "Run";
    }
}
