namespace JccAuditCli;

/// <summary>
/// .GetAwaiter().GetResult() 分析器 — 用 AST 遍历所有函数体，检测哪些包含 .GetAwaiter().GetResult()。
/// 检测逻辑共享 GetAwaiterPatternDetector（与修复器同一套判定），同检同换。
/// 判断"直接包含的函数体"（lambda 或方法）是否 async，而非外层方法。
/// 只检测，不修改。
/// </summary>
public static class GetAwaiterAnalyzer {

    /// <summary>
    /// 分析指定目录下所有 .cs 文件中的 .GetAwaiter().GetResult() 使用。
    /// </summary>
    public static async Task<int> AnalyzeAsync(string rootPath, CancellationToken ct) {
        Console.WriteLine("  遍历所有 .cs 文件，AST 解析函数节点...");

        var csFiles = EnumerateCsFiles(rootPath).ToList();
        Console.WriteLine($"  找到 {csFiles.Count} 个 .cs 文件");

        int syncCount = 0, asyncCount = 0, fixableCount = 0;
        var results = new List<(string file, int line, string funcName, string innerExpr, bool isAsync, bool isFixable, string skipReason)>();

        foreach (var file in csFiles) {
            if (ct.IsCancellationRequested) break;

            var sourceText = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            var text = SourceText.From(sourceText);
            var tree = CSharpSyntaxTree.ParseText(text, path: file);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

            // 遍历所有 InvocationExpression 节点，用共享检测器判断
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
                if (ct.IsCancellationRequested) break;

                // 共享检测逻辑 — 与修复器调用同一套判定
                if (!GetAwaiterPatternDetector.IsGetAwaiterGetResultPattern(invocation)) continue;

                // 判断直接包含的函数体（lambda 或方法）是否 async
                var isAsync = GetAwaiterPatternDetector.IsInAsyncContext(invocation);
                var isFixable = GetAwaiterPatternDetector.IsFixableViolation(invocation);
                var funcName = GetAwaiterPatternDetector.GetEnclosingFunctionName(invocation);
                var lineNum = text.Lines.GetLineFromPosition(invocation.SpanStart).LineNumber + 1;
                var innerExpr = GetAwaiterPatternDetector.ExtractInnerExpression(invocation)?.ToString().Trim() ?? "?";

                // 获取跳过原因（如果不可修复）
                var skipReason = string.Empty;
                if (!isFixable) {
                    if (GetAwaiterPatternDetector.IsInMainMethod(invocation)) skipReason = "Main方法";
                    else if (GetAwaiterPatternDetector.IsInPropertyGetter(invocation)) skipReason = "属性getter";
                    else if (isAsync) skipReason = "已是异步";
                }

                if (isAsync) asyncCount++; else syncCount++;
                if (isFixable) fixableCount++;
                results.Add((file, lineNum, funcName, innerExpr, isAsync, isFixable, skipReason));
            }
        }

        // 输出报告
        Console.WriteLine();
        Console.WriteLine("=== .GetAwaiter().GetResult() 分析报告 ===");
        Console.WriteLine($"  同步函数体中: {syncCount} 处");
        Console.WriteLine($"  异步函数体中: {asyncCount} 处");
        Console.WriteLine($"  可修复（同步函数体且不在跳过列表）: {fixableCount} 处");
        Console.WriteLine($"  总计: {syncCount + asyncCount} 处");

        if (results.Count > 0) {
            Console.WriteLine();
            Console.WriteLine("  --- 明细 ---");
            foreach (var r in results.OrderBy(r => r.file).ThenBy(r => r.line)) {
                var relPath = Path.GetRelativePath(rootPath, r.file);
                var tag = r.isFixable ? "[可修复]" : $"[跳过:{r.skipReason}]";
                var funcType = r.isAsync ? "异步" : "同步";
                Console.WriteLine($"    {tag} {relPath}:{r.line}  {funcType} {r.funcName}  →  {r.innerExpr}.GetAwaiter().GetResult()");
            }
        }

        return fixableCount;
    }

    /// <summary>
    /// 遍历目录下所有 .cs 文件，排除生成代码和构建产物。
    /// </summary>
    private static IEnumerable<string> EnumerateCsFiles(string rootPath) {
        return Directory.EnumerateFiles(rootPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !ShouldSkipFile(f));
    }

    /// <summary>
    /// 跳过生成代码和构建产物。
    /// </summary>
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
        return false;
    }
}
