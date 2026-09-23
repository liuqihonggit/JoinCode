namespace JccAuditCli;

/// <summary>
/// GetAwaiter().GetResult() 分析器 — 用 AST 遍历所有方法节点，检测哪些方法包含 .GetAwaiter().GetResult()。
/// 分类: sync 方法（非 async）vs async 方法，输出报告。
/// 只检测，不修改。
/// </summary>
public static class GetAwaiterAnalyzer {

    /// <summary>
    /// 分析指定目录下所有 .cs 文件中的 .GetAwaiter().GetResult() 使用。
    /// </summary>
    /// <param name="rootPath">项目根目录。</param>
    /// <param name=",ct">取消令牌。</param>
    /// <returns>sync 方法中的数量（0 表示无问题）。</returns>
    public static async Task<int> AnalyzeAsync(string rootPath, CancellationToken ct) {
        Console.WriteLine("  遍历所有 .cs 文件，AST 解析方法节点...");

        var csFiles = EnumerateCsFiles(rootPath).ToList();
        Console.WriteLine($"  找到 {csFiles.Count} 个 .cs 文件");

        int syncCount = 0, asyncCount = 0;
        var syncResults = new List<(string file, int line, string method, string innerExpr)>();

        foreach (var file in csFiles) {
            if (ct.IsCancellationRequested) break;

            var sourceText = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            var text = SourceText.From(sourceText);
            var tree = CSharpSyntaxTree.ParseText(text, path: file);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

            // 遍历所有 MethodDeclarationSyntax 节点
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>()) {
                if (ct.IsCancellationRequested) break;

                var isAsync = method.Modifiers.Any(SyntaxKind.AsyncKeyword);
                var methodName = method.Identifier.ValueText;

                // 在方法体内搜索 .GetAwaiter().GetResult() 模式
                var calls = method.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Where(IsGetAwaiterGetResultPattern);

                foreach (var call in calls) {
                    var lineNum = text.Lines.GetLineFromPosition(call.SpanStart).LineNumber + 1;
                    var innerExpr = ExtractInnerExpression(call);

                    if (isAsync) {
                        asyncCount++;
                    } else {
                        syncCount++;
                        syncResults.Add((file, lineNum, methodName, innerExpr));
                    }
                }
            }
        }

        // 输出报告
        Console.WriteLine();
        Console.WriteLine("=== .GetAwaiter().GetResult() 分析报告 ===");
        Console.WriteLine($"  同步方法中: {syncCount} 处");
        Console.WriteLine($"  异步方法中: {asyncCount} 处");
        Console.WriteLine($"  总计: {syncCount + asyncCount} 处");

        if (syncCount > 0) {
            Console.WriteLine();
            Console.WriteLine("  --- 同步方法中的 .GetAwaiter().GetResult()（可考虑改 异步 + await）---");
            foreach (var (file, line, method, innerExpr) in syncResults.OrderBy(r => r.file).ThenBy(r => r.line)) {
                var relPath = Path.GetRelativePath(rootPath, file);
                Console.WriteLine($"    [同步] {relPath}:{line}  {method}()  →  {innerExpr}.GetAwaiter().GetResult()");
            }
        }

        return syncCount;
    }

    /// <summary>
    /// 检测 invocation 是否是 expr.GetAwaiter().GetResult() 模式。
    /// </summary>
    private static bool IsGetAwaiterGetResultPattern(InvocationExpressionSyntax node) {
        // node.Expression: MemberAccessExpression, Name = "GetResult"
        if (node.Expression is not MemberAccessExpressionSyntax getResultAccess) return false;
        if (getResultAccess.Name.Identifier.ValueText != "GetResult") return false;
        // getResultAccess.Expression: InvocationExpression, calling "GetAwaiter"
        if (getResultAccess.Expression is not InvocationExpressionSyntax getAwaiterInvocation) return false;
        if (getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax getAwaiterAccess) return false;
        if (getAwaiterAccess.Name.Identifier.ValueText != "GetAwaiter") return false;
        return true;
    }

    /// <summary>
    /// 提取 .GetAwaiter().GetResult() 的内部表达式（即被等待的表达式）。
    /// </summary>
    private static string ExtractInnerExpression(InvocationExpressionSyntax node) {
        if (node.Expression is not MemberAccessExpressionSyntax getResultAccess) return "?";
        if (getResultAccess.Expression is not InvocationExpressionSyntax getAwaiterInvocation) return "?";
        if (getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax getAwaiterAccess) return "?";
        return getAwaiterAccess.Expression.ToString().Trim();
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
