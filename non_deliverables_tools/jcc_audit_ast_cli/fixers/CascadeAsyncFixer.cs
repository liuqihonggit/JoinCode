namespace JccAuditCli;

/// <summary>
/// 递归级联修复器 — 编译错误驱动，状态机组织修复流程。
/// fix-getawaiter-getresult 改 private 方法为 async 后，调用方报 CS4014/CS0029/CS1503。
/// 本修复器循环：dotnet build → 解析错误 → 加 await/改 async/回退 → 重新编译，直到无错误。
///
/// 状态机流转：
///   Initial → Building → ParsingErrors →
///     (无错误) → Done
///     (有错误) → Fixing → Verifying →
///       (修复了) → Building (继续循环)
///       (未修复) → Failed (报告剩余错误)
///
/// 修复策略（按错误类型决策）：
///   CS4014 (未await的Task)     → AddAwait（加 await）
///   CS0029 (类型不匹配)        → AddAsync（改 async + 加 await）
///   CS1503 (类型转换失败)      → AddAsync（改 async + 加 await）
///   CS1929 (方法不存在)        → RevertToSync（回退为 .GetAwaiter().GetResult()）
///   CS0019 (运算符不适用)      → RevertToSync（回退为 .GetAwaiter().GetResult()）
///
/// 跳过规则：
///   1. using 声明中的调用（using var x = Create() → 不加 await，避免 CS1929）
///   2. Lazy&lt;T&gt; 约束的方法（new Lazy&lt;T&gt;(Method) → 不改 async，避免 CS0019）
///   3. 构造函数（不能加 async，只修复方法组参数）
/// </summary>
public static class CascadeAsyncFixer {

    /// <summary>
    /// 递归修复级联错误。返回 (迭代次数, 总修复数)。
    /// 状态机流转用命令式 switch 语句组织，确保 ref 参数正确传递。
    /// </summary>
    public static async Task<(int iterations, int totalFixed)> FixCascadeAsync(
        string slnPath, int maxIterations, bool dryRun, CancellationToken ct) {

        var state = CascadeFixState.Initial;
        var iterations = 0;
        var totalFixed = 0;
        var errors = new List<CascadeError>();
        var buildOutput = string.Empty;

        while (state is not CascadeFixState.Done and not CascadeFixState.Failed) {
            switch (state) {
                case CascadeFixState.Initial:
                    if (iterations >= maxIterations) {
                        Console.WriteLine("  达到最大迭代次数，停止");
                        state = CascadeFixState.Done;
                    } else {
                        iterations++;
                        Console.WriteLine($"  === 迭代 {iterations} ===");
                        state = CascadeFixState.Building;
                    }
                    break;

                case CascadeFixState.Building:
                    buildOutput = await RunBuildAsync(slnPath, ct).ConfigureAwait(false);
                    state = CascadeFixState.ParsingErrors;
                    break;

                case CascadeFixState.ParsingErrors:
                    errors = ParseCascadeErrors(buildOutput);
                    if (errors.Count == 0) {
                        Console.WriteLine("  无级联错误，完成！");
                        state = CascadeFixState.Done;
                    } else {
                        Console.WriteLine($"  发现 {errors.Count} 个级联错误");
                        state = CascadeFixState.Fixing;
                    }
                    break;

                case CascadeFixState.Fixing:
                    var fixedThisRound = await FixErrorsAsync(errors, dryRun, ct).ConfigureAwait(false);
                    totalFixed += fixedThisRound;
                    if (fixedThisRound == 0) {
                        Console.WriteLine("  本轮未修复任何错误，停止以避免死循环");
                        state = CascadeFixState.Failed;
                    } else {
                        state = CascadeFixState.Verifying;
                    }
                    break;

                case CascadeFixState.Verifying:
                    // 重新编译验证
                    buildOutput = await RunBuildAsync(slnPath, ct).ConfigureAwait(false);
                    state = CascadeFixState.ParsingErrors;
                    break;
            }
        }

        if (state == CascadeFixState.Failed && errors.Count > 0) {
            Console.WriteLine("  剩余错误需要手动处理：");
            foreach (var e in errors)
                Console.WriteLine($"    {Path.GetFileName(e.File)}({e.Line},{e.Col}): {e.Code} {e.Message}");
        }

        return (iterations, totalFixed);
    }

    private static async Task<string> RunBuildAsync(string slnPath, CancellationToken ct) {
        var psi = new System.Diagnostics.ProcessStartInfo {
            FileName = "dotnet",
            Arguments = $"build \"{slnPath}\" --no-restore",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process is null) return string.Empty;

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return await stdoutTask.ConfigureAwait(false) + "\n" + await stderrTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 解析 dotnet build 输出中的级联错误。
    /// 支持的错误码：CS4014(未await)、CS0029(类型不匹配)、CS1503(类型转换)、CS1929(方法不存在)、CS0019(运算符不适用)。
    /// </summary>
    private static List<CascadeError> ParseCascadeErrors(string buildOutput) {
        var regex = new Regex(
            @"^(.+?)\((\d+),(\d+)\):\s*error\s+(CS4014|CS0029|CS1503|CS1929|CS0019):\s*(.+?)(?:\s*\[)",
            RegexOptions.Multiline);

        var errors = new List<CascadeError>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in regex.Matches(buildOutput)) {
            var file = match.Groups[1].Value.Trim();
            var line = int.Parse(match.Groups[2].Value);
            var col = int.Parse(match.Groups[3].Value);
            var code = match.Groups[4].Value;
            var msg = match.Groups[5].Value.Trim();

            if (FileFilter.ShouldSkipFile(file)) continue;

            var key = $"{file}:{line}:{col}";
            if (seen.Add(key)) {
                errors.Add(new CascadeError(file, line, col, code, msg));
            }
        }

        return errors;
    }

    private static async Task<int> FixErrorsAsync(
        List<CascadeError> errors, bool dryRun, CancellationToken ct) {

        var byFile = errors.GroupBy(e => e.File);
        var totalFixed = 0;

        foreach (var group in byFile) {
            var filePath = group.Key;
            var fileErrors = group.ToList();

            var fixedCount = await FixFileAsync(filePath, fileErrors, dryRun, ct).ConfigureAwait(false);
            totalFixed += fixedCount;
        }

        return totalFixed;
    }

    private static async Task<int> FixFileAsync(
        string filePath, List<CascadeError> errors, bool dryRun, CancellationToken ct) {

        var sourceText = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var text = SourceText.From(sourceText);
        var tree = CSharpSyntaxTree.ParseText(text, path: filePath);
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

        var isTestFile = FileFilter.IsTestFile(filePath);

        // 先检测 Lazy<T> 约束的方法名集合
        var lazyConstrainedMethods = DetectLazyConstrainedMethods(root);

        var rewriter = new CascadeRewriter(errors, isTestFile, lazyConstrainedMethods);
        var newRoot = rewriter.Visit(root);

        if (newRoot != root && rewriter.FixedCount > 0) {
            if (!dryRun) {
                var newText = newRoot.ToFullString();
                await File.WriteAllTextAsync(filePath, newText, ct).ConfigureAwait(false);
            }

            Console.WriteLine($"  {Path.GetFileName(filePath)}: {rewriter.FixedCount} 处修复");
            return rewriter.FixedCount;
        }

        return 0;
    }

    /// <summary>
    /// 检测类中被 Lazy&lt;T&gt; 约束的方法名集合。
    /// 模式：new Lazy&lt;...&gt;(MethodName) 或 new Lazy&lt;...&gt;(MethodName, ...)
    /// 这些方法不能改 async（Lazy&lt;T&gt; 需要 Func&lt;T&gt;，不能返回 Task&lt;T&gt;）。
    /// </summary>
    private static HashSet<string> DetectLazyConstrainedMethods(SyntaxNode root) {
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var invocation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()) {
            var typeText = invocation.Type.ToString();
            if (!typeText.StartsWith("Lazy<", StringComparison.Ordinal) &&
                !typeText.StartsWith("System.Lazy<", StringComparison.Ordinal))
                continue;

            var args = invocation.ArgumentList?.Arguments;
            if (args is null || args.Value.Count == 0) continue;

            // 第一个参数是方法名引用
            var firstArg = args.Value[0].Expression;
            if (firstArg is IdentifierNameSyntax id) {
                result.Add(id.Identifier.ValueText);
            }
        }

        return result;
    }

    internal sealed record CascadeError(string File, int Line, int Col, string Code, string Message);
}

/// <summary>
/// 级联修复状态机 — 显式状态枚举。
/// </summary>
internal enum CascadeFixState {
    /// <summary>初始状态</summary>
    Initial,
    /// <summary>运行 dotnet build</summary>
    Building,
    /// <summary>解析编译错误</summary>
    ParsingErrors,
    /// <summary>修复错误</summary>
    Fixing,
    /// <summary>验证修复结果（重新编译）</summary>
    Verifying,
    /// <summary>完成（无错误）</summary>
    Done,
    /// <summary>失败（有未修复错误，需手动处理）</summary>
    Failed,
}

/// <summary>
/// 级联重写器 — 按方法/构造函数范围修复，状态机决策修复策略。
/// 跳过规则：
///   1. using 声明中的调用不加 await（避免 CS1929）
///   2. Lazy&lt;T&gt; 约束的方法不改 async（避免 CS0019）
///   3. 构造函数只修复方法组参数
/// 回退策略：
///   CS1929/CS0019 → 撤销 await/async，改用 .GetAwaiter().GetResult()
/// </summary>
internal class CascadeRewriter : CSharpSyntaxRewriter {
    private readonly bool _isTestFile;
    private readonly ErrorLineMatcher _errorMatcher;
    private readonly HashSet<string> _lazyConstrainedMethods;
    private readonly bool _hasMethodGroupError;
    private readonly HashSet<int> _revertLines;
    private readonly Stack<bool> _contextStack = new();
    private readonly Stack<bool> _lazyStack = new();
    private bool _inTargetFunction;
    private bool _inLazyConstrainedMethod;

    /// <summary>已修复数量</summary>
    public int FixedCount { get; private set; }

    internal CascadeRewriter(
        List<CascadeAsyncFixer.CascadeError> errors,
        bool isTestFile,
        HashSet<string> lazyConstrainedMethods) {
        _isTestFile = isTestFile;
        _errorMatcher = new ErrorLineMatcher(errors.Select(e => (e.Line, e.Code)));
        _lazyConstrainedMethods = lazyConstrainedMethods;
        _hasMethodGroupError = errors.Any(e => e.Message.Contains("方法组"));
        // CS1929/CS0019 错误行 → 需要回退为 .GetAwaiter().GetResult()
        _revertLines = new HashSet<int>(
            errors.Where(e => e.Code is "CS1929" or "CS0019").Select(e => e.Line));
    }

    private bool IsTargetRange(SyntaxNode node) {
        var start = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var end = node.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
        return _errorMatcher.IntersectsRange(start, end);
    }

    /// <summary>
    /// 判断调用是否在 using 声明中（using var x = Create()）。
    /// using 声明中的调用不加 await，因为结果类型是 IDisposable 不是 Task。
    /// </summary>
    private static bool IsInUsingDeclaration(SyntaxNode node) {
        return node.Ancestors().OfType<LocalDeclarationStatementSyntax>()
            .Any(stmt => stmt.UsingKeyword.IsKind(SyntaxKind.UsingKeyword));
    }

    /// <summary>
    /// 判断方法是否被 Lazy&lt;T&gt; 约束（不能改 async）。
    /// </summary>
    private bool IsLazyConstrained(MethodDeclarationSyntax node) {
        return _lazyConstrainedMethods.Contains(node.Identifier.ValueText);
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) {
        var isTarget = IsTargetRange(node);
        var isLazy = IsLazyConstrained(node);

        _contextStack.Push(_inTargetFunction);
        _lazyStack.Push(_inLazyConstrainedMethod);
        _inTargetFunction = isTarget;
        _inLazyConstrainedMethod = isLazy;

        var visited = (MethodDeclarationSyntax?)base.VisitMethodDeclaration(node);

        _inTargetFunction = _contextStack.Pop();
        _inLazyConstrainedMethod = _lazyStack.Pop();

        if (visited is null) return null;
        if (!isTarget) return visited;

        // Lazy<T> 约束的方法不能改 async → 跳过（但调用已改为 .GetAwaiter().GetResult()）
        if (isLazy) {
            Console.WriteLine($"    跳过 Lazy<T> 约束方法改 async: {node.Identifier.ValueText}");
            return visited;
        }

        // 方法不是 async 且有非 await 调用 → 改 async
        if (!node.Modifiers.Any(SyntaxKind.AsyncKeyword) && HasNonAwaitedInvocations(node)) {
            var newMethod = SyntaxHelpers.AddAsyncModifier(visited);
            var newReturnType = SyntaxHelpers.TransformReturnType(visited.ReturnType);
            return newMethod.WithReturnType(newReturnType);
        }

        return visited;
    }

    public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node) {
        // 构造函数不能加 async，但可以修复方法组参数（如 new Thread(ScanLoop) → lambda）
        var isTarget = IsTargetRange(node);

        _contextStack.Push(_inTargetFunction);
        _inTargetFunction = isTarget;

        var visited = (ConstructorDeclarationSyntax?)base.VisitConstructorDeclaration(node);

        _inTargetFunction = _contextStack.Pop();
        return visited;
    }

    private static bool HasNonAwaitedInvocations(SyntaxNode node) {
        return node.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.Parent is not AwaitExpressionSyntax
                && !IsInUsingDeclaration(inv));
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node) {
        var visited = base.VisitInvocationExpression(node);

        if (!_inTargetFunction) return visited;
        if (node.Parent is AwaitExpressionSyntax) return visited;
        if (node.Parent is AssignmentExpressionSyntax assign && assign.Left == node)
            return visited;

        // 跳过 using 声明中的调用（避免给 IDisposable 加 await → CS1929）
        if (IsInUsingDeclaration(node)) return visited;

        // Lazy<T> 约束的方法中 → 用 .GetAwaiter().GetResult() 而不是 await（方法不能改 async）
        if (_inLazyConstrainedMethod) {
            if (visited is ExpressionSyntax expr) {
                var syncExpr = SyntaxHelpers.CreateGetAwaiterGetResult(expr, node);
                FixedCount++;
                return syncExpr;
            }
            return visited;
        }

        // CS1929/CS0019 回退行 → 改用 .GetAwaiter().GetResult()
        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (_revertLines.Contains(line)) {
            if (visited is ExpressionSyntax expr) {
                var syncExpr = SyntaxHelpers.CreateGetAwaiterGetResult(expr, node);
                FixedCount++;
                return syncExpr;
            }
            return visited;
        }

        if (visited is ExpressionSyntax awaitExpr) {
            var result = SyntaxHelpers.CreateAwaitExpression(awaitExpr, node, _isTestFile);
            FixedCount++;
            return result;
        }

        return visited;
    }

    /// <summary>
    /// 处理方法组参数（如 new Thread(ScanLoop) → new Thread(() => ScanLoop().GetAwaiter().GetResult())）。
    /// </summary>
    public override SyntaxNode? VisitArgument(ArgumentSyntax node) {
        var visited = (ArgumentSyntax?)base.VisitArgument(node);
        if (visited is null) return null;

        if (!_inTargetFunction || !_hasMethodGroupError) return visited;
        if (node.Expression is not IdentifierNameSyntax identifier) return visited;

        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!_errorMatcher.Contains(line)) return visited;

        // 包装为 lambda: () => Identifier().GetAwaiter().GetResult()
        var invocation = SyntaxFactory.InvocationExpression(identifier);
        var getAwaiterGetResult = SyntaxHelpers.CreateGetAwaiterGetResult(invocation, node);
        var lambda = SyntaxFactory.ParenthesizedLambdaExpression(
            SyntaxFactory.ParameterList(),
            getAwaiterGetResult);

        FixedCount++;
        return visited.WithExpression(lambda);
    }
}
