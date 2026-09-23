namespace AotSafety.Shared.RuleDetectors;

/// <summary>
/// .GetAwaiter().GetResult() 模式检测器 — 纯 AST 检测逻辑，分析器和 ast_cli 共享调用。
/// 检测和修复用同一套判定逻辑（同检同换），禁止双套实现导致脱节。
/// </summary>
public static class GetAwaiterPatternDetector {

    /// <summary>
    /// 判断 invocation 是否是 expr.GetAwaiter().GetResult() 模式。
    /// </summary>
    public static bool IsGetAwaiterGetResultPattern(InvocationExpressionSyntax node) {
        // node.Expression: MemberAccessExpression, Name = "GetResult"
        if (node.Expression is not MemberAccessExpressionSyntax getResultAccess) return false;
        if (getResultAccess.Name.Identifier.ValueText != "GetResult") return false;
        // getResultAccess.Expression: InvocationExpression, calling "GetAwaiter"
        if (getResultAccess.Expression is not InvocationExpressionSyntax getAwaiterInvocation) return false;
        if (getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax getAwaiterAccess) return false;
        if (getAwaiterAccess.Name.Identifier.ValueText != "GetAwaiter") return false;
        return true;
    }

    /// <summary>
    /// 提取 .GetAwaiter().GetResult() 的内部表达式（即被等待的表达式）。
    /// </summary>
    public static ExpressionSyntax? ExtractInnerExpression(InvocationExpressionSyntax node) {
        if (node.Expression is not MemberAccessExpressionSyntax getResultAccess) return null;
        if (getResultAccess.Expression is not InvocationExpressionSyntax getAwaiterInvocation) return null;
        if (getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax getAwaiterAccess) return null;
        return getAwaiterAccess.Expression;
    }

    /// <summary>
    /// 获取 enclosing 方法声明。
    /// </summary>
    public static MethodDeclarationSyntax? GetEnclosingMethod(SyntaxNode node) {
        return node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
    }

    /// <summary>
    /// 判断 enclosing 方法是否是 async。
    /// </summary>
    public static bool IsInAsyncMethod(SyntaxNode node) {
        var method = GetEnclosingMethod(node);
        return method is not null && method.Modifiers.Any(SyntaxKind.AsyncKeyword);
    }

    /// <summary>
    /// 判断是否在 Main 方法中。
    /// </summary>
    public static bool IsInMainMethod(SyntaxNode node) {
        var method = GetEnclosingMethod(node);
        return method is not null && method.Identifier.ValueText == "Main";
    }

    /// <summary>
    /// 判断是否在属性 getter 中。
    /// </summary>
    public static bool IsInPropertyGetter(SyntaxNode node) {
        return node.Ancestors().OfType<AccessorDeclarationSyntax>()
            .Any(a => a.Kind() == SyntaxKind.GetAccessorDeclaration);
    }

    /// <summary>
    /// 判断是否在 lambda 中。
    /// </summary>
    public static bool IsInLambda(SyntaxNode node) {
        return node.Ancestors().Any(a => a is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax);
    }

    /// <summary>
    /// 判断该节点是否应该被跳过（Main/属性 getter/lambda）。
    /// </summary>
    public static bool ShouldSkip(SyntaxNode node) {
        return IsInMainMethod(node) || IsInPropertyGetter(node) || IsInLambda(node);
    }

    /// <summary>
    /// 判断该节点是否是可修复的 .GetAwaiter().GetResult() 违规。
    /// 条件：是 .GetAwaiter().GetResult() 模式 + 在 async 方法中 + 不在跳过列表中。
    /// 检测和修复共享同一套判定逻辑（同检同换）。
    /// </summary>
    public static bool IsFixableViolation(InvocationExpressionSyntax node) {
        if (!IsGetAwaiterGetResultPattern(node)) return false;
        if (ShouldSkip(node)) return false;
        if (!IsInAsyncMethod(node)) return false;
        return true;
    }
}
