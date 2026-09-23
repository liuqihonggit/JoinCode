namespace JccAuditCli;

/// <summary>
/// .GetAwaiter().GetResult() 分析器 — 用 AST 遍历所有函数体，检测哪些包含 .GetAwaiter().GetResult()。
/// 检测逻辑共享 GetAwaiterPatternDetector（与修复器同一套判定），同检同换。
/// 输出详细分类表格，区分哪些可安全改 async、哪些不能改。
/// </summary>
public static class GetAwaiterAnalyzer {

    /// <summary>
    /// 分析指定目录下所有 .cs 文件中的 .GetAwaiter().GetResult() 使用。
    /// </summary>
    public static async Task<int> AnalyzeAsync(string rootPath, CancellationToken ct) {
        Console.WriteLine("  遍历所有 .cs 文件，AST 解析函数节点...");

        var csFiles = FileFilter.EnumerateCsFiles(rootPath).ToList();
        Console.WriteLine($"  找到 {csFiles.Count} 个 .cs 文件");

        var results = new List<(string file, int line, string funcName, string funcType, string access, string returnType, string riskLevel, string reason, string innerExpr)>();

        foreach (var file in csFiles) {
            if (ct.IsCancellationRequested) break;

            var sourceText = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
            var text = SourceText.From(sourceText);
            var tree = CSharpSyntaxTree.ParseText(text, path: file);
            var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
                if (ct.IsCancellationRequested) break;

                if (!GetAwaiterPatternDetector.IsGetAwaiterGetResultPattern(invocation)) continue;

                var enclosing = GetAwaiterPatternDetector.GetEnclosingFunction(invocation);
                var funcName = GetAwaiterPatternDetector.GetEnclosingFunctionName(invocation);
                var lineNum = text.Lines.GetLineFromPosition(invocation.SpanStart).LineNumber + 1;
                var innerExpr = GetAwaiterPatternDetector.ExtractInnerExpression(invocation)?.ToString().Trim() ?? "?";
                var (riskLevel, reason) = GetAwaiterPatternDetector.GetRiskLevel(invocation);

                var funcType = enclosing switch {
                    LambdaExpressionSyntax => "lambda",
                    AnonymousMethodExpressionSyntax => "anonymous",
                    MethodDeclarationSyntax => "方法",
                    _ => "?"
                };

                var access = "?";
                var returnType = "?";
                if (enclosing is MethodDeclarationSyntax method) {
                    access = GetAwaiterPatternDetector.GetAccessibility(method);
                    returnType = GetAwaiterPatternDetector.GetReturnType(method);
                } else if (enclosing is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax) {
                    access = "lambda";
                    returnType = "委托";
                }

                results.Add((file, lineNum, funcName, funcType, access, returnType, riskLevel, reason, innerExpr));
            }
        }

        // 输出汇总
        var byRisk = results.GroupBy(r => r.riskLevel).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine();
        Console.WriteLine("=== .GetAwaiter().GetResult() 分析报告 ===");
        Console.WriteLine($"  总计: {results.Count} 处");
        Console.WriteLine($"  不能改: {byRisk.GetValueOrDefault("不能改", 0)} 处");
        Console.WriteLine($"  高风险: {byRisk.GetValueOrDefault("高风险", 0)} 处");
        Console.WriteLine($"  中风险: {byRisk.GetValueOrDefault("中风险", 0)} 处");

        // 输出表格
        Console.WriteLine();
        Console.WriteLine("  ┌────────────────────────────────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("  │ 风险   │ 类型   │ 可访问性  │ 返回类型      │ 函数名                    │ 文件:行                    │ 原因                 │");
        Console.WriteLine("  ├────────────────────────────────────────────────────────────────────────────────────────────────┤");

        foreach (var r in results.OrderBy(r => r.riskLevel != "不能改").ThenBy(r => r.riskLevel != "高风险").ThenBy(r => r.file).ThenBy(r => r.line)) {
            var relPath = Path.GetRelativePath(rootPath, r.file);
            var location = $"{relPath}:{r.line}";
            Console.WriteLine($"  │ {r.riskLevel,-6} │ {r.funcType,-6} │ {r.access,-9} │ {r.returnType,-13} │ {r.funcName,-25} │ {location,-26} │ {r.reason,-20} │");
        }

        Console.WriteLine("  └────────────────────────────────────────────────────────────────────────────────────────────────┘");

        // 统计可安全改的（中风险 private 方法）
        var safeCount = results.Count(r => r.riskLevel == "中风险");
        Console.WriteLine();
        Console.WriteLine($"  可安全改（中风险 private 方法）: {safeCount} 处");
        Console.WriteLine($"  需评估（高风险）: {byRisk.GetValueOrDefault("高风险", 0)} 处");
        Console.WriteLine($"  不能改: {byRisk.GetValueOrDefault("不能改", 0)} 处");

        return results.Count;
    }
}
