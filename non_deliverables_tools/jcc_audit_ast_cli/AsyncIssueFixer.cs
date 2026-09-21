namespace JccAuditCli;

/// <summary>
/// Roslyn AST 综合修复器 — 用语义模型一次性修复所有异步相关问题
/// </summary>
public static class AsyncIssueFixer {

    /// <summary>
    /// 加载解决方案，用语义模型检测并修复所有异步相关问题
    /// </summary>
    public static async Task<(int FixedFiles, int FixedIssues, int SkippedFiles)> FixAllAsync(
        string solutionPath, bool dryRun, CancellationToken ct = default) {

        var solutionFullPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(solutionFullPath))
            throw new ArgumentException($"[GEN065] 解决方案不存在: {solutionFullPath}");

        using var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionFullPath, cancellationToken: ct);

        var fixedFiles = 0;
        var fixedIssues = 0;
        var skippedFiles = 0;
        var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects) {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;

            foreach (var tree in compilation.SyntaxTrees) {
                ct.ThrowIfCancellationRequested();
                if (tree.FilePath is null || !tree.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ShouldSkipPath(tree.FilePath)) { skippedFiles++; continue; }
                if (processedFiles.Contains(tree.FilePath)) { skippedFiles++; continue; }
                processedFiles.Add(tree.FilePath);

                var root = await tree.GetRootAsync(ct);
                var model = compilation.GetSemanticModel(tree);
                var rewriter = new AsyncIssueRewriter(model, tree.FilePath);
                var newRoot = rewriter.Visit(root);

                if (rewriter.FixedIssues == 0) { skippedFiles++; continue; }
                var newSource = newRoot.ToFullString();
                if (newSource == root.ToFullString()) { skippedFiles++; continue; }

                var relPath = Path.GetRelativePath(Path.GetDirectoryName(solutionFullPath)!, tree.FilePath);
                Console.WriteLine($"  {relPath}: {rewriter.FixedIssues} 个问题修复");

                if (!dryRun)
                    await File.WriteAllTextAsync(tree.FilePath, newSource, ct);

                fixedFiles++;
                fixedIssues += rewriter.FixedIssues;
            }
        }
        return (fixedFiles, fixedIssues, skippedFiles);
    }

    private static bool ShouldSkipPath(string path) {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/") ||
               normalized.Contains("/bin/") ||
               normalized.Contains("/artifacts/") ||
               normalized.Contains("/.xxx/");
    }
}

/// <summary>
/// 综合修复器 — 5 类修复：
/// 1. 方法重命名 Clear→ClearAsync, RemoveScope→RemoveScopeAsync + 加 await
/// 2. 移除测试代码中 ConfigureAwait(false)
/// 3. 加 await 到未 await 的 Task/ValueTask 方法调用
/// 4. using → await using（IAsyncDisposable 类型）
/// 5. 修复 await xxx.Should() → (await xxx).Should()
///
/// 关键：所有 SemanticModel 查询必须在 base.Visit 之前用原始节点做，
/// 因为 base.Visit 返回的新节点不在原始语法树中，SemanticModel 查询会失败。
/// </summary>
internal class AsyncIssueRewriter : CSharpSyntaxRewriter {
    private readonly SemanticModel _model;
    private readonly bool _isTestFile;
    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;

    public int FixedIssues { get; private set; }

    internal AsyncIssueRewriter(SemanticModel model, string filePath) {
        _model = model;
        _isTestFile = filePath.Contains(".tests/") || filePath.Contains("/test/");
    }

    public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node) {
        // 修复4：using → await using（用原始 node 查询 SemanticModel）
        if (node.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) && !node.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword)) {
            if (IsAsyncDisposableDeclaration(node)) {
                FixedIssues++;
                var visited = (LocalDeclarationStatementSyntax)base.VisitLocalDeclarationStatement(node)!;
                return visited.WithAwaitKeyword(
                    SyntaxFactory.Token(SyntaxKind.AwaitKeyword).WithTrailingTrivia(SyntaxFactory.Whitespace(" ")));
            }
        }
        return base.VisitLocalDeclarationStatement(node);
    }

    public override SyntaxNode? VisitUsingStatement(UsingStatementSyntax node) {
        // 修复4：using → await using（用原始 node 查询 SemanticModel）
        if (!node.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword) && IsAsyncDisposableUsing(node)) {
            FixedIssues++;
            var visited = (UsingStatementSyntax)base.VisitUsingStatement(node)!;
            return visited.WithAwaitKeyword(
                SyntaxFactory.Token(SyntaxKind.AwaitKeyword).WithTrailingTrivia(SyntaxFactory.Whitespace(" ")));
        }
        return base.VisitUsingStatement(node);
    }

    public override SyntaxNode? VisitAwaitExpression(AwaitExpressionSyntax node) {
        // 修复5：await xxx.Should() → (await xxx).Should()（用原始 node 查询 SemanticModel）
        var fixedNode = TryFixAwaitShouldPattern(node);
        if (fixedNode is not null) { FixedIssues++; return fixedNode; }
        return base.VisitAwaitExpression(node);
    }

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node) {
        // 修复3c：对 obj.Method() 语句加 await（用原始 node 查询 SemanticModel）
        var fixedExpr = TryAddAwaitToStatement(node);
        if (fixedExpr is not null) { FixedIssues++; return fixedExpr; }
        return base.VisitExpressionStatement(node);
    }

    public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node) {
        // 修复3b：对 x = obj.Method() 加 await（用原始 node 查询 SemanticModel）
        var fixedNode = TryAddAwaitToAssignment(node);
        if (fixedNode is not null) { FixedIssues++; return fixedNode; }
        return base.VisitAssignmentExpression(node);
    }

    public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node) {
        // 修复3：对 var x = obj.Method() 加 await（用原始 node 查询 SemanticModel）
        if (node.Initializer is not null) {
            var fixedNode = TryAddAwaitToInitializer(node);
            if (fixedNode is not null) { FixedIssues++; return fixedNode; }
        }
        return base.VisitVariableDeclarator(node);
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node) {
        // 修复1：方法重命名（纯语法匹配，不需要 SemanticModel）
        var renamed = TryRenameMethod(node);
        if (renamed is not null) { FixedIssues++; return renamed; }
        // 修复2：移除 ConfigureAwait(false)（纯语法匹配）
        if (_isTestFile) {
            var withoutCa = TryRemoveConfigureAwait(node);
            if (withoutCa is not null) { FixedIssues++; return withoutCa; }
        }
        return base.VisitInvocationExpression(node);
    }

    /// <summary>
    /// 修复1：SessionRouter.Clear() → await SessionRouter.ClearAsync()
    ///        SessionRouter.RemoveScope(x) → await SessionRouter.RemoveScopeAsync(x)
    /// </summary>
    private SyntaxNode? TryRenameMethod(InvocationExpressionSyntax invocation) {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;
        if (memberAccess.Expression is not IdentifierNameSyntax receiver)
            return null;
        if (receiver.Identifier.ValueText != "SessionRouter")
            return null;

        var methodName = memberAccess.Name.Identifier.ValueText;
        var newMethodName = methodName switch {
            "Clear" => "ClearAsync",
            "RemoveScope" => "RemoveScopeAsync",
            _ => null
        };
        if (newMethodName is null) return null;
        if (IsAlreadyAwaited(invocation)) return null;

        var newMemberAccess = memberAccess.WithName(
            SyntaxFactory.IdentifierName(newMethodName).WithTriviaFrom(memberAccess.Name));
        var newInvocation = invocation.WithExpression(newMemberAccess);
        return CreateAwait(newInvocation)
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia());
    }

    /// <summary>
    /// 修复2：移除 ConfigureAwait(false) — await xxx.ConfigureAwait(false) → await xxx
    /// </summary>
    private static SyntaxNode? TryRemoveConfigureAwait(InvocationExpressionSyntax invocation) {
        if (invocation.Expression is not MemberAccessExpressionSyntax ma)
            return null;
        if (ma.Name.Identifier.ValueText != "ConfigureAwait")
            return null;
        if (invocation.ArgumentList.Arguments.Count != 1)
            return null;
        var arg = invocation.ArgumentList.Arguments[0].Expression;
        if (!arg.IsKind(SyntaxKind.FalseLiteralExpression))
            return null;
        return ma.Expression;
    }

    /// <summary>
    /// 修复3：对 var x = obj.Method() 加 await，当 Method 返回 Task/ValueTask 且未被 await
    /// </summary>
    private VariableDeclaratorSyntax? TryAddAwaitToInitializer(VariableDeclaratorSyntax declarator) {
        var initializer = declarator.Initializer;
        if (initializer is null) return null;
        var value = initializer.Value;
        if (IsAlreadyAwaited(value)) return null;
        if (!ReturnsTaskOrValueTask(value)) return null;
        var awaited = CreateAwait(value).WithTriviaFrom(value);
        return declarator.WithInitializer(initializer.WithValue(awaited));
    }

    /// <summary>
    /// 修复3b：对 x = obj.Method() 加 await
    /// </summary>
    private AssignmentExpressionSyntax? TryAddAwaitToAssignment(AssignmentExpressionSyntax assignment) {
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)) return null;
        var value = assignment.Right;
        if (IsAlreadyAwaited(value)) return null;
        if (!ReturnsTaskOrValueTask(value)) return null;
        var awaited = CreateAwait(value).WithTriviaFrom(value);
        return assignment.WithRight(awaited);
    }

    /// <summary>
    /// 修复3c：对 obj.Method() 语句加 await（如 SessionRouter.ClearAsync()）
    /// </summary>
    private ExpressionStatementSyntax? TryAddAwaitToStatement(ExpressionStatementSyntax stmt) {
        var expr = stmt.Expression;
        if (expr is not InvocationExpressionSyntax invocation) return null;
        if (IsAlreadyAwaited(invocation)) return null;
        if (!ReturnsTaskOrValueTask(invocation)) return null;
        var awaited = CreateAwait(invocation).WithTriviaFrom(invocation);
        return stmt.WithExpression(awaited);
    }

    /// <summary>
    /// 修复5：await xxx.Should().Be(...) → (await xxx).Should().Be(...)
    /// 检测 await 表达式内部含 .Should() 调用，且最内层返回 Task/ValueTask
    /// </summary>
    private SyntaxNode? TryFixAwaitShouldPattern(AwaitExpressionSyntax awaitExpr) {
        var inner = awaitExpr.Expression;
        var shouldAccess = FindShouldMemberAccess(inner);
        if (shouldAccess is null) return null;
        var innerCall = shouldAccess.Expression;
        if (IsAlreadyAwaited(innerCall)) return null;
        if (!ReturnsTaskOrValueTask(innerCall)) return null;
        var awaitedInner = CreateAwait(innerCall).WithTriviaFrom(innerCall);
        var parenthesized = SyntaxFactory.ParenthesizedExpression(awaitedInner);
        var newInner = inner.ReplaceNode(innerCall, parenthesized);
        return awaitExpr.WithExpression((ExpressionSyntax)newInner);
    }

    private static MemberAccessExpressionSyntax? FindShouldMemberAccess(SyntaxNode node) {
        foreach (var desc in node.DescendantNodes()) {
            if (desc is MemberAccessExpressionSyntax ma && ma.Name.Identifier.ValueText == "Should")
                return ma;
        }
        return null;
    }

    /// <summary>
    /// 修复4辅助：判断 LocalDeclaration 是否声明 IAsyncDisposable 变量
    /// </summary>
    private bool IsAsyncDisposableDeclaration(LocalDeclarationStatementSyntax node) {
        if (node.Declaration.Variables.Count == 0) return false;
        var type = _model.GetTypeInfo(node.Declaration.Type).Type;
        return type is not null && ImplementsIAsyncDisposable(type);
    }

    private bool IsAsyncDisposableUsing(UsingStatementSyntax node) {
        if (node.Declaration is not null) {
            var type = _model.GetTypeInfo(node.Declaration.Type).Type;
            return type is not null && ImplementsIAsyncDisposable(type);
        }
        if (node.Expression is not null) {
            var type = _model.GetTypeInfo(node.Expression).Type;
            return type is not null && ImplementsIAsyncDisposable(type);
        }
        return false;
    }

    private bool ImplementsIAsyncDisposable(ITypeSymbol type) {
        var asyncDisposable = _model.Compilation.GetTypeByMetadataName("System.IAsyncDisposable");
        if (asyncDisposable is null) return false;
        if (type.Equals(asyncDisposable, SymbolEqualityComparer.IncludeNullability)) return true;
        return type.AllInterfaces.Any(i => i.Equals(asyncDisposable, SymbolEqualityComparer.IncludeNullability));
    }

    /// <summary>
    /// 判断表达式返回 Task/ValueTask 且未被 await
    /// </summary>
    private bool ReturnsTaskOrValueTask(SyntaxNode expression) {
        var type = _model.GetTypeInfo(expression).Type;
        if (type is null) return false;
        var name = type.ToDisplayString(TypeFormat);
        return name.StartsWith("Task", StringComparison.Ordinal) ||
               name.StartsWith("ValueTask", StringComparison.Ordinal) ||
               name.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal) ||
               name.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal);
    }

    private static bool IsAlreadyAwaited(SyntaxNode node) {
        return node.Parent is AwaitExpressionSyntax;
    }

    /// <summary>
    /// 创建 await 表达式。库代码加 ConfigureAwait(false)，测试代码不加。
    /// </summary>
    private AwaitExpressionSyntax CreateAwait(ExpressionSyntax expression) {
        var awaitToken = SyntaxFactory.Token(SyntaxKind.AwaitKeyword)
            .WithTrailingTrivia(SyntaxFactory.Whitespace(" "));
        var awaitedExpr = expression;
        if (!_isTestFile) {
            var configureAwait = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    expression,
                    SyntaxFactory.Token(SyntaxKind.DotToken),
                    SyntaxFactory.IdentifierName("ConfigureAwait")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)))));
            awaitedExpr = configureAwait;
        }
        return SyntaxFactory.AwaitExpression(awaitToken, awaitedExpr);
    }
}
