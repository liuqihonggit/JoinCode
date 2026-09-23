namespace JccAuditCli;

/// <summary>
/// Roslyn AST 修复器 — 检测方法 body 含 await 但方法签名非 async，自动添加 async 修饰符并调整返回类型
/// </summary>
public static class AsyncMethodFixer {

    /// <summary>
    /// 扫描目录下所有 .cs 文件，修复含 await 的非 async 方法
    /// </summary>
    public static async Task<(int FixedFiles, int FixedMethods, int SkippedFiles)> FixDirectoryAsync(
        string rootPath, bool dryRun, bool skipTests, CancellationToken ct = default) {

        var rootDir = Path.GetFullPath(rootPath);
        if (!Directory.Exists(rootDir))
            throw new ArgumentException($"[GEN065] 目录不存在: {rootDir}");

        var csFiles = Directory.GetFiles(rootDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !ShouldSkip(f, skipTests))
            .ToList();

        var fixedFiles = 0;
        var fixedMethods = 0;
        var skippedFiles = 0;

        foreach (var file in csFiles) {
            ct.ThrowIfCancellationRequested();

            var source = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            var tree = CSharpSyntaxTree.ParseText(source, path: file);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

            var rewriter = new AsyncMethodRewriter();
            var newRoot = rewriter.Visit(root);

            if (rewriter.FixedMethods == 0) {
                skippedFiles++;
                continue;
            }

            var newSource = newRoot.ToFullString();
            if (newSource == source) {
                skippedFiles++;
                continue;
            }

            Console.WriteLine($"  {Path.GetRelativePath(rootDir, file)}: {rewriter.FixedMethods} 个方法修复");

            if (!dryRun) {
                await File.WriteAllTextAsync(file, newSource, ct).ConfigureAwait(false);
            }

            fixedFiles++;
            fixedMethods += rewriter.FixedMethods;
        }

        return (fixedFiles, fixedMethods, skippedFiles);
    }

    private static bool ShouldSkip(string path, bool skipTests) {
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
            return true;
        if (normalized.Contains("/.xxx/"))
            return true;
        if (skipTests && (normalized.Contains(".tests/") || normalized.Contains("/test/")))
            return true;
        return false;
    }
}

/// <summary>
/// SyntaxRewriter — 遍历方法声明，检测 body 含 await 但签名非 async
/// </summary>
internal class AsyncMethodRewriter : CSharpSyntaxRewriter {
    /// <summary>
    /// 已修复的方法数
    /// </summary>
    public int FixedMethods { get; private set; }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) {
        // 跳过已 async 的方法
        if (node.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return base.VisitMethodDeclaration(node);

        // 跳过抽象/接口方法（无 body）
        if (node.Body is null && node.ExpressionBody is null)
            return base.VisitMethodDeclaration(node);

        // 检测 body 是否含 await 表达式（排除 lambda/local function 内的 await）
        var hasAwait = ContainsDirectAwait(node);
        if (!hasAwait)
            return base.VisitMethodDeclaration(node);

        // 添加 async 修饰符 + 调整返回类型（共享 SyntaxHelpers）
        var newNode = SyntaxHelpers.AddAsyncModifier(node);
        var newReturnType = SyntaxHelpers.TransformReturnType(node.ReturnType);

        newNode = newNode.WithReturnType(newReturnType);

        FixedMethods++;
        return newNode;
    }

    /// <summary>
    /// 检测方法 body 是否含直接 await（排除 lambda/local function 内的 await）
    /// 遍历所有 await 关键字 token，覆盖 await expr / await using / await foreach
    /// </summary>
    private static bool ContainsDirectAwait(MethodDeclarationSyntax node) {
        var bodyNode = node.Body ?? (SyntaxNode?)node.ExpressionBody;
        if (bodyNode is null)
            return false;

        foreach (var token in bodyNode.DescendantTokens()) {
            if (!token.IsKind(SyntaxKind.AwaitKeyword))
                continue;
            if (token.Parent is null)
                continue;
            if (!IsInsideLambdaOrLocalFunction(token.Parent, bodyNode))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 判断 await 是否在 lambda 或 local function 内部
    /// </summary>
    private static bool IsInsideLambdaOrLocalFunction(SyntaxNode node, SyntaxNode bodyBoundary) {
        var current = node.Parent;
        while (current is not null && current != bodyBoundary) {
            if (current is ParenthesizedLambdaExpressionSyntax or
                SimpleLambdaExpressionSyntax or
                LocalFunctionStatementSyntax)
                return true;
            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// 将返回类型包装为 Task{T}，void → Task
    /// 跳过已经是 Task/ValueTask 的类型
    /// </summary>
}
