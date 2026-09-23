namespace AotSafety.Shared.RuleDetectors;

/// <summary>
/// .GetAwaiter().GetResult() 模式检测器 — 纯 AST 检测逻辑，分析器和 ast_cli 共享调用。
/// 检测和修复用同一套判定逻辑（同检同换），禁止双套实现导致脱节。
/// 判断"直接包含的函数体"（lambda 或方法）是否 async，而非外层方法。
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
    /// 获取直接包含的函数体（lambda 或方法），而非外层方法。
    /// 例如: async 方法内的同步 lambda 中调用 .GetAwaiter().GetResult()，
    /// 返回的是 lambda 节点，不是外层方法。
    /// </summary>
    public static SyntaxNode? GetEnclosingFunction(SyntaxNode node) {
        return node.Ancestors().FirstOrDefault(a =>
            a is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or MethodDeclarationSyntax);
    }

    /// <summary>
    /// 获取 enclosing 方法声明（向上查找最近的方法，跨越 lambda）。
    /// </summary>
    public static MethodDeclarationSyntax? GetEnclosingMethod(SyntaxNode node) {
        return node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
    }

    /// <summary>
    /// 判断直接包含的函数体（lambda 或方法）是否是 async。
    /// 检测的是委托函数本身，不是外层方法。
    /// </summary>
    public static bool IsInAsyncContext(SyntaxNode node) {
        var enclosing = GetEnclosingFunction(node);
        return enclosing switch {
            LambdaExpressionSyntax lambda => lambda.Modifiers.Any(SyntaxKind.AsyncKeyword),
            AnonymousMethodExpressionSyntax anon => anon.Modifiers.Any(SyntaxKind.AsyncKeyword),
            MethodDeclarationSyntax method => method.Modifiers.Any(SyntaxKind.AsyncKeyword),
            _ => false
        };
    }

    /// <summary>
    /// 获取直接包含的函数体名称（用于报告）。
    /// </summary>
    public static string GetEnclosingFunctionName(SyntaxNode node) {
        var enclosing = GetEnclosingFunction(node);
        return enclosing switch {
            LambdaExpressionSyntax => "lambda",
            AnonymousMethodExpressionSyntax => "anonymous",
            MethodDeclarationSyntax method => method.Identifier.ValueText + "()",
            _ => "?"
        };
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
    /// 判断该节点是否应该被跳过（Main/属性 getter）。
    /// 注意: 不跳过 lambda，lambda 内的可以改 async lambda。
    /// </summary>
    public static bool ShouldSkip(SyntaxNode node) {
        return IsInMainMethod(node) || IsInPropertyGetter(node);
    }

    /// <summary>
    /// 判断该节点是否是可修复的 .GetAwaiter().GetResult() 违规。
    /// 条件：是 .GetAwaiter().GetResult() 模式 + 直接包含体是同步的 + 不在跳过列表中。
    /// 修复方式：改直接包含体为 async + await（方法加 async，lambda 加 async）。
    /// 检测和修复共享同一套判定逻辑（同检同换）。
    /// </summary>
    public static bool IsFixableViolation(InvocationExpressionSyntax node) {
        if (!IsGetAwaiterGetResultPattern(node)) return false;
        if (ShouldSkip(node)) return false;
        // 直接包含体必须是同步的（才能改 async + await）
        if (IsInAsyncContext(node)) return false;
        return true;
    }
}
