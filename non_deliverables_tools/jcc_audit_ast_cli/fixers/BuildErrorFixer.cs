namespace JccAuditCli;

/// <summary>
/// 编译错误驱动的修复器 — 运行 dotnet build，解析 CS4014/CS0029/CS1503/CS1061 错误位置，精确加 await
/// </summary>
public static class BuildErrorFixer {

    /// <summary>
    /// 本修复器能处理的编译错误码集合（唯一数据源，regex 与展示文本均委托此集合）。
    /// </summary>
    private static readonly string[] HandledErrorCodes = ["CS4014", "CS0029", "CS1503", "CS1061", "CS0019"];

    /// <summary>
    /// 运行 dotnet build 获取错误，在错误位置加 await
    /// </summary>
    public static async Task<(int FixedFiles, int FixedIssues)> FixFromBuildErrorsAsync(
        string solutionPath, bool dryRun, CancellationToken ct = default) {

        var errors = await CollectBuildErrorsAsync(solutionPath, ct);
        if (errors.Count == 0) {
            Console.WriteLine($"没有 {string.Join("/", HandledErrorCodes)} 错误。");
            return (0, 0);
        }

        Console.WriteLine($"发现 {errors.Count} 个 {string.Join("/", HandledErrorCodes)} 错误。");

        var byFile = errors.GroupBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase);
        var fixedFiles = 0;
        var fixedIssues = 0;

        foreach (var group in byFile) {
            ct.ThrowIfCancellationRequested();
            var errorMatcher = new ErrorLineMatcher(group.Select(e => (e.Line, e.ErrorCode)));
            var isTestFile = FileFilter.IsTestFile(group.Key);

            var source = await File.ReadAllTextAsync(group.Key, ct);
            var tree = CSharpSyntaxTree.ParseText(source, path: group.Key);
            var root = await tree.GetRootAsync(ct);

            var rewriter = new UnawaitedVariableRewriter(errorMatcher, isTestFile);
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
            FileName = "dotnet",
            Arguments = $"build \"{slnFullPath}\" --no-restore",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var output = await stdoutTask + Environment.NewLine + await stderrTask;

        var errors = new List<BuildError>();
        var regex = new System.Text.RegularExpressions.Regex(
            @"^(.+?)\((\d+),(\d+)\):\s*error\s+(" + string.Join("|", HandledErrorCodes) + @")",
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
    private readonly ErrorLineMatcher _errorMatcher;
    private readonly bool _isTestFile;

    /// <summary>
    /// 已修复的数量
    /// </summary>
    public int FixedCount { get; private set; }

    internal UnawaitedVariableRewriter(ErrorLineMatcher errorMatcher, bool isTestFile) {
        _errorMatcher = errorMatcher;
        _isTestFile = isTestFile;
    }

    private bool IsErrorLine(int line) => _errorMatcher.Contains(line);
    private bool IsErrorLine(int line, string code) => _errorMatcher.Contains(line, code);

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
            // 测试方法虽然 public，但可以改 async（xUnit 支持 async Task 测试方法）
            // 避免 xUnit1031：测试方法中禁止 .GetAwaiter().GetResult()
            if (IsTestMethod(method)) return false;
            if (method.Modifiers.Any(SyntaxKind.PublicKeyword)
                || method.Modifiers.Any(SyntaxKind.InternalKeyword))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 判断方法是否为 xUnit 测试方法（[Fact]/[Theory]）
    /// </summary>
    private static bool IsTestMethod(MethodDeclarationSyntax method) {
        return method.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a => a.Name.ToString() is "Fact" or "Theory");
    }

    /// <summary>
    /// 处理 CS4014：裸语句调用加 await
    /// 不能 async 的上下文用 .GetAwaiter().GetResult() 代替 await
    /// </summary>
    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node) {
        var visited = (ExpressionStatementSyntax?)base.VisitExpressionStatement(node);
        if (visited is null) return null;

        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!IsErrorLine(line, "CS4014")) return visited;
        if (node.Expression is AwaitExpressionSyntax) return visited;
        if (node.Expression is not InvocationExpressionSyntax) return visited;
        // 跳过 lambda 内的裸语句（由 VisitSimpleLambdaExpressionGpression 处理）
        if (node.Ancestors().OfType<SimpleLambdaExpressionSyntax>().Any()
            || node.Ancestors().OfType<ParenthesizedLambdaExpressionSyntax>().Any())
            return visited;

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
    /// 处理 CS0029：变量声明加 await
    /// 不能 async 的上下文用 .GetAwaiter().GetResult() 代替 await
    /// </summary>
    public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node) {
        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!IsErrorLine(line, "CS0029"))
            return base.VisitVariableDeclarator(node);

        // 跳过数组类型的变量（如 var paths = new[] { validPath, invalidPath }）
        if (node.Parent is VariableDeclarationSyntax decl && decl.Type.IsKind(SyntaxKind.ArrayType))
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
    /// 处理 CS4034：lambda 内有 await 但 lambda 未标记 async → 给 lambda 加 async 修饰符
    /// </summary>
    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) {
        var visited = (SimpleLambdaExpressionSyntax?)base.VisitSimpleLambdaExpression(node);
        if (visited is null) return null;
        return TryAddAsyncToLambda(node, visited);
    }

    /// <summary>
    /// 处理 CS4034：parenthesized lambda 内有 await 但 lambda 未标记 async → 给 lambda 加 async 修饰符
    /// </summary>
    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) {
        var visited = (ParenthesizedLambdaExpressionSyntax?)base.VisitParenthesizedLambdaExpression(node);
        if (visited is null) return null;
        return TryAddAsyncToLambda(node, visited);
    }

    private SyntaxNode TryAddAsyncToLambda(SyntaxNode originalLambda, SyntaxNode visitedLambda) {
        if (originalLambda.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Any(inv => IsErrorLine(inv.GetLocation().GetLineSpan().StartLinePosition.Line + 1, "CS4014"))) {
            var asyncKeyword = SyntaxFactory.Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(SyntaxFactory.Space);
            FixedCount++;
            return visitedLambda switch {
                SimpleLambdaExpressionSyntax sl => sl.WithAsyncKeyword(asyncKeyword),
                ParenthesizedLambdaExpressionSyntax pl => pl.WithAsyncKeyword(asyncKeyword),
                _ => visitedLambda
            };
        }
        return visitedLambda;
    }

    /// <summary>
    /// 处理 CS0029：switch 表达式 arm 中的方法调用返回 Task<T> 但需要 T
    /// 在不能 async 的上下文用 .GetAwaiter().GetResult() 代替 await
    /// </summary>
    public override SyntaxNode? VisitSwitchExpressionArm(SwitchExpressionArmSyntax node) {
        var visited = (SwitchExpressionArmSyntax?)base.VisitSwitchExpressionArm(node);
        if (visited is null) return null;

        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!IsErrorLine(line, "CS0029")) return visited;
        if (node.Expression is AwaitExpressionSyntax) return visited;
        if (node.Expression is not InvocationExpressionSyntax invocation) return visited;

        var newExpr = SyntaxHelpers.CreateGetAwaiterGetResult(invocation, invocation);
        FixedCount++;
        return visited.WithExpression(newExpr);
    }

    /// <summary>
    /// 处理 CS0019：?? 运算符右操作数返回 Task<T> 但需要 T
    /// pathResult ?? pathResult2 → pathResult ?? pathResult2?.GetAwaiter().GetResult()
    /// </summary>
    public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node) {
        var visited = (BinaryExpressionSyntax?)base.VisitBinaryExpression(node);
        if (visited is null) return null;

        if (!node.IsKind(SyntaxKind.CoalesceExpression)) return visited;

        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!IsErrorLine(line, "CS0019")) return visited;

        var target = node.Right;
        if (target is not (InvocationExpressionSyntax or IdentifierNameSyntax)) return visited;

        ExpressionSyntax newRight;
        if (target is IdentifierNameSyntax idName) {
            var name = idName.Identifier.ValueText;
            newRight = SyntaxFactory.ParseExpression($"{name}?.GetAwaiter().GetResult()")
                .WithLeadingTrivia(target.GetLeadingTrivia())
                .WithTrailingTrivia(target.GetTrailingTrivia());
        } else {
            newRight = SyntaxHelpers.CreateGetAwaiterGetResult(target, target);
        }
        FixedCount++;
        return visited.WithRight(newRight);
    }

    /// <summary>
    /// 处理 CS1503 方法组转换：new Thread(ScanLoop) → new Thread(() => ScanLoop().GetAwaiter().GetResult())
    /// 当参数是 IdentifierNameSyntax（方法名引用）且在错误行，包装为 lambda。
    /// </summary>
    /// <summary>
    /// 处理 CS1503 方法组转换和变量类型不匹配：
    /// - ObjectCreationExpression 参数（如 new Thread(ScanLoop)）→ 方法组，包装为 lambda
    /// - InvocationExpression 参数（如 ShouldCache(result)）→ 变量，加 .GetAwaiter().GetResult()
    /// </summary>
    public override SyntaxNode? VisitArgument(ArgumentSyntax node) {
        var visited = (ArgumentSyntax?)base.VisitArgument(node);
        if (visited is null) return null;

        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!IsErrorLine(line, "CS1503")) return visited;
        if (node.Expression is not IdentifierNameSyntax identifier) return visited;

        var isObjectCreation = node.Parent?.Parent is ObjectCreationExpressionSyntax;

        if (isObjectCreation) {
            // 方法组转换: new Thread(ScanLoop) → new Thread(() => ScanLoop().GetAwaiter().GetResult())
            var invocation = SyntaxFactory.InvocationExpression(identifier);
            var getAwaiterGetResult = SyntaxHelpers.CreateGetAwaiterGetResult(invocation, node);
            var lambda = SyntaxFactory.ParenthesizedLambdaExpression(
                SyntaxFactory.ParameterList(),
                getAwaiterGetResult);
            FixedCount++;
            return visited.WithExpression(lambda);
        } else {
            // 变量类型不匹配: ShouldCache(result) → ShouldCache(result.GetAwaiter().GetResult())
            var memberAccess = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                identifier,
                SyntaxFactory.Token(SyntaxKind.DotToken),
                SyntaxFactory.IdentifierName("GetAwaiter"));
            var getAwaiterCall = SyntaxFactory.InvocationExpression(memberAccess, SyntaxFactory.ArgumentList());
            var getResultAccess = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                getAwaiterCall,
                SyntaxFactory.Token(SyntaxKind.DotToken),
                SyntaxFactory.IdentifierName("GetResult"));
            var getResultCall = SyntaxFactory.InvocationExpression(getResultAccess, SyntaxFactory.ArgumentList());
            var newExpr = getResultCall
                .WithLeadingTrivia(node.GetLeadingTrivia())
                .WithTrailingTrivia(node.GetTrailingTrivia());
            FixedCount++;
            return visited.WithExpression(newExpr);
        }
    }

    /// <summary>
    /// 处理 CS0029 赋值表达式：x = SomeAsync() 或 x = result → 加 await/.GetAwaiter().GetResult()
    /// 支持右侧是 InvocationExpression（方法调用）或 IdentifierName（变量引用）
    /// </summary>
    public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node) {
        var visited = (AssignmentExpressionSyntax?)base.VisitAssignmentExpression(node);
        if (visited is null) return null;

        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!IsErrorLine(line, "CS0029")) return visited;
        if (node.Right is AwaitExpressionSyntax) return visited;

        // 右侧是变量引用（如 ParseCache[command] = result）→ result.GetAwaiter().GetResult()
        if (node.Right is IdentifierNameSyntax varId) {
            var newExpr = CreateGetAwaiterGetResultFromIdentifier(varId);
            FixedCount++;
            return visited.WithRight(newExpr);
        }

        // 右侧是方法调用（如 x = SomeAsync()）→ await SomeAsync() 或 SomeAsync().GetAwaiter().GetResult()
        if (node.Right is not InvocationExpressionSyntax invocation) return visited;

        ExpressionSyntax awaitExpr;
        if (IsInNoAsyncContext(node)) {
            awaitExpr = SyntaxHelpers.CreateGetAwaiterGetResult(invocation, invocation);
        } else {
            awaitExpr = SyntaxHelpers.CreateAwaitExpression(invocation, invocation, _isTestFile);
        }
        FixedCount++;
        return visited.WithRight(awaitExpr);
    }

    /// <summary>
    /// 处理 CS0029 return 语句：return result → return result.GetAwaiter().GetResult()
    /// </summary>
    public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node) {
        var visited = (ReturnStatementSyntax?)base.VisitReturnStatement(node);
        if (visited is null) return null;

        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!IsErrorLine(line, "CS0029")) return visited;
        if (node.Expression is not IdentifierNameSyntax varId) return visited;

        var newExpr = CreateGetAwaiterGetResultFromIdentifier(varId);
        FixedCount++;
        return visited.WithExpression(newExpr);
    }

    /// <summary>
    /// 从标识符创建 expr.GetAwaiter().GetResult() 表达式
    /// </summary>
    private static ExpressionSyntax CreateGetAwaiterGetResultFromIdentifier(IdentifierNameSyntax identifier) {
        var memberAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            identifier,
            SyntaxFactory.Token(SyntaxKind.DotToken),
            SyntaxFactory.IdentifierName("GetAwaiter"));
        var getAwaiterCall = SyntaxFactory.InvocationExpression(memberAccess, SyntaxFactory.ArgumentList());
        var getResultAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            getAwaiterCall,
            SyntaxFactory.Token(SyntaxKind.DotToken),
            SyntaxFactory.IdentifierName("GetResult"));
        var getResultCall = SyntaxFactory.InvocationExpression(getResultAccess, SyntaxFactory.ArgumentList());
        return getResultCall
            .WithLeadingTrivia(identifier.GetLeadingTrivia())
            .WithTrailingTrivia(identifier.GetTrailingTrivia());
    }
}
