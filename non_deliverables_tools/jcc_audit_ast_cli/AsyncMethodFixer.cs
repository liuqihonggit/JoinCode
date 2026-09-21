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
    public int FixedMethods { get; private set; }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) {
        // 跳过已 async 的方法
        if (node.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return base.VisitMethodDeclaration(node);

        // 跳过抽象/接口方法（无 body）
        if (node.Body is null && node.ExpressionBody is null)
            return base.VisitMethodDeclaration(node);

        // 检测 body 是否含 await 表达式
        var hasAwait = ContainsAwait(node);
        if (!hasAwait)
            return base.VisitMethodDeclaration(node);

        // 添加 async 修饰符
        var asyncModifier = SyntaxFactory.Token(SyntaxKind.AsyncKeyword);
        var newModifiers = node.Modifiers.Add(asyncModifier);

        // 调整返回类型
        var newReturnType = WrapInTask(node.ReturnType);

        var newNode = node
            .WithModifiers(newModifiers)
            .WithReturnType(newReturnType);

        FixedMethods++;
        return newNode;
    }

    /// <summary>
    /// 检测方法 body 或 expression body 是否含 AwaitExpressionSyntax
    /// </summary>
    private static bool ContainsAwait(MethodDeclarationSyntax node) {
        if (node.Body is not null)
            return node.Body.DescendantNodes().Any(n => n.IsKind(SyntaxKind.AwaitExpression));
        if (node.ExpressionBody is not null)
            return node.ExpressionBody.DescendantNodes().Any(n => n.IsKind(SyntaxKind.AwaitExpression));
        return false;
    }

    /// <summary>
    /// 将返回类型包装为 Task{T}，void → Task
    /// 跳过已经是 Task/ValueTask 的类型
    /// </summary>
    private static TypeSyntax WrapInTask(TypeSyntax returnType) {
        var name = returnType.ToString();

        // 已经是 Task 或 ValueTask，不需要包装
        if (name.StartsWith("Task", StringComparison.Ordinal) ||
            name.StartsWith("ValueTask", StringComparison.Ordinal) ||
            name.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal))
            return returnType;

        // void → Task
        if (name == "void")
            return SyntaxFactory.IdentifierName("Task")
                .WithTrailingTrivia(returnType.GetTrailingTrivia());

        // T → Task<T>
        return SyntaxFactory.GenericName(
            SyntaxFactory.Identifier("Task"),
            SyntaxFactory.TypeArgumentList(
                SyntaxFactory.SingletonSeparatedList(returnType)))
            .WithTrailingTrivia(returnType.GetTrailingTrivia());
    }
}
