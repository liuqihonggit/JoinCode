namespace JccAuditCli;

/// <summary>
/// 分析器驱动修复器 — dotnet build 输出驱动方案。
/// 检测: 运行 dotnet build，分析器在编译器管线中调用 SyncMethodAsyncCallDetector 检测违规。
/// 记录: 解析 JCC3016 错误输出，提取 (file, line, col) 违规位置。
/// 替换: 按位置定位 InvocationExpression，替换为 invocation.GetAwaiter().GetResult()。
/// 幂等: 已修复的不会产生 JCC3016 错误，自动跳过。
/// 通用: 任何新工程 dotnet build 都会触发分析器。
/// </summary>
public static class AnalyzerDrivenFixer {

    /// <summary>
    /// 修复解决方案中所有 JCC3016 违规。
    /// 检测: dotnet build 输出中的 JCC3016 error（分析器调用 SyncMethodAsyncCallDetector）。
    /// 修复: invocation → invocation.GetAwaiter().GetResult()。
    /// </summary>
    /// <param name="solutionPath">解决方案路径（.slnx / .sln / .csproj）。</param>
    /// <param name="dryRun">仅预览，不实际写入。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>（修复文件数, 修复问题数, 跳过数）。</returns>
    public static async Task<(int fixedFiles, int fixedIssues, int skipped)> FixAsync(
        string solutionPath, bool dryRun, CancellationToken ct) {

        Console.WriteLine($"  规则: JCC3016（dotnet build 输出驱动）");
        Console.WriteLine($"  模式: {(dryRun ? "预览 (DryRun)" : "实际写入")}");

        // ── 阶段1: dotnet build 捕获 JCC3016 错误 ──
        Console.WriteLine("  [1/3] 运行 dotnet build 检测违规...");
        var buildOutput = await RunDotnetBuildAsync(solutionPath, ct).ConfigureAwait(false);

        // ── 阶段2: 解析错误位置（记录有问题的节点） ──
        Console.WriteLine("  [2/3] 解析 JCC3016 错误位置...");
        var violations = ParseJcc3016Errors(buildOutput);

        if (violations.Count == 0) {
            Console.WriteLine("  未发现违规，无需修复。");
            return (0, 0, 0);
        }

        // 按文件分组
        var violationsByFile = violations
            .GroupBy(v => v.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList());

        Console.WriteLine($"  发现 {violations.Count} 处违规，分布在 {violationsByFile.Count} 个文件:");

        // 输出违规列表（记录有问题的节点）
        foreach (var (filePath, fileViolations) in violationsByFile) {
            var lines = string.Join(", ", fileViolations.OrderBy(v => v.Line).Select(v => v.Line));
            Console.WriteLine($"    {Path.GetFileName(filePath)}: 行 {lines}");
        }

        // ── 阶段3: 按文件应用修复（替换操作） ──
        Console.WriteLine($"  [3/3] {(dryRun ? "预览修复" : "应用修复")}...");
        int fixedFiles = 0, fixedIssues = 0, skipped = 0;

        foreach (var (filePath, fileViolations) in violationsByFile) {
            if (ct.IsCancellationRequested) break;

            var (wasFixed, issueCount, skipCount) = await FixFileAsync(filePath, fileViolations, dryRun, ct).ConfigureAwait(false);
            fixedFiles += wasFixed;
            fixedIssues += issueCount;
            skipped += skipCount;
        }

        return (fixedFiles, fixedIssues, skipped);
    }

    /// <summary>
    /// 运行 dotnet build 捕获输出。
    /// --no-restore 加速（假设已 restore）；若失败则不带 --no-restore 重试。
    /// </summary>
    private static async Task<string> RunDotnetBuildAsync(string targetPath, CancellationToken ct) {
        var fullPath = Path.GetFullPath(targetPath);

        // 第一次尝试: --no-restore（快）
        var output = await TryRunBuildAsync(fullPath, "--no-restore", ct).ConfigureAwait(false);
        if (output is not null) return output;

        // 第二次尝试: 不带 --no-restore（会自动 restore）
        Console.WriteLine("  (--no-restore 失败，重试带 restore...)");
        output = await TryRunBuildAsync(fullPath, "", ct).ConfigureAwait(false);
        return output ?? string.Empty;
    }

    /// <summary>
    /// 尝试运行 dotnet build，返回输出或 null（启动失败）。
    /// </summary>
    private static async Task<string?> TryRunBuildAsync(
        string fullPath, string extraArgs, CancellationToken ct) {
        var psi = new System.Diagnostics.ProcessStartInfo {
            FileName = "dotnet",
            Arguments = $"build \"{fullPath}\" {extraArgs}".TrimEnd(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process is null) return null;

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return stdout + "\n" + stderr;
    }

    /// <summary>
    /// 解析 dotnet build 输出中的 JCC3016 错误位置。
    /// 格式: 文件路径(行,列): error JCC3016: 消息 [项目路径]
    /// 去重: 同一 (file, line, col) 只保留一个（dotnet build 可能重复输出）。
    /// </summary>
    private static List<Violation> ParseJcc3016Errors(string buildOutput) {
        var regex = new Regex(
            @"^(.+?)\((\d+),(\d+)\):\s*error\s+JCC3016:",
            RegexOptions.Multiline);

        var violations = new List<Violation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in regex.Matches(buildOutput)) {
            var file = match.Groups[1].Value.Trim();
            var line = int.Parse(match.Groups[2].Value);
            var col = int.Parse(match.Groups[3].Value);

            if (ShouldSkipFile(file)) continue;

            var key = $"{file}:{line}:{col}";
            if (seen.Add(key)) {
                violations.Add(new Violation(file, line, col));
            }
        }

        return violations;
    }

    /// <summary>
    /// 修复单个文件中的所有违规。
    /// 按行:列定位 InvocationExpression，替换为 .GetAwaiter().GetResult()。
    /// </summary>
    private static async Task<(int fixedCount, int issueCount, int skippedCount)> FixFileAsync(
        string filePath, List<Violation> violations, bool dryRun, CancellationToken ct) {

        var sourceText = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var text = SourceText.From(sourceText);
        var tree = CSharpSyntaxTree.ParseText(text, path: filePath);
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

        // 按行:列定位 InvocationExpression
        var invocations = new List<InvocationExpressionSyntax>();
        var foundPositions = new HashSet<int>();

        foreach (var v in violations) {
            if (ct.IsCancellationRequested) break;

            // line/col 是 1-based，SourceText 是 0-based
            var lineIndex = v.Line - 1;
            if (lineIndex < 0 || lineIndex >= text.Lines.Count) continue;

            var lineStart = text.Lines[lineIndex].Start;
            var position = lineStart + (v.Column - 1);
            if (position < 0 || position > text.Length) continue;

            // FindNode 找到位置处的节点
            var node = root.FindNode(TextSpan.FromBounds(position, position));

            // 向上查找 ExpressionStatement
            var exprStmt = node.FirstAncestorOrSelf<ExpressionStatementSyntax>();
            if (exprStmt is null) continue;

            // 提取 InvocationExpression（裸语句或赋值右侧）
            var invocation = exprStmt.Expression as InvocationExpressionSyntax;
            if (invocation is null) {
                if (exprStmt.Expression is AssignmentExpressionSyntax assign) {
                    invocation = assign.Right as InvocationExpressionSyntax;
                }
            }

            if (invocation is null) continue;

            // 去重（同一 invocation 可能被多个违规位置命中）
            var startPos = invocation.SpanStart;
            if (foundPositions.Add(startPos)) {
                invocations.Add(invocation);
            }
        }

        if (invocations.Count == 0) {
            Console.WriteLine($"    {Path.GetFileName(filePath)}: 未定位到 invocation，跳过");
            return (0, 0, violations.Count);
        }

        // 一次性替换所有违规 invocation → invocation.GetAwaiter().GetResult()
        var newRoot = root.ReplaceNodes(invocations,
            (oldNode, _) => CreateGetAwaiterGetResult(oldNode));

        if (newRoot != root) {
            if (!dryRun) {
                var newText = newRoot.ToFullString();
                await File.WriteAllTextAsync(filePath, newText, ct).ConfigureAwait(false);
            }

            Console.WriteLine($"    {Path.GetFileName(filePath)}: {invocations.Count} 处修复");
            return (1, invocations.Count, 0);
        }

        return (0, 0, violations.Count);
    }

    /// <summary>
    /// 创建 invocation.GetAwaiter().GetResult() 表达式。
    /// 保留原始 invocation 的 leading/trailing trivia。
    /// </summary>
    private static ExpressionSyntax CreateGetAwaiterGetResult(InvocationExpressionSyntax invocation) {
        var getAwaiterAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            invocation.WithoutTrailingTrivia(),
            SyntaxFactory.Token(SyntaxKind.DotToken),
            SyntaxFactory.IdentifierName("GetAwaiter"));

        var getAwaiterCall = SyntaxFactory.InvocationExpression(getAwaiterAccess);

        var getResultAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            getAwaiterCall,
            SyntaxFactory.Token(SyntaxKind.DotToken),
            SyntaxFactory.IdentifierName("GetResult"));

        var getResultCall = SyntaxFactory.InvocationExpression(getResultAccess);

        return getResultCall
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia());
    }

    /// <summary>
    /// 跳过生成代码和构建产物
    /// </summary>
    private static bool ShouldSkipFile(string? filePath) {
        if (string.IsNullOrEmpty(filePath)) return true;
        var normalized = filePath.Replace('\\', '/');
        if (normalized.Contains("/artifacts/")) return true;
        if (normalized.Contains("/obj/")) return true;
        if (normalized.Contains("/bin/")) return true;
        if (normalized.Contains("/bcl_bridge/")) return true;
        if (normalized.Contains("/aot_safety.generator/")) return true;
        if (normalized.Contains("/aot_safety.shared/")) return true;
        return false;
    }

    /// <summary>
    /// 违规位置记录
    /// </summary>
    private sealed record Violation(string FilePath, int Line, int Column);
}
