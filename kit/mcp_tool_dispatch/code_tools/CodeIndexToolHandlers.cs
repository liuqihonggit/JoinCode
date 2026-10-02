
namespace McpToolDispatch;

/// <summary>
/// 代码索引工具处理器 - 提供 C# AST 符号搜索、定义/引用查找、调用图与依赖图分析等功能
/// </summary>
[McpToolDispatch(ToolCategory.CodeIndex, Optional = true)]
public sealed class CodeIndexToolHandlers {
    private readonly ICodeIndexer _indexer;
    private readonly IProgressiveDisclosure? _disclosure;

    /// <summary>
    /// 初始化 <see cref="CodeIndexToolHandlers"/> 实例
    /// </summary>
    /// <param name="indexer">代码索引器</param>
    /// <param name="disclosure">渐进式披露器（可选）</param>
    public CodeIndexToolHandlers(ICodeIndexer indexer, IProgressiveDisclosure? disclosure = null) {
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _disclosure = disclosure;
    }

    /// <summary>
    /// 搜索 C# AST 符号（类、方法、属性等），仅搜索已解析的 .cs 文件
    /// </summary>
    /// <param name="query">搜索查询（支持 FTS5 全文搜索语法）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含匹配符号列表的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexSearch, "C# AST symbol search ONLY. Searches indexed C# code symbols (classes, methods, properties, etc.) from parsed .cs files. Do NOT use for config files, docs, JSON, YAML, or non-C# content - use grep/glob instead.", "code_index")]
    public async Task<ToolResult> SearchAsync(
        [McpToolParameter("Search query (supports FTS5 full-text search syntax)")] string query,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(query)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.QueryCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var result = await _indexer.Searcher.SearchAsync(query, cancellationToken).ConfigureAwait(false);

            if (result.Items.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.NoMatchingSymbols, query)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.FoundSymbolsCount, result.TotalCount, result.ElapsedMs));
            sb.AppendLine();

            for (var i = 0; i < Math.Min(result.Items.Count, 30); i++) {
                var symbol = result.Items[i];
                sb.AppendLine($"{i + 1}. {FormatSymbolKind(symbol.Kind)} {symbol.Name}");
                sb.AppendLine($"   {L.T(StringKey.LabelLocation, symbol.FilePath, symbol.StartLine)}");

                if (!string.IsNullOrEmpty(symbol.ParentSymbol)) {
                    sb.AppendLine($"   {L.T(StringKey.LabelParentSymbol, symbol.ParentSymbol)}");
                }

                if (!string.IsNullOrEmpty(symbol.Namespace)) {
                    sb.AppendLine($"   {L.T(StringKey.LabelNamespace, symbol.Namespace)}");
                }

                sb.AppendLine();
            }

            if (result.Items.Count > 30) {
                sb.AppendLine(L.T(StringKey.MoreResults, result.Items.Count - 30));
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.SymbolSearchFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 综合检索 C# 符号：模糊匹配符号后 AST 提取引用及调用方/被调用方
    /// </summary>
    /// <param name="pattern">匹配符号名称/全限定名的正则模式（rg 风格）</param>
    /// <param name="max_token_budget">结果的最大 token 预算</param>
    /// <param name="include_ast">是否包含 AST 展开（引用及调用方/被调用方）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含匹配符号、引用、调用方和被调用方的综合检索结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexSearchComprehensive, "C# AST comprehensive search ONLY. Fuzzy match C# symbols then AST-extract references + callers/callees. Do NOT use for config, docs, JSON, YAML, or non-C# content - use grep/glob instead.", "code_index")]
    public async Task<ToolResult> SearchComprehensiveAsync(
        [McpToolParameter("Regex pattern to fuzzy match symbol name/FQN (rg-style, e.g. 'User*' or 'Get.*Name')")] string pattern,
        [McpToolParameter("Max token budget for result (approx 4 chars/token, truncated if exceeded, default 2000)")] int max_token_budget = 2000,
        [McpToolParameter("Include AST expansion (references + callers/callees). Set false to only return symbol matches, saving tokens for non-code queries. Default true.")] bool include_ast = true,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(pattern)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.QueryCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var result = await _indexer.SearchComprehensiveAsync(pattern, max_token_budget, cancellationToken, include_ast).ConfigureAwait(false);

            // 真正无匹配符号 (TotalMatchedCount==0): 返回空结果提示
            // 注意: 不能用 MatchedSymbols.Count==0 判断,因为预算过小会截断到 0,但实际有匹配
            if (result.TotalMatchedCount == 0) {
                var emptySb = new System.Text.StringBuilder();
                emptySb.AppendLine(L.T(StringKey.NoMatchingSymbols, pattern));
                emptySb.AppendLine($"耗时: {result.ElapsedMs}ms");
                return ToolResultBuilder.Success().WithText(emptySb.ToString()).Build();
            }

            var sb = new System.Text.StringBuilder();
            // 头部摘要: 匹配 N 个符号，引用 M 个，调用方 P 个，被调用方 Q 个
            sb.AppendLine($"综合检索完成: 匹配 {result.MatchedSymbols.Count} 个符号, 引用 {result.References.Count} 个, 调用方 {result.Callers.Count} 个, 被调用方 {result.Callees.Count} 个");
            sb.AppendLine($"预估 token: {result.EstimatedTokens} / {max_token_budget} (预算), 耗时: {result.ElapsedMs}ms");

            // 候选上限提示: 实际匹配数 > 显示数,说明被 100 候选上限截断
            if (result.TotalMatchedCount > result.MatchedSymbols.Count) {
                sb.AppendLine($"⚠ 匹配数超过候选上限,实际共 {result.TotalMatchedCount} 个,仅显示前 {result.MatchedSymbols.Count} 个,建议缩小 pattern 范围");
            }

            // 截断提示
            if (result.Truncated) {
                sb.AppendLine($"⚠ 结果已截断(达 token 预算上限,已截断 {result.TruncatedCount} 条,优先级: matched > references > callers > callees),建议缩小 pattern 或提高 max_token_budget");
            }
            sb.AppendLine();

            // === 匹配符号 ===
            sb.AppendLine("=== 匹配符号 ===");
            for (var i = 0; i < result.MatchedSymbols.Count; i++) {
                var symbol = result.MatchedSymbols[i];
                sb.AppendLine($"{i + 1}. {FormatSymbolKind(symbol.Kind)} {symbol.Name}");
                sb.AppendLine($"   {L.T(StringKey.LabelLocation, symbol.FilePath, symbol.StartLine)}");
                if (!string.IsNullOrEmpty(symbol.ParentSymbol)) {
                    sb.AppendLine($"   {L.T(StringKey.LabelParentSymbol, symbol.ParentSymbol)}");
                }
            }
            sb.AppendLine();

            // === 引用 ===
            if (result.References.Count > 0) {
                sb.AppendLine("=== 引用 ===");
                for (var i = 0; i < result.References.Count; i++) {
                    var symbol = result.References[i];
                    sb.AppendLine($"{i + 1}. {FormatSymbolKind(symbol.Kind)} {symbol.Name}");
                    sb.AppendLine($"   {L.T(StringKey.LabelLocation, symbol.FilePath, symbol.StartLine)}");
                }
                sb.AppendLine();
            }

            // === 调用方 ===
            if (result.Callers.Count > 0) {
                sb.AppendLine("=== 调用方 ===");
                for (var i = 0; i < result.Callers.Count; i++) {
                    var edge = result.Callers[i];
                    sb.AppendLine($"{i + 1}. {edge.CallerSymbol} [{edge.CallKind}]");
                    sb.AppendLine($"   {L.T(StringKey.LabelCallSite, edge.CallSiteFilePath, edge.CallSiteLine)}");
                }
                sb.AppendLine();
            }

            // === 被调用方 ===
            if (result.Callees.Count > 0) {
                sb.AppendLine("=== 被调用方 ===");
                for (var i = 0; i < result.Callees.Count; i++) {
                    var edge = result.Callees[i];
                    sb.AppendLine($"{i + 1}. {edge.CalleeSymbol} [{edge.CallKind}]");
                    sb.AppendLine($"   {L.T(StringKey.LabelCallSite, edge.CallSiteFilePath, edge.CallSiteLine)}");
                }
                sb.AppendLine();
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText($"综合检索失败: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 查找指定 C# 符号的定义位置
    /// </summary>
    /// <param name="symbol_name">符号名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含符号定义位置信息的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexFindDefinition, "Find the definition location of a C# symbol in AST index", "code_index")]
    public async Task<ToolResult> FindDefinitionAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(symbol_name)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var definition = await _indexer.Searcher.FindDefinitionAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (definition is null) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.SymbolDefinitionNotFound, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.LabelSymbolDefinition, definition.Name));
            sb.AppendLine(L.T(StringKey.SyncLabelType, definition.Kind));
            sb.AppendLine(L.T(StringKey.LabelLocation, definition.FilePath, definition.StartLine));

            if (!string.IsNullOrEmpty(definition.ParentSymbol)) {
                sb.AppendLine(L.T(StringKey.LabelParentSymbol, definition.ParentSymbol));
            }

            if (!string.IsNullOrEmpty(definition.Namespace)) {
                sb.AppendLine(L.T(StringKey.LabelNamespace, definition.Namespace));
            }

            if (!string.IsNullOrEmpty(definition.Accessibility)) {
                sb.AppendLine(L.T(StringKey.LabelAccessModifier, definition.Accessibility));
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FindDefinitionFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 查找指定 C# 符号的所有引用
    /// </summary>
    /// <param name="symbol_name">符号名称</param>
    /// <param name="limit">最大返回引用数（默认 5，传更大值获取更多）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含符号所有引用位置的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexFindReferences, "Find all references to a C# symbol in AST index", "code_index")]
    public async Task<ToolResult> FindReferencesAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        [McpToolParameter("Maximum references to return (default 5, pass larger to see more)")] int limit = 5,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(symbol_name)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var references = await _indexer.Searcher.FindReferencesAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (references.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.SymbolReferencesNotFound, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            var totalCount = references.Count;
            var toShow = references.Take(limit).ToList();
            sb.AppendLine(L.T(StringKey.FoundReferencesCount, totalCount));
            sb.AppendLine();

            var grouped = toShow.GroupBy(r => r.FilePath);

            foreach (var group in grouped) {
                sb.AppendLine($"{ObjectSymbol.File.ToValue()} {group.Key}");

                foreach (var symbol in group.OrderBy(s => s.StartLine)) {
                    sb.AppendLine($"   {L.T(StringKey.LabelLine, symbol.StartLine, FormatSymbolKind(symbol.Kind), symbol.Name)}");
                }

                sb.AppendLine();
            }

            if (totalCount > limit)
                sb.AppendLine($"... and {totalCount - limit} more (pass limit={totalCount} to see all)");

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FindReferencesFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 查找指定 C# 符号的所有调用方
    /// </summary>
    /// <param name="symbol_name">符号名称</param>
    /// <param name="limit">最大返回调用方数（默认 5，传更大值获取更多）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含所有调用方信息的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexGetCallers, "Find all callers of a specified C# symbol in AST index", "code_index")]
    public async Task<ToolResult> GetCallersAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        [McpToolParameter("Maximum callers to return (default 5, pass larger to see more)")] int limit = 5,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(symbol_name)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var callers = await _indexer.CallGraph.GetCallersAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (callers.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.CallersNotFound, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            var totalCount = callers.Count;
            var toShow = callers.Take(limit).ToList();
            sb.AppendLine(L.T(StringKey.CallersOfSymbol, symbol_name, totalCount));
            sb.AppendLine();

            for (var i = 0; i < toShow.Count; i++) {
                var edge = toShow[i];
                sb.AppendLine($"{i + 1}. {edge.CallerSymbol} [{edge.CallKind}]");
                sb.AppendLine($"   {L.T(StringKey.LabelCallSite, edge.CallSiteFilePath, edge.CallSiteLine)}");
                sb.AppendLine();
            }

            if (totalCount > limit)
                sb.AppendLine($"... and {totalCount - limit} more (pass limit={totalCount} to see all)");

            sb.AppendLine("Triples:");
            foreach (var edge in toShow)
                sb.AppendLine($"  ({edge.CallerSymbol}, calls, {symbol_name})");

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FindCallersFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 查找指定 C# 符号调用的所有被调用方
    /// </summary>
    /// <param name="symbol_name">符号名称</param>
    /// <param name="limit">最大返回被调用方数（默认 5，传更大值获取更多）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含所有被调用方信息的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexGetCallees, "Find all callees invoked by a specified C# symbol in AST index", "code_index")]
    public async Task<ToolResult> GetCalleesAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        [McpToolParameter("Maximum callees to return (default 5, pass larger to see more)")] int limit = 5,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(symbol_name)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var callees = await _indexer.CallGraph.GetCalleesAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (callees.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.CalleesNotFound, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            var totalCount = callees.Count;
            var toShow = callees.Take(limit).ToList();
            sb.AppendLine(L.T(StringKey.CalleesOfSymbol, symbol_name, totalCount));
            sb.AppendLine();

            for (var i = 0; i < toShow.Count; i++) {
                var edge = toShow[i];
                sb.AppendLine($"{i + 1}. {edge.CalleeSymbol} [{edge.CallKind}]");
                sb.AppendLine($"   {L.T(StringKey.LabelCallSite, edge.CallSiteFilePath, edge.CallSiteLine)}");
                sb.AppendLine();
            }

            if (totalCount > limit)
                sb.AppendLine($"... and {totalCount - limit} more (pass limit={totalCount} to see all)");

            sb.AppendLine("Triples:");
            foreach (var edge in toShow)
                sb.AppendLine($"  ({symbol_name}, calls, {edge.CalleeSymbol})");

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FindCalleesFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 查找两个符号之间的调用链
    /// </summary>
    /// <param name="from">起始符号名称</param>
    /// <param name="to">目标符号名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含调用链路径的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexGetCallChain, "Find the call chain between two symbols", "code_index")]
    public async Task<ToolResult> GetCallChainAsync(
        [McpToolParameter("Source symbol name")] string from,
        [McpToolParameter("Target symbol name")] string to,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(from)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FromCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(to)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.ToCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var chain = await _indexer.CallGraph.GetCallChainAsync(from, to, cancellationToken).ConfigureAwait(false);

            if (chain.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.CallChainNotFound, from, to)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.CallChainSteps, from, to, chain.Count));
            sb.AppendLine();

            for (var i = 0; i < chain.Count; i++) {
                var edge = chain[i];
                sb.AppendLine($"{i + 1}. {edge.CallerSymbol} → {edge.CalleeSymbol} [{edge.CallKind}]");
                sb.AppendLine($"   {L.T(StringKey.LabelLocation, edge.CallSiteFilePath, edge.CallSiteLine)}");
                sb.AppendLine();
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FindCallChainFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 分析修改指定符号的影响范围
    /// </summary>
    /// <param name="symbol_name">符号名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含受影响符号列表的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexGetImpactScope, "Analyze the impact scope of modifying a symbol", "code_index")]
    public async Task<ToolResult> GetImpactScopeAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(symbol_name)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var scope = await _indexer.CallGraph.GetImpactScopeAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (scope.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.ModifyNoImpact, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.ImpactScopeOfSymbol, symbol_name, scope.Count));
            sb.AppendLine();

            for (var i = 0; i < scope.Count; i++) {
                sb.AppendLine($"{i + 1}. {scope[i]}");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.ImpactScopeAnalysisFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 查找继承或实现指定符号的所有类型
    /// </summary>
    /// <param name="symbol_name">符号名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含所有继承者信息的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexGetInheritors, "Find types that inherit or implement a specified symbol", "code_index")]
    public async Task<ToolResult> GetInheritorsAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(symbol_name)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var inheritors = await _indexer.DependencyGraph.GetInheritorsAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (inheritors.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.InheritorsNotFound, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.InheritorsOfSymbol, symbol_name, inheritors.Count));
            sb.AppendLine();

            for (var i = 0; i < inheritors.Count; i++) {
                var edge = inheritors[i];
                sb.AppendLine($"{i + 1}. {edge.SourceSymbol} [{edge.DependencyKind}]");
                sb.AppendLine();
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FindInheritorsFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 查找指定符号的依赖项
    /// </summary>
    /// <param name="symbol_name">符号名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含所有依赖项信息的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexGetDependencies, "Find dependencies of a specified symbol", "code_index")]
    public async Task<ToolResult> GetDependenciesAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(symbol_name)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var deps = await _indexer.DependencyGraph.GetDependenciesAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (deps.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.DependenciesNotFound, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.DependenciesOfSymbol, symbol_name, deps.Count));
            sb.AppendLine();

            for (var i = 0; i < deps.Count; i++) {
                var edge = deps[i];
                sb.AppendLine($"{i + 1}. → {edge.TargetSymbol} [{edge.DependencyKind}]");
                sb.AppendLine();
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FindDependenciesFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 分析修改指定文件后受影响的文件
    /// </summary>
    /// <param name="file_path">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含受影响文件列表的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexGetAffectedFiles, "Analyze files affected by modifying a specified file", "code_index")]
    public async Task<ToolResult> GetAffectedFilesAsync(
        [McpToolParameter("File path")] string file_path,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FilePathCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var files = await _indexer.DependencyGraph.GetAffectedFilesAsync(file_path, cancellationToken).ConfigureAwait(false);

            if (files.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.ModifyFileNoImpact, file_path)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.AffectedFilesOfModify, file_path, files.Count));
            sb.AppendLine();

            for (var i = 0; i < files.Count; i++) {
                sb.AppendLine($"{i + 1}. {files[i]}");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.AffectedFilesAnalysisFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 重建代码索引
    /// </summary>
    /// <param name="workspace_root">工作区根目录路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含重建统计信息的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexRebuild, "Rebuild the code index", "code_index")]
    public async Task<ToolResult> RebuildAsync(
        [McpToolParameter("Workspace root directory path")] string workspace_root,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(workspace_root)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.WorkspaceRootCannotBeEmpty)).Build();
        }

        try {
            var prevPriority = System.Diagnostics.Process.GetCurrentProcess().PriorityClass;
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
            try {
                var options = new CodeIndexOptions { WorkspaceRoot = workspace_root };
                var result = await _indexer.BuildIndexAsync(options, cancellationToken).ConfigureAwait(false);

            var persistDir = Path.Combine(workspace_root, ".jcc", "code-index");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.IndexRebuildComplete));
            sb.AppendLine(L.T(StringKey.UpdatedFiles, result.UpdatedCount));
            sb.AppendLine(L.T(StringKey.SkippedFiles, result.SkippedCount));
            sb.AppendLine(L.T(StringKey.DeletedFiles, result.DeletedCount));
            sb.AppendLine($"持久化目录: {persistDir.Replace('\\', '/')}");
            sb.AppendLine($"  ✅ 符号索引: KV store ({_indexer.Persistence.Count} 个符号)");
            if (result.VectorChunkCount > 0) {
                sb.AppendLine($"  ✅ 向量索引: vector_index.bin ({result.VectorChunkCount} 个块)");
            } else {
                sb.AppendLine($"  ⚠ 向量索引: 未建立（模型文件不存在，语义搜索不可用）");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
            } finally {
                System.Diagnostics.Process.GetCurrentProcess().PriorityClass = prevPriority;
            }
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.IndexRebuildFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 获取代码索引统计信息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含索引统计信息的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexStats, "Get code index statistics", "code_index")]
    public async Task<ToolResult> GetStatsAsync(
        CancellationToken cancellationToken = default) {
        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var stats = await _indexer.GetStatsAsync(cancellationToken).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.CodeIndexStats));
            sb.AppendLine(L.T(StringKey.StatsFileCount, stats.FileCount));
            sb.AppendLine(L.T(StringKey.StatsSymbolCount, stats.SymbolCount));
            sb.AppendLine(L.T(StringKey.StatsCallEdgeCount, stats.CallEdgeCount));
            sb.AppendLine(L.T(StringKey.StatsDependencyEdgeCount, stats.DependencyEdgeCount));
            sb.AppendLine(L.T(StringKey.StatsProjectCount, stats.ProjectCount));
            sb.AppendLine(L.T(StringKey.StatsLastUpdated, stats.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss")));

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.GetStatsFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 渐进式探索 C# 代码：符号索引 → 调用关系 → 源代码
    /// </summary>
    /// <param name="query">搜索查询（符号名称或关键字）</param>
    /// <param name="level">披露级别：index=仅符号索引，relationships=含调用图，source=含源代码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含渐进式探索结果的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexExplore, "Progressively explore C# code: symbol index -> call relationships -> source code. AST index only.", "code_index")]
    public async Task<ToolResult> ExploreAsync(
        [McpToolParameter("Search query (symbol name or keyword)")] string query,
        [McpToolParameter("Disclosure level: index=symbol index only, relationships=with call graph, source=with source code")] string level = "index",
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(query)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.QueryCannotBeEmpty)).Build();
        }

        if (_disclosure is null) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.ProgressiveDisclosureNotEnabled)).Build();
        }

        var disclosureLevel = level.ToLowerInvariant() switch {
            "index" => DisclosureLevel.Index,
            "relationships" => DisclosureLevel.Relationships,
            "source" => DisclosureLevel.Source,
            _ => DisclosureLevel.Index
        };

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var result = await _disclosure.DiscloseAsync(query, disclosureLevel, cancellationToken).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(result.FormattedContent);
            sb.AppendLine();
            sb.AppendLine($"---");
            sb.AppendLine(L.T(StringKey.DisclosureLevelInfo, result.Level, result.EstimatedTokens));

            if (result.HasMoreDetails) {
                sb.AppendLine(L.T(StringKey.NeedMoreInfoHint, ObjectSymbol.DiamondFilled.ToValue(), result.Level == DisclosureLevel.Index ? "relationships" : "source"));
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.ProgressiveExploreFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 语义搜索代码块 — 通过向量嵌入按语义相似度召回（找类似代码，不是精确匹配）。
    /// </summary>
    /// <param name="query">自然语言查询或代码片段</param>
    /// <param name="top_k">返回结果数上限</param>
    /// <param name="include_source_text">是否返回匹配块原文（函数源码）</param>
    /// <param name="include_parent_document">是否返回父文档原文（类/文件完整源码）</param>
    /// <param name="include_graph">是否返回知识图谱关联（调用方/被调用方），默认 true</param>
    /// <param name="file_type">文件类型过滤@过滤（扩展名不含点，如 "cs"/"md"），null=全部</param>
    /// <param name="namespace_filter">命名空间过滤（前缀匹配，如 "JoinCode.CodeIndex"），null=全部</param>
    /// <param name="symbol_kind">符号类型过滤（如 "Method"/"Class"），null=全部</param>
    /// <param name="persist_dir">持久化目录路径，用于从外部位置加载索引。null=自动发现 git 工作区</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含匹配代码块列表的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexSearchSemantic, "Semantic search code blocks via vector embeddings. Find similar code by meaning, not exact text match. Returns metadata (file path, line range, symbol name, similarity score). Set include_source_text=true to get matched block source code. Set include_parent_document=true to get parent class/file source. Set include_graph=true to get caller/callee relations.", "code_index")]
    public async Task<ToolResult> SearchSemanticAsync(
        [McpToolParameter("Natural language query or code snippet (e.g. 'find authentication logic', 'rate limiting implementation')")] string query,
        [McpToolParameter("Maximum number of results to return", Required = false, DefaultValue = "10")] int top_k = 10,
        [McpToolParameter("Include matched block source text (function code) in results", Required = false, DefaultValue = "false")] bool include_source_text = false,
        [McpToolParameter("Include parent document (class/file) source text in results for full context", Required = false, DefaultValue = "false")] bool include_parent_document = false,
        [McpToolParameter("Include knowledge graph triples (caller,calls,callee). GraphRAG: vector recall + graph triples correct AI cognition", Required = false, DefaultValue = "true")] bool include_graph = true,
        [McpToolParameter("Filter by file extension without dot, e.g. 'cs' for C# only, 'md' for Markdown only. Default null = all file types", Required = false)] string? file_type = null,
        [McpToolParameter("Filter by namespace prefix, e.g. 'JoinCode.CodeIndex' for code in that namespace only. Default null = all namespaces", Required = false)] string? namespace_filter = null,
        [McpToolParameter("Filter by symbol kind, e.g. 'Method' for methods only, 'Class' for classes only. Default null = all kinds", Required = false)] string? symbol_kind = null,
        [McpToolParameter("Persistence directory path to load index from. Default null = auto-discover git workspace root", Required = false)] string? persist_dir = null,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(query)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.QueryCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken, persist_dir).ConfigureAwait(false);
            var options = new SearchOptions {
                IncludeSourceText = include_source_text,
                IncludeParentDocument = include_parent_document,
                FileType = file_type,
                Namespace = namespace_filter,
                SymbolKind = symbol_kind
            };
            var oversampleK = Math.Max(top_k * 3, top_k + 10);
            var rawResults = await _indexer.SearchSemanticAsync(query, oversampleK, cancellationToken, options).ConfigureAwait(false);
            var results = await SemanticSearchReranker.RerankAsync(
                query, rawResults, top_k, _indexer.CallGraph, cancellationToken).ConfigureAwait(false);

            if (results.Count == 0) {
                var stats = await _indexer.GetStatsAsync(cancellationToken).ConfigureAwait(false);
                if (stats.SymbolCount == 0) {
                    return ToolResultBuilder.Success().WithText(
                        $"当前为无索引状态 — 未找到任何已构建的代码索引。\n" +
                        $"查询: \"{query}\"\n\n" +
                        "要构建索引，请调用 code_index_rebuild 工具：\n" +
                        "  code_index_rebuild(workspace_root=\"<你的工作区根目录路径>\")\n" +
                        "构建完成后再次调用 code_index_search_semantic 即可进行语义搜索。"
                    ).Build();
                }
                return ToolResultBuilder.Success().WithText(
                    $"已索引 {stats.SymbolCount} 个符号，但未找到与 \"{query}\" 语义相似的代码块。\n" +
                    "建议：换用不同关键词，或调用 code_index_search 进行精确符号搜索。"
                ).Build();
            }

            var sb = new StringBuilder();
            var seenFqns = new HashSet<string>(StringComparer.Ordinal);
            sb.AppendLine($"Found {results.Count} semantically similar code block(s) for: \"{query}\"");
            sb.AppendLine();

            for (var i = 0; i < results.Count; i++) {
                var r = results[i];
                sb.AppendLine($"{i + 1}. [{r.Score:F4}] {r.SymbolFqn}");
                sb.AppendLine($"   {r.FilePath.Replace('\\', '/')}:{r.StartLine}-{r.EndLine}");

                if (include_source_text && !string.IsNullOrEmpty(r.SourceText)) {
                    sb.AppendLine($"   --- Source ---");
                    var sourceLines = r.SourceText.Split('\n');
                    foreach (var line in sourceLines) {
                        sb.AppendLine($"   {line}");
                    }
                    sb.AppendLine($"   --- End Source ---");
                }

                if (include_parent_document && !string.IsNullOrEmpty(r.ParentDocumentText)) {
                    sb.AppendLine($"   --- Parent: {r.ParentSymbolFqn} ({r.ParentStartLine}-{r.ParentEndLine}) ---");
                    var parentLines = r.ParentDocumentText.Split('\n');
                    var previewLines = parentLines.Length > 50
                        ? parentLines.Take(50).Concat(new[] { $"... ({parentLines.Length} lines total)" })
                        : parentLines;
                    foreach (var line in previewLines) {
                        sb.AppendLine($"   {line}");
                    }
                    sb.AppendLine($"   --- End Parent ---");
                }

                if (include_graph && r.ContainedSymbolFqns.Count > 0) {
                    var graphWritten = false;
                    const int FqnLimit = 5;
                    const int EdgeLimit = 2;
                    var fqnProcessed = 0;
                    foreach (var fqn in r.ContainedSymbolFqns) {
                        if (fqnProcessed >= FqnLimit) break;
                        if (!seenFqns.Add(fqn)) continue;
                        fqnProcessed++;

                        var callers = await _indexer.CallGraph.GetCallersAsync(fqn, cancellationToken).ConfigureAwait(false);
                        var callees = await _indexer.CallGraph.GetCalleesAsync(fqn, cancellationToken).ConfigureAwait(false);

                        if (callers.Count == 0 && callees.Count == 0) continue;
                        if (!graphWritten) {
                            sb.AppendLine("   --- Graph ---");
                            graphWritten = true;
                        }

                        if (callers.Count > 0) {
                            sb.AppendLine($"   {fqn} <- 调用方:");
                            foreach (var c in callers.Take(EdgeLimit)) {
                                sb.AppendLine($"     {c.CallerSymbol} at {c.CallSiteFilePath.Replace('\\', '/')}:{c.CallSiteLine}");
                            }
                        }
                        if (callees.Count > 0) {
                            sb.AppendLine($"   {fqn} -> 被调用方:");
                            foreach (var c in callees.Take(EdgeLimit)) {
                                sb.AppendLine($"     {c.CalleeSymbol} at {c.CallSiteFilePath.Replace('\\', '/')}:{c.CallSiteLine}");
                            }
                        }
                    }
                    if (graphWritten) {
                        sb.AppendLine("   --- End Graph ---");
                    }
                }

                sb.AppendLine();
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText($"Semantic search failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 查找指定项目的项目依赖
    /// </summary>
    /// <param name="project_path">项目文件路径（.csproj）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含项目依赖列表的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexGetProjectDeps, "Find project dependencies of a specified project", "code_index")]
    public async Task<ToolResult> GetProjectDependenciesAsync(
        [McpToolParameter("Project file path (.csproj)")] string project_path,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(project_path)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.ProjectPathCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var deps = await _indexer.ProjectDependencyGraph.GetProjectDependenciesAsync(project_path, cancellationToken).ConfigureAwait(false);

            if (deps.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.ProjectNoDependencies, project_path)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.ProjectDependenciesOf, project_path, deps.Count));
            sb.AppendLine();

            for (var i = 0; i < deps.Count; i++) {
                sb.AppendLine($"{i + 1}. {deps[i].TargetProjectPath}");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FindProjectDependenciesFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 查找依赖于指定项目的所有项目
    /// </summary>
    /// <param name="project_path">项目文件路径（.csproj）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含项目被依赖列表的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexGetProjectDependents, "Find projects that depend on a specified project", "code_index")]
    public async Task<ToolResult> GetProjectDependentsAsync(
        [McpToolParameter("Project file path (.csproj)")] string project_path,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(project_path)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.ProjectPathCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var dependents = await _indexer.ProjectDependencyGraph.GetProjectDependentsAsync(project_path, cancellationToken).ConfigureAwait(false);

            if (dependents.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.NoProjectDependsOn, project_path)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.ProjectDependentsOf, project_path, dependents.Count));
            sb.AppendLine();

            for (var i = 0; i < dependents.Count; i++) {
                sb.AppendLine($"{i + 1}. {dependents[i].SourceProjectPath}");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FindProjectDependentsFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 分析修改指定文件后受影响的项目
    /// </summary>
    /// <param name="file_path">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含受影响项目列表的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexGetAffectedProjects, "Analyze projects affected by modifying a specified file", "code_index")]
    public async Task<ToolResult> GetAffectedProjectsAsync(
        [McpToolParameter("File path")] string file_path,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(file_path)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FilePathCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var projects = await _indexer.ProjectDependencyGraph.GetAffectedProjectsAsync(file_path, cancellationToken).ConfigureAwait(false);

            if (projects.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.ModifyFileNoProjectImpact, file_path)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.AffectedProjectsOfModify, file_path, projects.Count));
            sb.AppendLine();

            for (var i = 0; i < projects.Count; i++) {
                sb.AppendLine($"{i + 1}. {projects[i]}");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.AffectedProjectsAnalysisFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 查找指定项目引用的 NuGet 包
    /// </summary>
    /// <param name="project_path">项目文件路径（.csproj）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含 NuGet 包列表的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexGetProjectNuGets, "Find NuGet packages referenced by a specified project", "code_index")]
    public async Task<ToolResult> GetProjectNuGetPackagesAsync(
        [McpToolParameter("Project file path (.csproj)")] string project_path,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(project_path)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.ProjectPathCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var packages = await _indexer.ProjectDependencyGraph.GetProjectNuGetPackagesAsync(project_path, cancellationToken).ConfigureAwait(false);

            if (packages.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.ProjectNoNuGetPackages, project_path)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.ProjectNuGetPackages, project_path, packages.Count));
            sb.AppendLine();

            for (var i = 0; i < packages.Count; i++) {
                var pkg = packages[i];
                sb.AppendLine($"{i + 1}. {pkg.PackageName}{(pkg.Version is not null ? $" ({pkg.Version})" : "")}");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FindNuGetPackagesFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 查找引用指定 NuGet 包的所有项目
    /// </summary>
    /// <param name="package_name">NuGet 包名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含项目列表的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexGetNuGetProjects, "Find all projects referencing a specified NuGet package", "code_index")]
    public async Task<ToolResult> GetProjectsUsingNuGetPackageAsync(
        [McpToolParameter("NuGet package name")] string package_name,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(package_name)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.PackageNameCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var projects = await _indexer.ProjectDependencyGraph.GetProjectsUsingNuGetPackageAsync(package_name, cancellationToken).ConfigureAwait(false);

            if (projects.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.NoProjectUsingNuGet, package_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.ProjectsUsingNuGet, package_name, projects.Count));
            sb.AppendLine();

            for (var i = 0; i < projects.Count; i++) {
                sb.AppendLine($"{i + 1}. {projects[i]}");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.FindProjectsFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 列出工作区中的所有项目
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含所有项目列表的工具结果</returns>
    [McpTool(CodeToolNameEnumConstants.CodeIndexGetAllProjects, "List all projects in the workspace", "code_index")]
    public async Task<ToolResult> GetAllProjectsAsync(
        CancellationToken cancellationToken = default) {
        try {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var projects = await _indexer.ProjectDependencyGraph.GetAllProjectsAsync(cancellationToken).ConfigureAwait(false);

            if (projects.Count == 0) {
                return ToolResultBuilder.Success().WithText(L.T(StringKey.NoIndexedProjects)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.WorkspaceProjects, projects.Count));
            sb.AppendLine();

            for (var i = 0; i < projects.Count; i++) {
                var project = projects[i];
                sb.AppendLine($"{i + 1}. {project.Name}");
                sb.AppendLine($"   {L.T(StringKey.LabelPath, project.FilePath)}");

                if (!string.IsNullOrEmpty(project.TargetFramework)) {
                    sb.AppendLine($"   {L.T(StringKey.LabelTargetFramework, project.TargetFramework)}");
                }

                if (!string.IsNullOrEmpty(project.OutputType)) {
                    sb.AppendLine($"   {L.T(StringKey.LabelOutputType, project.OutputType)}");
                }

                sb.AppendLine();
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.ListProjectsFailed, ex.Message)).Build();
        }
    }

    private static string FormatSymbolKind(SymbolKind kind) {
        return kind switch {
            SymbolKind.Class => ObjectSymbol.DiamondFilled.ToValue(),
            SymbolKind.Struct => ObjectSymbol.Struct.ToValue(),
            SymbolKind.Interface => ObjectSymbol.ArrowRight.ToValue(),
            SymbolKind.Enum => ObjectSymbol.List.ToValue(),
            SymbolKind.Method => ObjectSymbol.Directory.ToValue(),
            SymbolKind.Property => StatusSymbol.Stop.ToValue(),
            SymbolKind.Field => ObjectSymbol.DiamondFilled.ToValue(),
            SymbolKind.Event => ObjectSymbol.Lightning.ToValue(),
            SymbolKind.Delegate => ObjectSymbol.Directory.ToValue(),
            SymbolKind.Namespace => ObjectSymbol.Directory.ToValue(),
            SymbolKind.Constant => ObjectSymbol.DiamondOpen.ToValue(),
            SymbolKind.Constructor => ObjectSymbol.Directory.ToValue(),
            SymbolKind.Record => ObjectSymbol.Color.ToValue(),
            SymbolKind.RecordStruct => ObjectSymbol.DiamondOpen.ToValue(),
            SymbolKind.Operator => ObjectSymbol.Operator.ToValue(),
            SymbolKind.Indexer => ObjectSymbol.Indexer.ToValue(),
            SymbolKind.Destructor => ObjectSymbol.Destructor.ToValue(),
            SymbolKind.LocalFunction => ObjectSymbol.LocalFunction.ToValue(),
            _ => ObjectSymbol.File.ToValue()
        };
    }

    /// <summary>
    /// 文档检索 — .md 文件语义搜索。
    /// </summary>
    /// <param name="query">搜索查询（自然语言）。</param>
    /// <param name="top_k">最大结果数。</param>
    /// <param name="include_source_text">包含块原文。</param>
    /// <param name="persist_dir">索引目录（默认自动发现 git 工作区）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含文档检索结果的工具结果。</returns>
    [McpTool(CodeToolNameEnumConstants.SearchDocument, "Document search: semantic search over .md files only. Returns matching markdown chunks.", "code_index")]
    public async Task<ToolResult> SearchDocumentAsync(
        [McpToolParameter("Search query (natural language)")] string query,
        [McpToolParameter("Maximum results", Required = false, DefaultValue = "10")] int top_k = 10,
        [McpToolParameter("Include source text in results")] bool include_source_text = false,
        [McpToolParameter("Index directory (default: auto-discover git workspace)")] string? persist_dir = null,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(query)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.QueryCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken, persist_dir).ConfigureAwait(false);
            var options = new SearchOptions {
                IncludeSourceText = include_source_text,
                FileType = "md"
            };
            var results = await _indexer.SearchSemanticAsync(query, top_k, cancellationToken, options).ConfigureAwait(false);
            if (results.Count == 0) {
                return ToolResultBuilder.Success().WithText($"No document matches for: \"{query}\"").Build();
            }
            var sb = FormatVectorResults(results, query, "document", include_source_text);
            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText($"search_document failed: {ex.Message}").Build();
        }
    }

    private async Task AppendGraphRelationsAsync(StringBuilder sb, string fqn, CancellationToken ct) {
        var callers = await _indexer.CallGraph.GetCallersAsync(fqn, ct).ConfigureAwait(false);
        var callees = await _indexer.CallGraph.GetCalleesAsync(fqn, ct).ConfigureAwait(false);
        if (callers.Count == 0 && callees.Count == 0) return;
        sb.AppendLine("   --- Triples ---");
        foreach (var c in callers.Take(3)) {
            sb.AppendLine($"   ({c.CallerSymbol}, calls, {fqn})");
        }
        foreach (var c in callees.Take(3)) {
            sb.AppendLine($"   ({fqn}, calls, {c.CalleeSymbol})");
        }
        sb.AppendLine("   --- End Triples ---");
    }

    private static StringBuilder FormatVectorResults(IReadOnlyList<ChunkSearchResult> results, string query, string mode, bool includeSource) {
        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Count} match(s) for: \"{query}\" (mode={mode})");
        sb.AppendLine();
        for (var i = 0; i < results.Count; i++) {
            var r = results[i];
            sb.AppendLine($"{i + 1}. [{r.Score:F4}] {r.SymbolFqn}");
            sb.AppendLine($"   {r.FilePath.Replace('\\', '/')}:{r.StartLine}-{r.EndLine}");
            if (includeSource && !string.IsNullOrEmpty(r.SourceText)) {
                sb.AppendLine("   --- Source ---");
                foreach (var line in r.SourceText.Split('\n')) {
                    sb.AppendLine($"   {line}");
                }
                sb.AppendLine("   --- End Source ---");
            }
        }
        return sb;
    }

    private Task EnsureLoadedAsync(CancellationToken ct, string? persistDir = null) => _indexer.EnsureIndexLoadedAsync(ct, persistDir);
}