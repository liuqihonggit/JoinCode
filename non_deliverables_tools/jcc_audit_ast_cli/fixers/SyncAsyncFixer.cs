namespace JccAuditCli;

/// <summary>
/// Solution 级别同步化重写器 — 把 async/await 转为同步代码
/// </summary>
public static class SyncAsyncFixer {

    /// <summary>
    /// 同步化解决方案中所有非 UI 项目的 async/await
    /// </summary>
    public static async Task<(int FixedFiles, int FixedMethods, int FixedAwaits, int SkippedFiles)> SyncAllAsync(
        string solutionPath, bool dryRun, string[]? excludeDirs = null, string[]? includeDirs = null, CancellationToken ct = default) {

        var solutionFullPath = Path.GetFullPath(solutionPath);
        if (!File.Exists(solutionFullPath))
            throw new ArgumentException($"解决方案不存在: {solutionFullPath}");

        var excludes = excludeDirs ?? [];
        var includes = includeDirs ?? [];
        using var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionFullPath, cancellationToken: ct);

        var fixedFiles = 0;
        var fixedMethods = 0;
        var fixedAwaits = 0;
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
                if (ShouldSkipPath(tree.FilePath, excludes, includes)) { skippedFiles++; continue; }
                if (processedFiles.Contains(tree.FilePath)) { skippedFiles++; continue; }
                processedFiles.Add(tree.FilePath);

                var root = await tree.GetRootAsync(ct);
                var model = compilation.GetSemanticModel(tree);
                var rewriter = new SyncAsyncRewriter(model);
                var newRoot = rewriter.Visit(root);

                if (rewriter.FixedMethods == 0 && rewriter.FixedAwaits == 0) { skippedFiles++; continue; }
                var newSource = newRoot.ToFullString();
                if (newSource == root.ToFullString()) { skippedFiles++; continue; }

                var relPath = Path.GetRelativePath(Path.GetDirectoryName(solutionFullPath)!, tree.FilePath);
                Console.WriteLine($"  {relPath}: {rewriter.FixedMethods} 方法, {rewriter.FixedAwaits} await");

                if (!dryRun)
                    await File.WriteAllTextAsync(tree.FilePath, newSource, ct);

                fixedFiles++;
                fixedMethods += rewriter.FixedMethods;
                fixedAwaits += rewriter.FixedAwaits;
            }
        }
        return (fixedFiles, fixedMethods, fixedAwaits, skippedFiles);
    }

    private static bool ShouldSkipPath(string filePath, string[] excludes, string[] includes) {
        var normalized = filePath.Replace('\\', '/');
        if (normalized.Contains("/artifacts/")) return true;
        if (normalized.Contains("/obj/")) return true;
        if (normalized.Contains("/bin/")) return true;
        if (normalized.Contains("/bcl_bridge/")) return true;
        if (normalized.Contains("/aot_safety.generator/")) return true;
        foreach (var ex in excludes) {
            if (normalized.Contains(ex.Replace('\\', '/'))) return true;
        }
        if (includes.Length > 0) {
            var inInclude = false;
            foreach (var inc in includes) {
                if (normalized.Contains(inc.Replace('\\', '/'))) { inInclude = true; break; }
            }
            if (!inInclude) return true;
        }
        return false;
    }
}

/// <summary>
/// SyntaxRewriter — 同步化 async/await
/// </summary>
internal class SyncAsyncRewriter : CSharpSyntaxRewriter {
    private readonly SemanticModel _model;
    private bool _inAsyncMethod;

    /// <summary>已同步化的方法数</summary>
    public int FixedMethods { get; private set; }
    /// <summary>已处理的 await 数</summary>
    public int FixedAwaits { get; private set; }

    internal SyncAsyncRewriter(SemanticModel model) {
        _model = model;
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) {
        var wasAsync = node.Modifiers.Any(SyntaxKind.AsyncKeyword);
        if (!wasAsync)
            return base.VisitMethodDeclaration(node);

        var prevInAsync = _inAsyncMethod;
        _inAsyncMethod = true;

        var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;

        _inAsyncMethod = prevInAsync;

        var newReturnType = SyncReturnType(visited.ReturnType);
        var asyncIndex = visited.Modifiers.IndexOf(SyntaxKind.AsyncKeyword);
        var newModifiers = visited.Modifiers.RemoveAt(asyncIndex);

        var result = visited.WithReturnType(newReturnType).WithModifiers(newModifiers);
        FixedMethods++;
        return result;
    }

    public override SyntaxNode? VisitAwaitExpression(AwaitExpressionSyntax node) {
        var innerExpr = node.Expression;

        if (IsSourceAsyncMethodCall(innerExpr, out _)) {
            FixedAwaits++;
            return base.VisitAwaitExpression(node) is AwaitExpressionSyntax visited
                ? (SyntaxNode)visited.Expression
                : innerExpr;
        }

        FixedAwaits++;
        var visitedInner = (ExpressionSyntax)base.VisitAwaitExpression(node)!;
        var getResult = CreateGetAwaiterGetResult(innerExpr);
        return SyntaxHelpers.PreserveTrivia(getResult, node);
    }

    public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node) {
        if (node.Expression is null)
            return base.VisitReturnStatement(node);

        var expr = node.Expression;
        var newExpr = TryTransformReturnExpression(expr);
        if (newExpr is not null) {
            FixedAwaits++;
            return node.WithExpression(newExpr);
        }

        return base.VisitReturnStatement(node);
    }

    public override SyntaxNode? VisitUsingStatement(UsingStatementSyntax node) {
        if (!node.AwaitKeyword.IsKind(SyntaxKind.None)) {
            var newKeyword = SyntaxFactory.Token(SyntaxKind.None);
            FixedAwaits++;
            return base.VisitUsingStatement(node) is UsingStatementSyntax visited
                ? visited.WithAwaitKeyword(newKeyword)
                : node.WithAwaitKeyword(newKeyword);
        }
        return base.VisitUsingStatement(node);
    }

    public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node) {
        if (!node.AwaitKeyword.IsKind(SyntaxKind.None)) {
            var newKeyword = SyntaxFactory.Token(SyntaxKind.None);
            FixedAwaits++;
            return base.VisitLocalDeclarationStatement(node) is LocalDeclarationStatementSyntax visited
                ? visited.WithAwaitKeyword(newKeyword)
                : node.WithAwaitKeyword(newKeyword);
        }
        return base.VisitLocalDeclarationStatement(node);
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node) {
        if (!node.AwaitKeyword.IsKind(SyntaxKind.None)) {
            var newKeyword = SyntaxFactory.Token(SyntaxKind.None);
            FixedAwaits++;
            return base.VisitForEachStatement(node) is ForEachStatementSyntax visited
                ? visited.WithAwaitKeyword(newKeyword)
                : node.WithAwaitKeyword(newKeyword);
        }
        return base.VisitForEachStatement(node);
    }

    private TypeSyntax SyncReturnType(TypeSyntax returnType) {
        var text = returnType.ToString().Trim();

        if (text == "Task" || text == "ValueTask")
            return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword))
                .WithTriviaFrom(returnType);

        if (text.StartsWith("Task<", StringComparison.Ordinal) && text.EndsWith('>'))
            return ExtractGenericArg(returnType, text, "Task<");

        if (text.StartsWith("ValueTask<", StringComparison.Ordinal) && text.EndsWith('>'))
            return ExtractGenericArg(returnType, text, "ValueTask<");

        return returnType;
    }

    private static TypeSyntax ExtractGenericArg(TypeSyntax returnType, string text, string prefix) {
        var inner = text.Substring(prefix.Length, text.Length - prefix.Length - 1);
        return SyntaxFactory.ParseTypeName(inner).WithTriviaFrom(returnType);
    }

    private bool IsSourceAsyncMethodCall(ExpressionSyntax expr, out IMethodSymbol? methodSymbol) {
        methodSymbol = null;

        InvocationExpressionSyntax? invocation = null;
        if (expr is InvocationExpressionSyntax inv)
            invocation = inv;
        else if (expr is MemberAccessExpressionSyntax ma && ma.Name.Identifier.ValueText == "ConfigureAwait")
            invocation = ma.Expression as InvocationExpressionSyntax;

        if (invocation is null)
            return false;

        var symbolInfo = _model.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return false;

        methodSymbol = method;
        return method.IsAsync && method.ContainingType is not null && method.DeclaringSyntaxReferences.Length > 0;
    }

    private static ExpressionSyntax CreateGetAwaiterGetResult(ExpressionSyntax expr) {
        return SyntaxHelpers.CreateGetAwaiterGetResult(expr, expr);
    }

    private ExpressionSyntax? TryTransformReturnExpression(ExpressionSyntax expr) {
        var text = expr.ToString().Trim();

        if (text == "Task.CompletedTask" || text == "Task.CompletedTask.ConfigureAwait(false)")
            return null;

        if (text.StartsWith("Task.FromResult(", StringComparison.Ordinal)) {
            var inner = ExtractFromResultArg(expr);
            if (inner is not null)
                return inner;
        }

        return null;
    }

    private static ExpressionSyntax? ExtractFromResultArg(ExpressionSyntax expr) {
        if (expr is InvocationExpressionSyntax inv
            && inv.Expression is MemberAccessExpressionSyntax ma
            && ma.Name.Identifier.ValueText == "FromResult"
            && inv.ArgumentList.Arguments.Count == 1) {
            return inv.ArgumentList.Arguments[0].Expression;
        }
        return null;
    }
}
