namespace JccAuditCli;

/// <summary>
/// 编译错误驱动的修复器 — 运行 dotnet build，解析 CS4014/CS0029/CS1503/CS1061 错误位置，精确加 await
/// </summary>
public static class BuildErrorFixer {

    /// <summary>
    /// 运行 dotnet build 获取错误，在错误位置加 await
    /// </summary>
    public static async Task<(int FixedFiles, int FixedIssues)> FixFromBuildErrorsAsync(
        string solutionPath, bool dryRun, CancellationToken ct = default) {

        var errors = await CollectBuildErrorsAsync(solutionPath, ct);
        if (errors.Count == 0) {
            Console.WriteLine("没有 CS4014/CS0029/CS1503/CS1061 错误。");
            return (0, 0);
        }

        Console.WriteLine($"发现 {errors.Count} 个 CS4014/CS0029/CS1503/CS1061 错误。");

        var byFile = errors.GroupBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase);
        var fixedFiles = 0;
        var fixedIssues = 0;

        foreach (var group in byFile) {
            ct.ThrowIfCancellationRequested();
            var errorMap = group.GroupBy(e => e.Line).ToDictionary(g => g.Key, g => g.First().ErrorCode);
            var isTestFile = FileFilter.IsTestFile(group.Key);

            var source = await File.ReadAllTextAsync(group.Key, ct);
            var tree = CSharpSyntaxTree.ParseText(source, path: group.Key);
            var root = await tree.GetRootAsync(ct);

            var rewriter = new UnawaitedVariableRewriter(errorMap, isTestFile);
            var newRoot = rewriter.Visit(root);

            if (rewriter.FixedCount == 0) continue;

            var newSource = newRoot.ToFullString();
            if (newSource == source) continue;

            Console.WriteLine($"  {Path.GetFileName(group.Key)}: {rewriter.FixedCount} 处加 await");

            if (!dryRun)
                await File.WriteAllTextAsync(group.Key, newSource, ct);

            fixedFiles++;
            fixedIssues += rewriter.FixedCount;
        }

        return (fixedFiles, fixedIssues);
    }

    /// <summary>
    /// 运行 dotnet build，解析 CS4014/CS0029/CS1503/CS1061 错误
    /// </summary>
    private static async Task<List<BuildError>> CollectBuildErrorsAsync(string solutionPath, CancellationToken ct) {
        var slnFullPath = Path.GetFullPath(solutionPath);
        var psi = new System.Diagnostics.ProcessStartInfo {
            FileName = "cmd",
            Arguments = $"/c dotnet build \"{slnFullPath}\" --no-restore 2>&1",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var errors = new List<BuildError>();
        var regex = new System.Text.RegularExpressions.Regex(
            @"^(.+?)\((\d+),(\d+)\):\s*error\s+(CS4014|CS0029|CS1503|CS1061)",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        foreach (System.Text.RegularExpressions.Match m in regex.Matches(output)) {
            var file = m.Groups[1].Value.Trim();
            if (FileFilter.ShouldSkipFile(file)) continue;
            errors.Add(new BuildError(
                file,
                int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[3].Value),
                m.Groups[4].Value));
        }

        return errors;
    }

    private sealed record BuildError(string FilePath, int Line, int Column, string ErrorCode);
}

/// <summary>
/// 在指定行加 await — 处理 CS4014(裸语句)/CS0029(变量声明)/CS1503(参数)
/// </summary>
internal class UnawaitedVariableRewriter : CSharpSyntaxRewriter {
    private readonly Dictionary<int, string> _errorMap;
    private readonly bool _isTestFile;

    /// <summary>
    /// 已修复的数量
    /// </summary>
    public int FixedCount { get; private set; }

    internal UnawaitedVariableRewriter(Dictionary<int, string> errorMap, bool isTestFile) {
        _errorMap = errorMap;
        _isTestFile = isTestFile;
    }

    private bool IsErrorLine(int line) => _errorMap.ContainsKey(line);
    private bool IsErrorLine(int line, string code) => _errorMap.TryGetValue(line, out var c) && c == code;

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) {
        var hasErrorInMethod = node.DescendantNodes()
            .Any(n => IsErrorLine(n.GetLocation().GetLineSpan().StartLinePosition.Line + 1));

        var fixedBefore = FixedCount;

        var visited = (MethodDeclarationSyntax?)base.VisitMethodDeclaration(node);

        if (visited is null) return null;
        if (!hasErrorInMethod) return visited;

        // 本方法有修复且不是 async → 改 async
        // 但 public/internal 方法可能实现接口，改返回类型会导致 CS0738 → 不改
        var fixedInThisMethod = FixedCount - fixedBefore;
        if (fixedInThisMethod > 0 && !node.Modifiers.Any(SyntaxKind.AsyncKeyword)) {
            var isPublic = node.Modifiers.Any(SyntaxKind.PublicKeyword)
                || node.Modifiers.Any(SyntaxKind.InternalKeyword);
            if (!isPublic) {
                var newMethod = SyntaxHelpers.AddAsyncModifier(visited);
                var newReturnType = SyntaxHelpers.TransformReturnType(visited.ReturnType);
                return newMethod.WithReturnType(newReturnType);
            }
        }

        return visited;
    }

    /// <summary>
    /// 判断节点是否在不能 async 的上下文中（构造函数/属性 getter/public 方法）
    /// 这些上下文中用 .GetAwaiter().GetResult() 代替 await
    /// </summary>
    private static bool IsInNoAsyncContext(SyntaxNode node) {
        // 构造函数不能 async
        if (node.Ancestors().OfType<ConstructorDeclarationSyntax>().Any()) return true;
        // 属性 getter 不能 async
        if (node.Ancestors().OfType<AccessorDeclarationSyntax>().Any()) return true;
        // public/internal 方法可能实现接口，改返回类型会导致 CS0738
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is not null) {
            if (method.Modifiers.Any(SyntaxKind.PublicKeyword)
                || method.Modifiers.Any(SyntaxKind.InternalKeyword))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 处理 CS4014：裸语句调用加 await
    /// 不能 async 的上下文用 .GetAwaiter().GetResult() 代替 await
    /// </summary>
    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node) {
        var visited = (ExpressionStatementSyntax?)base.VisitExpressionStatement(node);
        if (visited is null) return null;

        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!IsErrorLine(line)) return visited;
        if (node.Expression is AwaitExpressionSyntax) return visited;
        if (node.Expression is not InvocationExpressionSyntax) return visited;

        ExpressionSyntax newExpr;
        if (IsInNoAsyncContext(node)) {
            newExpr = SyntaxHelpers.CreateGetAwaiterGetResult(node.Expression, node.Expression);
        } else {
            newExpr = SyntaxHelpers.CreateAwaitExpression(node.Expression, node.Expression, _isTestFile);
        }
        FixedCount++;
        return visited.WithExpression(newExpr);
    }

    /// <summary>
    /// 处理 CS0029/CS1503：变量声明加 await
    /// 不能 async 的上下文用 .GetAwaiter().GetResult() 代替 await
    /// </summary>
    public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node) {
        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!IsErrorLine(line))
            return base.VisitVariableDeclarator(node);

        var initializer = node.Initializer;
        if (initializer is null) return base.VisitVariableDeclarator(node);
        var value = initializer.Value;
        if (value.Parent is AwaitExpressionSyntax) return base.VisitVariableDeclarator(node);
        if (value is not InvocationExpressionSyntax) return base.VisitVariableDeclarator(node);

        var varName = node.Identifier.ValueText;
        if (varName.StartsWith("task", StringComparison.OrdinalIgnoreCase))
            return base.VisitVariableDeclarator(node);

        ExpressionSyntax newExpr;
        if (IsInNoAsyncContext(node)) {
            newExpr = SyntaxHelpers.CreateGetAwaiterGetResult(value, value);
        } else {
            newExpr = SyntaxHelpers.CreateAwaitExpression(value, value, _isTestFile);
        }
        FixedCount++;
        return node.WithInitializer(initializer.WithValue(newExpr));
    }

    /// <summary>
    /// 处理 CS1503 方法组转换：new Thread(ScanLoop) → new Thread(() => ScanLoop().GetAwaiter().GetResult())
    /// 当参数是 IdentifierNameSyntax（方法名引用）且在错误行，包装为 lambda。
    /// </summary>
    /// <summary>
    /// 处理 CS1503 方法组转换：new Thread(ScanLoop) → new Thread(() => ScanLoop().GetAwaiter().GetResult())
    /// 只在 CS1503 错误行处理（其他错误码的 IdentifierNameSyntax 是变量引用不是方法组）
    /// </summary>
    public override SyntaxNode? VisitArgument(ArgumentSyntax node) {
        var visited = (ArgumentSyntax?)base.VisitArgument(node);
        if (visited is null) return null;

        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!IsErrorLine(line, "CS1503")) return visited;
        if (node.Expression is not IdentifierNameSyntax identifier) return visited;

        // 包装为 lambda: () => Identifier().GetAwaiter().GetResult()
        var invocation = SyntaxFactory.InvocationExpression(identifier);
        var getAwaiterGetResult = SyntaxHelpers.CreateGetAwaiterGetResult(invocation, node);
        var lambda = SyntaxFactory.ParenthesizedLambdaExpression(
            SyntaxFactory.ParameterList(),
            getAwaiterGetResult);

        FixedCount++;
        return visited.WithExpression(lambda);
    }

    /// <summary>
    /// 处理 CS0029 赋值表达式：x = SomeAsync() → x = await SomeAsync() 或 x = SomeAsync().GetAwaiter().GetResult()
    /// </summary>
    public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node) {
        var visited = (AssignmentExpressionSyntax?)base.VisitAssignmentExpression(node);
        if (visited is null) return null;

        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!IsErrorLine(line, "CS0029")) return visited;
        if (node.Right is AwaitExpressionSyntax) return visited;
        if (node.Right is not InvocationExpressionSyntax invocation) return visited;

        ExpressionSyntax newExpr;
        if (IsInNoAsyncContext(node)) {
            newExpr = SyntaxHelpers.CreateGetAwaiterGetResult(invocation, invocation);
        } else {
            newExpr = SyntaxHelpers.CreateAwaitExpression(invocation, invocation, _isTestFile);
        }
        FixedCount++;
        return visited.WithRight(newExpr);
    }
}
