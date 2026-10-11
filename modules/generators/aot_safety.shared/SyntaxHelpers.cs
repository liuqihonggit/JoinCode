namespace AotSafety.Shared;

/// <summary>
/// AST 修复共享工具 — 分析器 CodeFix 和 ast_cli fixer 统一调用，避免 trivia 处理不一致导致格式问题。
/// 核心原则：内部表达式去掉所有 trivia，leading/trailing trivia 由最终表达式统一设置。
/// 同检同修：检测逻辑在 GetAwaiterPatternDetector，修复逻辑在此，两者同在共享工程。
/// </summary>
public static class SyntaxHelpers {

    /// <summary>
    /// 创建 await 表达式：await expr.ConfigureAwait(false)（库代码）或 await expr（测试代码）。
    /// 保留原始节点的 leading/trailing trivia（缩进、换行等）。
    /// 内部表达式去掉所有 trivia，避免缩进翻倍。
    /// </summary>
    public static ExpressionSyntax CreateAwaitExpression(
        ExpressionSyntax innerExpr, SyntaxNode originalNode, bool isTestFile) {
        var cleanExpr = innerExpr.WithoutTrivia();
        ExpressionSyntax awaitedExpr;

        if (isTestFile) {
            awaitedExpr = cleanExpr;
        } else {
            awaitedExpr = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    cleanExpr,
                    SyntaxFactory.Token(SyntaxKind.DotToken),
                    SyntaxFactory.IdentifierName("ConfigureAwait")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)))));
        }

        var awaitKeyword = SyntaxFactory.Token(SyntaxKind.AwaitKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);
        var awaitExpr = SyntaxFactory.AwaitExpression(awaitKeyword, awaitedExpr);

        return awaitExpr
            .WithLeadingTrivia(originalNode.GetLeadingTrivia())
            .WithTrailingTrivia(originalNode.GetTrailingTrivia());
    }

    /// <summary>
    /// 创建 expr.GetAwaiter().GetResult() 表达式。
    /// 保留原始节点的 leading/trailing trivia。
    /// </summary>
    public static ExpressionSyntax CreateGetAwaiterGetResult(
        ExpressionSyntax expr, SyntaxNode originalNode) {
        var cleanExpr = expr.WithoutTrivia();

        var getAwaiterAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            cleanExpr,
            SyntaxFactory.Token(SyntaxKind.DotToken),
            SyntaxFactory.IdentifierName("GetAwaiter"));

        var getAwaiterCall = SyntaxFactory.InvocationExpression(
            getAwaiterAccess, SyntaxFactory.ArgumentList());

        var getResultAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            getAwaiterCall,
            SyntaxFactory.Token(SyntaxKind.DotToken),
            SyntaxFactory.IdentifierName("GetResult"));

        var getResultCall = SyntaxFactory.InvocationExpression(
            getResultAccess, SyntaxFactory.ArgumentList());

        return getResultCall
            .WithLeadingTrivia(originalNode.GetLeadingTrivia())
            .WithTrailingTrivia(originalNode.GetTrailingTrivia());
    }

    /// <summary>
    /// 给方法声明加 async 修饰符（带 trailing space）。
    /// </summary>
    public static MethodDeclarationSyntax AddAsyncModifier(MethodDeclarationSyntax method) {
        var asyncModifier = SyntaxFactory.Token(SyntaxKind.AsyncKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);
        return method.WithModifiers(method.Modifiers.Add(asyncModifier));
    }

    /// <summary>
    /// 转换返回类型: void→Task, T→Task&lt;T&gt;, Task/ValueTask 不变。
    /// 保留原始返回类型的 leading/trailing trivia。
    /// </summary>
    public static TypeSyntax TransformReturnType(TypeSyntax returnType) {
        var returnText = returnType.ToString().Trim();
        var leadingTrivia = returnType.GetLeadingTrivia();
        var trailingTrivia = returnType.GetTrailingTrivia();

        if (returnText == "void")
            return SyntaxFactory.IdentifierName("Task")
                .WithLeadingTrivia(leadingTrivia)
                .WithTrailingTrivia(trailingTrivia);

        if (returnText == "Task" || returnText.StartsWith("Task<", StringComparison.Ordinal))
            return returnType;

        if (returnText.StartsWith("ValueTask", StringComparison.Ordinal))
            return returnType;

        return SyntaxFactory.GenericName(
                SyntaxFactory.Identifier("Task"),
                SyntaxFactory.TypeArgumentList(
                    SyntaxFactory.SingletonSeparatedList(returnType.WithoutTrivia())))
            .WithLeadingTrivia(leadingTrivia)
            .WithTrailingTrivia(trailingTrivia);
    }

    /// <summary>
    /// 保留原始节点的 leading/trailing trivia 到新节点。
    /// </summary>
    public static TNode PreserveTrivia<TNode>(TNode newNode, SyntaxNode originalNode) where TNode : SyntaxNode {
        return newNode
            .WithLeadingTrivia(originalNode.GetLeadingTrivia())
            .WithTrailingTrivia(originalNode.GetTrailingTrivia());
    }
}
