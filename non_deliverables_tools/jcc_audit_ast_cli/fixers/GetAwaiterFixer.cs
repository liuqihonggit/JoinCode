namespace JccAuditCli;

/// <summary>
/// 修复 .GetAwaiter().GetResult() 违规 — 在异步方法中替换为 await + ConfigureAwait(false)。
/// 文件遍历方式（不依赖 MSBuildWorkspace），AST 解析每个 .cs 文件。
/// 检测逻辑共享 GetAwaiterPatternDetector（与分析器同一套判定），同检同换。
/// </summary>
public static class GetAwaiterFixer {
    /// <summary>
    /// 修复指定目录下所有 .cs 文件中，可修复的 .GetAwaiter().GetResult()。
    /// 可修复判定: GetAwaiterPatternDetector.IsFixableViolation()（与检测器共享）。
    /// </summary>
    public static async Task<(int fixedFiles, int fixedIssues, int skipped)> FixAllAsync(
        string rootPath, bool dryRun, CancellationToken ct) {

        Console.WriteLine("  遍历所有 .cs 文件，AST 解析方法节点...");

        var csFiles = EnumerateCsFiles(rootPath).ToList();
        Console.WriteLine($"  找到 {csFiles.Count} 个 .cs 文件");

        int fixedFiles = 0, fixedIssues = 0, skipped = 0;

        foreach (var filePath in csFiles) {
            if (ct.IsCancellationRequested) break;

            var sourceText = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var text = SourceText.From(sourceText);
            var tree = CSharpSyntaxTree.ParseText(text, path: filePath);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

            var isTestFile = IsTestFile(filePath);
            var rewriter = new GetAwaiterRewriter(isTestFile);
            var newRoot = rewriter.Visit(root);

            if (newRoot != root && rewriter.FixedCount > 0) {
                fixedFiles++;
                fixedIssues += rewriter.FixedCount;

                if (!dryRun) {
                    var newText = newRoot.ToFullString();
                    await File.WriteAllTextAsync(filePath, newText, ct).ConfigureAwait(false);
                }

                Console.WriteLine($"  {Path.GetRelativePath(rootPath, filePath)}: {rewriter.FixedCount} 处修复");
            }
        }

        return (fixedFiles, fixedIssues, skipped);
    }

    private static bool IsTestFile(string filePath) {
        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains(".tests/") || normalized.Contains("/test/");
    }

    private static IEnumerable<string> EnumerateCsFiles(string rootPath) {
        return Directory.EnumerateFiles(rootPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !ShouldSkipFile(f));
    }

    private static bool ShouldSkipFile(string filePath) {
        var normalized = filePath.Replace('\\', '/');
        if (normalized.Contains("/artifacts/")) return true;
        if (normalized.Contains("/obj/")) return true;
        if (normalized.Contains("/bin/")) return true;
        if (normalized.Contains("/.xxx/")) return true;
        if (normalized.Contains("/.git/")) return true;
        if (normalized.Contains("/bcl_bridge/")) return true;
        if (normalized.Contains("/aot_safety.generator/")) return true;
        if (normalized.Contains("/aot_safety.shared/")) return true;
        if (normalized.EndsWith("SyncFileReader.cs", StringComparison.Ordinal)) return true;
        return false;
    }
}

/// <summary>
/// SyntaxRewriter — 查找并替换 .GetAwaiter().GetResult() 模式。
/// 检测逻辑共享 GetAwaiterPatternDetector.IsFixableViolation()（与检测器同一套判定）。
/// </summary>
internal class GetAwaiterRewriter : CSharpSyntaxRewriter {
    private readonly bool _isTestFile;

    /// <summary>
    /// 已修复的数量
    /// </summary>
    public int FixedCount { get; private set; }

    internal GetAwaiterRewriter(bool isTestFile) {
        _isTestFile = isTestFile;
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node) {
        // 共享检测逻辑 — 与检测器调用同一套判定（同检同换）
        if (!GetAwaiterPatternDetector.IsFixableViolation(node))
            return base.VisitInvocationExpression(node);

        // 提取内部表达式（共享检测器）
        var innerExpr = GetAwaiterPatternDetector.ExtractInnerExpression(node);
        if (innerExpr is null)
            return base.VisitInvocationExpression(node);

        // 创建 await 表达式
        var awaitExpr = CreateAwaitExpression(innerExpr, node);
        FixedCount++;

        return awaitExpr;
    }

    private ExpressionSyntax CreateAwaitExpression(ExpressionSyntax innerExpr, InvocationExpressionSyntax originalNode) {
        // innerExpr.GetAwaiter().GetResult() → await innerExpr.ConfigureAwait(false)
        // 测试代码不加 ConfigureAwait(false)
        ExpressionSyntax awaitedExpr;

        if (_isTestFile) {
            awaitedExpr = innerExpr;
        } else {
            // 加 .ConfigureAwait(false)
            var configureAwaitCall = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    innerExpr.WithoutTrailingTrivia(),
                    SyntaxFactory.IdentifierName("ConfigureAwait")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)))));

            awaitedExpr = configureAwaitCall;
        }

        // 创建 await 表达式，保留原始 trivia
        var awaitKeyword = SyntaxFactory.Token(SyntaxKind.AwaitKeyword).WithTrailingTrivia(SyntaxFactory.Space);
        var awaitExpr = SyntaxFactory.AwaitExpression(awaitKeyword, awaitedExpr);

        // 保留原始节点的 leading/trailing trivia
        return awaitExpr
            .WithLeadingTrivia(originalNode.GetLeadingTrivia())
            .WithTrailingTrivia(originalNode.GetTrailingTrivia());
    }
}
