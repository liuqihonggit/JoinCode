namespace JccAuditCli;

/// <summary>
/// 编译错误驱动的修复器 — 运行 dotnet build，解析 CS1503/CS1061 错误位置，精确加 await
/// </summary>
public static class BuildErrorFixer {

    /// <summary>
    /// 运行 dotnet build 获取错误，在错误位置加 await
    /// </summary>
    public static async Task<(int FixedFiles, int FixedIssues)> FixFromBuildErrorsAsync(
        string solutionPath, bool dryRun, CancellationToken ct = default) {

        var errors = await CollectBuildErrorsAsync(solutionPath, ct);
        if (errors.Count == 0) {
            Console.WriteLine("没有 CS1503/CS1061 错误。");
            return (0, 0);
        }

        Console.WriteLine($"发现 {errors.Count} 个 CS1503/CS1061 错误。");

        var byFile = errors.GroupBy(e => e.FilePath, StringComparer.OrdinalIgnoreCase);
        var fixedFiles = 0;
        var fixedIssues = 0;

        foreach (var group in byFile) {
            ct.ThrowIfCancellationRequested();
            var errorLines = group.Select(e => e.Line).ToHashSet();

            var source = await File.ReadAllTextAsync(group.Key, ct);
            var tree = CSharpSyntaxTree.ParseText(source, path: group.Key);
            var root = await tree.GetRootAsync(ct);

            var rewriter = new UnawaitedVariableRewriter(errorLines);
            var newRoot = rewriter.Visit(root);

            if (rewriter.FixedCount == 0) continue;

            var newSource = newRoot.ToFullString();
            if (newSource == source) continue;

            Console.WriteLine($"  {Path.GetFileName(group.Key)}: {rewriter.FixedCount} 个变量加 await");

            if (!dryRun)
                await File.WriteAllTextAsync(group.Key, newSource, ct);

            fixedFiles++;
            fixedIssues += rewriter.FixedCount;
        }

        return (fixedFiles, fixedIssues);
    }

    /// <summary>
    /// 运行 dotnet build，解析 CS1503/CS1061 错误
    /// </summary>
    private static async Task<List<BuildError>> CollectBuildErrorsAsync(string solutionPath, CancellationToken ct) {
        var slnFullPath = Path.GetFullPath(solutionPath);
        var psi = new System.Diagnostics.ProcessStartInfo {
            FileName = "cmd",
            Arguments = $"/c dotnet build \"{slnFullPath}\" --no-incremental 2>&1",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var errors = new List<BuildError>();
        var regex = new System.Text.RegularExpressions.Regex(
            @"^(.+?)\((\d+),(\d+)\):\s*error\s+(CS1503|CS1061)",
            System.Text.RegularExpressions.RegexOptions.Multiline);

        foreach (System.Text.RegularExpressions.Match m in regex.Matches(output)) {
            errors.Add(new BuildError(
                m.Groups[1].Value.Trim(),
                int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[3].Value),
                m.Groups[4].Value));
        }

        return errors;
    }

    private sealed record BuildError(string FilePath, int Line, int Column, string ErrorCode);
}

/// <summary>
/// 在指定行的变量声明加 await
/// </summary>
internal class UnawaitedVariableRewriter : CSharpSyntaxRewriter {
    private readonly HashSet<int> _errorLines;
    /// <summary>
    /// 已修复的变量声明数
    /// </summary>
    public int FixedCount { get; private set; }

    internal UnawaitedVariableRewriter(HashSet<int> errorLines) {
        _errorLines = errorLines;
    }

    public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node) {
        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!_errorLines.Contains(line))
            return base.VisitVariableDeclarator(node);

        var initializer = node.Initializer;
        if (initializer is null) return base.VisitVariableDeclarator(node);
        var value = initializer.Value;
        if (value.Parent is AwaitExpressionSyntax) return base.VisitVariableDeclarator(node);

        var varName = node.Identifier.ValueText;
        if (varName.StartsWith("task", StringComparison.OrdinalIgnoreCase))
            return base.VisitVariableDeclarator(node);

        var awaited = SyntaxHelpers.CreateAwaitExpression(value, value, isTestFile: true);
        FixedCount++;
        return node.WithInitializer(initializer.WithValue(awaited));
    }
}
