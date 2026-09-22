using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace JccAuditCli;

/// <summary>
/// 修复 .GetAwaiter().GetResult() 违规 — 替换为 await + ConfigureAwait(false)
/// </summary>
public static class GetAwaiterFixer {
    /// <summary>
    /// 修复解决方案中所有 .GetAwaiter().GetResult() 违规
    /// </summary>
    public static async Task<(int fixedFiles, int fixedIssues, int skipped)> FixAllAsync(string solutionPath, bool dryRun, CancellationToken ct) {
        var workspace = MSBuildWorkspace.Create();

        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct).ConfigureAwait(false);

        var fixedFiles = 0;
        var fixedIssues = 0;
        var skipped = 0;

        foreach (var project in solution.Projects) {
            if (ct.IsCancellationRequested) break;

            foreach (var document in project.Documents) {
                if (ct.IsCancellationRequested) break;

                var filePath = document.FilePath;
                if (filePath is null) continue;
                if (ShouldSkipFile(filePath)) {
                    skipped++;
                    continue;
                }

                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is null) continue;

                var isTestFile = IsTestFile(filePath);
                var rewriter = new GetAwaiterRewriter(isTestFile);
                var newRoot = rewriter.Visit(root);

                if (newRoot != root && rewriter.FixedCount > 0) {
                    fixedFiles++;
                    fixedIssues += rewriter.FixedCount;

                    if (!dryRun) {
                        var newDoc = document.WithSyntaxRoot(newRoot);
                        var text = await newDoc.GetTextAsync(ct).ConfigureAwait(false);
                        await File.WriteAllTextAsync(filePath, text.ToString(), ct).ConfigureAwait(false);
                    }

                    Console.WriteLine($"  {Path.GetFileName(filePath)}: {rewriter.FixedCount} 处修复");
                }
            }
        }

        return (fixedFiles, fixedIssues, skipped);
    }

    private static bool ShouldSkipFile(string filePath) {
        var normalized = filePath.Replace('\\', '/');
        if (normalized.Contains("/artifacts/")) return true;
        if (normalized.Contains("/obj/")) return true;
        if (normalized.Contains("/bin/")) return true;
        if (normalized.Contains("/bcl_bridge/")) return true;
        if (normalized.EndsWith("SyncFileReader.cs", StringComparison.Ordinal)) return true;
        if (normalized.EndsWith("Program.cs", StringComparison.Ordinal) && normalized.Contains("/tui/")) return true;
        if (normalized.Contains("/aot_safety.generator/")) return true;
        return false;
    }

    private static bool IsTestFile(string filePath) {
        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains(".tests/") || normalized.Contains("/test/");
    }
}

/// <summary>
/// SyntaxRewriter — 查找并替换 .GetAwaiter().GetResult() 模式
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
        // 检测 pattern: expr.GetAwaiter().GetResult()
        // node = invocation of "GetResult"
        // node.Expression = MemberAccessExpression(Name=GetResult, Expression=invocation of "GetAwaiter")
        var innerExpr = TryExtractGetAwaiterPattern(node);
        if (innerExpr is null)
            return base.VisitInvocationExpression(node);

        // 跳过：在 Main 方法中
        if (IsInMainMethod(node))
            return base.VisitInvocationExpression(node);

        // 跳过：在属性 getter 中（AGENTS.md 例外）
        if (IsInPropertyGetter(node))
            return base.VisitInvocationExpression(node);

        // 创建 await 表达式
        var awaitExpr = CreateAwaitExpression(innerExpr, node);
        FixedCount++;

        return awaitExpr;
    }

    /// <summary>
    /// 检测 expr.GetAwaiter().GetResult() 模式，返回 expr
    /// </summary>
    private static ExpressionSyntax? TryExtractGetAwaiterPattern(InvocationExpressionSyntax node) {
        // node.Expression 应该是 MemberAccessExpression，Name = "GetResult"
        if (node.Expression is not MemberAccessExpressionSyntax getResultAccess)
            return null;
        if (getResultAccess.Name.Identifier.ValueText != "GetResult")
            return null;

        // getResultAccess.Expression 应该是 InvocationExpression，调用 "GetAwaiter"
        if (getResultAccess.Expression is not InvocationExpressionSyntax getAwaiterInvocation)
            return null;
        if (getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax getAwaiterAccess)
            return null;
        if (getAwaiterAccess.Name.Identifier.ValueText != "GetAwaiter")
            return null;

        // 返回 GetAwaiter 的接收者（即需要 await 的表达式）
        return getAwaiterAccess.Expression;
    }

    private static bool IsInMainMethod(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is MethodDeclarationSyntax method) {
                if (method.Identifier.ValueText == "Main")
                    return true;
            }
            current = current.Parent;
        }
        return false;
    }

    private static bool IsInPropertyGetter(SyntaxNode node) {
        var current = node.Parent;
        while (current is not null) {
            if (current is AccessorDeclarationSyntax accessor && accessor.Kind() == SyntaxKind.GetAccessorDeclaration)
                return true;
            current = current.Parent;
        }
        return false;
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
