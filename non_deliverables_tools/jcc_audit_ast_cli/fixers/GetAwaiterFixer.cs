namespace JccAuditCli;

/// <summary>
/// 修复 .GetAwaiter().GetResult() 违规 — 在同步 private 方法中改 async + await。
/// 文件遍历方式，AST 解析每个 .cs 文件。
/// 检测逻辑共享 GetAwaiterPatternDetector（与分析器同一套判定），同检同换。
/// 修复策略：
///   1. private 方法: 加 async 修饰符 + 改返回类型(void→Task, T→Task<T>) + 替换 invocation→await
///   2. 调用方: 编译错误驱动，用 fix-from-build-errors 修复
/// </summary>
public static class GetAwaiterFixer {
    /// <summary>
    /// 修复指定目录下所有 .cs 文件中，可修复的 .GetAwaiter().GetResult()。
    /// 可修复判定: GetAwaiterPatternDetector.IsFixableViolation()（与检测器共享）。
    /// </summary>
    public static async Task<(int fixedFiles, int fixedIssues, int skipped)> FixAllAsync(
        string rootPath, bool dryRun, CancellationToken ct) {

        Console.WriteLine("  遍历所有 .cs 文件，AST 解析方法节点...");

        var csFiles = FileFilter.EnumerateCsFiles(rootPath).ToList();
        Console.WriteLine($"  找到 {csFiles.Count} 个 .cs 文件");

        int fixedFiles = 0, fixedIssues = 0, skipped = 0;

        foreach (var filePath in csFiles) {
            if (ct.IsCancellationRequested) break;

            var sourceText = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            var text = SourceText.From(sourceText);
            var tree = CSharpSyntaxTree.ParseText(text, path: filePath);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

            var isTestFile = FileFilter.IsTestFile(filePath);
            var skipMethods = DetectLazyConstrainedAndPropagated(root);
            var rewriter = new GetAwaiterRewriter(isTestFile, skipMethods);
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

    /// <summary>
    /// 检测 Lazy&lt;T&gt; 约束的方法 + 传播到被调用的 private 方法。
    /// 传播规则：如果 Lazy 约束方法 A 调用 private 方法 B，则 B 也跳过（A 不能 await B）。
    /// </summary>
    private static HashSet<string> DetectLazyConstrainedAndPropagated(SyntaxNode root) {
        var skipMethods = new HashSet<string>(StringComparer.Ordinal);

        // 1. 检测直接 Lazy 约束的方法名
        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()) {
            var typeText = creation.Type.ToString();
            if (!typeText.StartsWith("Lazy<", StringComparison.Ordinal) &&
                !typeText.StartsWith("System.Lazy<", StringComparison.Ordinal))
                continue;

            var args = creation.ArgumentList?.Arguments;
            if (args is null || args.Value.Count == 0) continue;

            if (args.Value[0].Expression is IdentifierNameSyntax id)
                skipMethods.Add(id.Identifier.ValueText);
        }

        if (skipMethods.Count == 0) return skipMethods;

        // 2. 传播：遍历 Lazy 约束方法的体，收集被调用的方法名
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>()) {
            if (!skipMethods.Contains(method.Identifier.ValueText)) continue;

            foreach (var inv in method.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
                if (inv.Expression is IdentifierNameSyntax calledId)
                    skipMethods.Add(calledId.Identifier.ValueText);
            }
        }

        return skipMethods;
    }
}

/// <summary>
/// SyntaxRewriter — 改 private 方法为 async + 替换 .GetAwaiter().GetResult() → await。
/// 检测逻辑共享 GetAwaiterPatternDetector（与检测器同一套判定）。
/// 只处理中风险（private 方法直接体内的 violation），跳过高风险（public/lambda）和不能改（Main/构造/属性）。
/// </summary>
internal class GetAwaiterRewriter : CSharpSyntaxRewriter {
    private readonly bool _isTestFile;
    private readonly HashSet<string> _skipMethods;
    private readonly Stack<bool> _contextStack = new();
    private bool _inTargetMethod;

    /// <summary>
    /// 已修复的数量
    /// </summary>
    public int FixedCount { get; private set; }

    internal GetAwaiterRewriter(bool isTestFile, HashSet<string> skipMethods) {
        _isTestFile = isTestFile;
        _skipMethods = skipMethods;
    }

    /// <summary>
    /// 判断方法是否是修复目标：private + 非 async + 方法直接体内有可修复的 violation。
    /// "直接体内"指 GetEnclosingFunction 返回的是该方法本身（不是 lambda）。
    /// 跳过 Lazy&lt;T&gt; 约束传播的方法（不能改 async，因为调用方不能 await）。
    /// </summary>
    private bool IsTargetMethod(MethodDeclarationSyntax node) {
        if (!node.Modifiers.Any(SyntaxKind.PrivateKeyword)) return false;
        if (node.Modifiers.Any(SyntaxKind.AsyncKeyword)) return false;
        if (_skipMethods.Contains(node.Identifier.ValueText)) {
            Console.WriteLine($"    跳过 Lazy<T> 约束传播方法: {node.Identifier.ValueText}");
            return false;
        }
        return node.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => GetAwaiterPatternDetector.IsFixableViolation(inv)
                && GetAwaiterPatternDetector.GetEnclosingFunction(inv) is MethodDeclarationSyntax);
    }

    /// <summary>
    /// 重写方法声明：如果是目标方法（private + 非 async + 直接体内有 fixable violation），
    /// 加 async 修饰符 + 改返回类型(void→Task, T→Task<T>)。
    /// 用 _inTargetMethod 标志控制只替换目标方法直接体内的 invocation。
    /// </summary>
    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) {
        var isTarget = IsTargetMethod(node);

        _contextStack.Push(_inTargetMethod);
        _inTargetMethod = isTarget;

        var visited = (MethodDeclarationSyntax?)base.VisitMethodDeclaration(node);

        _inTargetMethod = _contextStack.Pop();

        if (visited is null) return null;
        if (!isTarget) return visited;

        var newModifiers = SyntaxHelpers.AddAsyncModifier(visited).Modifiers;
        var newReturnType = SyntaxHelpers.TransformReturnType(visited.ReturnType);

        return visited
            .WithModifiers(newModifiers)
            .WithReturnType(newReturnType);
    }

    /// <summary>
    /// 转换返回类型: void→Task, T→Task&lt;T&gt;, Task/ValueTask 不变。
    /// </summary>
    private static TypeSyntax TransformReturnType(TypeSyntax returnType) {
        return SyntaxHelpers.TransformReturnType(returnType);
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node) {
        // 只处理目标方法直接体内的 violation（中风险 private 方法）
        if (!_inTargetMethod) return base.VisitInvocationExpression(node);
        if (!GetAwaiterPatternDetector.IsFixableViolation(node))
            return base.VisitInvocationExpression(node);
        // 跳过 lambda 内的（高险风，委托类型变更）
        if (GetAwaiterPatternDetector.GetEnclosingFunction(node) is not MethodDeclarationSyntax)
            return base.VisitInvocationExpression(node);

        var innerExpr = GetAwaiterPatternDetector.ExtractInnerExpression(node);
        if (innerExpr is null)
            return base.VisitInvocationExpression(node);

        var awaitExpr = CreateAwaitExpression(innerExpr, node);
        FixedCount++;

        return awaitExpr;
    }

    private ExpressionSyntax CreateAwaitExpression(ExpressionSyntax innerExpr, InvocationExpressionSyntax originalNode) {
        return SyntaxHelpers.CreateAwaitExpression(innerExpr, originalNode, _isTestFile);
    }
}
