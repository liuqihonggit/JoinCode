
namespace McpToolHandlers;

[McpToolHandler(ToolCategory.CodeIndex, Optional = true)]
public sealed class CodeIndexToolHandlers
{
    private readonly ICodeIndexer _indexer;
    private readonly IProgressiveDisclosure? _disclosure;

    public CodeIndexToolHandlers(ICodeIndexer indexer, IProgressiveDisclosure? disclosure = null)
    {
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _disclosure = disclosure;
    }

    [McpTool(CodeToolNameConstants.CodeIndexSearch, "C# AST symbol search ONLY. Searches indexed C# code symbols (classes, methods, properties, etc.) from parsed .cs files. Do NOT use for config files, docs, JSON, YAML, or non-C# content - use grep/glob instead.", "code_index")]
    public async Task<ToolResult> SearchAsync(
        [McpToolParameter("Search query (supports FTS5 full-text search syntax)")] string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.QueryCannotBeEmpty)).Build();
        }

        try
        {
            var result = await _indexer.Searcher.SearchAsync(query, cancellationToken).ConfigureAwait(false);

            if (result.Items.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoMatchingSymbols, query)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.FoundSymbolsCount, result.TotalCount, result.ElapsedMs));
            sb.AppendLine();

            for (int i = 0; i < Math.Min(result.Items.Count, 30); i++)
            {
                var symbol = result.Items[i];
                sb.AppendLine($"{i + 1}. {FormatSymbolKind(symbol.Kind)} {symbol.Name}");
                sb.AppendLine($"   {L.T(StringKey.LabelLocation, symbol.FilePath, symbol.StartLine)}");

                if (!string.IsNullOrEmpty(symbol.ParentSymbol))
                {
                    sb.AppendLine($"   {L.T(StringKey.LabelParentSymbol, symbol.ParentSymbol)}");
                }

                if (!string.IsNullOrEmpty(symbol.Namespace))
                {
                    sb.AppendLine($"   {L.T(StringKey.LabelNamespace, symbol.Namespace)}");
                }

                sb.AppendLine();
            }

            if (result.Items.Count > 30)
            {
                sb.AppendLine(L.T(StringKey.MoreResults, result.Items.Count - 30));
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.SymbolSearchFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexSearchComprehensive, "C# AST comprehensive search ONLY. Fuzzy match C# symbols then AST-extract references + callers/callees. Do NOT use for config, docs, JSON, YAML, or non-C# content - use grep/glob instead.", "code_index")]
    public async Task<ToolResult> SearchComprehensiveAsync(
        [McpToolParameter("Regex pattern to fuzzy match symbol name/FQN (rg-style, e.g. 'User*' or 'Get.*Name')")] string pattern,
        [McpToolParameter("Max token budget for result (approx 4 chars/token, truncated if exceeded, default 2000)")] int max_token_budget = 2000,
        [McpToolParameter("Include AST expansion (references + callers/callees). Set false to only return symbol matches, saving tokens for non-code queries. Default true.")] bool include_ast = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.QueryCannotBeEmpty)).Build();
        }

        try
        {
            var result = await _indexer.SearchComprehensiveAsync(pattern, max_token_budget, cancellationToken, include_ast).ConfigureAwait(false);

            // 真正无匹配符号 (TotalMatchedCount==0): 返回空结果提示
            // 注意: 不能用 MatchedSymbols.Count==0 判断,因为预算过小会截断到 0,但实际有匹配
            if (result.TotalMatchedCount == 0)
            {
                var emptySb = new System.Text.StringBuilder();
                emptySb.AppendLine(L.T(StringKey.NoMatchingSymbols, pattern));
                emptySb.AppendLine($"耗时: {result.ElapsedMs}ms");
                return McpResultBuilder.Success().WithText(emptySb.ToString()).Build();
            }

            var sb = new System.Text.StringBuilder();
            // 头部摘要: 匹配 N 个符号，引用 M 个，调用方 P 个，被调用方 Q 个
            sb.AppendLine($"综合检索完成: 匹配 {result.MatchedSymbols.Count} 个符号, 引用 {result.References.Count} 个, 调用方 {result.Callers.Count} 个, 被调用方 {result.Callees.Count} 个");
            sb.AppendLine($"预估 token: {result.EstimatedTokens} / {max_token_budget} (预算), 耗时: {result.ElapsedMs}ms");

            // 候选上限提示: 实际匹配数 > 显示数,说明被 100 候选上限截断
            if (result.TotalMatchedCount > result.MatchedSymbols.Count)
            {
                sb.AppendLine($"⚠ 匹配数超过候选上限,实际共 {result.TotalMatchedCount} 个,仅显示前 {result.MatchedSymbols.Count} 个,建议缩小 pattern 范围");
            }

            // 截断提示
            if (result.Truncated)
            {
                sb.AppendLine($"⚠ 结果已截断(达 token 预算上限,已截断 {result.TruncatedCount} 条,优先级: matched > references > callers > callees),建议缩小 pattern 或提高 max_token_budget");
            }
            sb.AppendLine();

            // === 匹配符号 ===
            sb.AppendLine("=== 匹配符号 ===");
            for (int i = 0; i < result.MatchedSymbols.Count; i++)
            {
                var symbol = result.MatchedSymbols[i];
                sb.AppendLine($"{i + 1}. {FormatSymbolKind(symbol.Kind)} {symbol.Name}");
                sb.AppendLine($"   {L.T(StringKey.LabelLocation, symbol.FilePath, symbol.StartLine)}");
                if (!string.IsNullOrEmpty(symbol.ParentSymbol))
                {
                    sb.AppendLine($"   {L.T(StringKey.LabelParentSymbol, symbol.ParentSymbol)}");
                }
            }
            sb.AppendLine();

            // === 引用 ===
            if (result.References.Count > 0)
            {
                sb.AppendLine("=== 引用 ===");
                for (int i = 0; i < result.References.Count; i++)
                {
                    var symbol = result.References[i];
                    sb.AppendLine($"{i + 1}. {FormatSymbolKind(symbol.Kind)} {symbol.Name}");
                    sb.AppendLine($"   {L.T(StringKey.LabelLocation, symbol.FilePath, symbol.StartLine)}");
                }
                sb.AppendLine();
            }

            // === 调用方 ===
            if (result.Callers.Count > 0)
            {
                sb.AppendLine("=== 调用方 ===");
                for (int i = 0; i < result.Callers.Count; i++)
                {
                    var edge = result.Callers[i];
                    sb.AppendLine($"{i + 1}. {edge.CallerSymbol} [{edge.CallKind}]");
                    sb.AppendLine($"   {L.T(StringKey.LabelCallSite, edge.CallSiteFilePath, edge.CallSiteLine)}");
                }
                sb.AppendLine();
            }

            // === 被调用方 ===
            if (result.Callees.Count > 0)
            {
                sb.AppendLine("=== 被调用方 ===");
                for (int i = 0; i < result.Callees.Count; i++)
                {
                    var edge = result.Callees[i];
                    sb.AppendLine($"{i + 1}. {edge.CalleeSymbol} [{edge.CallKind}]");
                    sb.AppendLine($"   {L.T(StringKey.LabelCallSite, edge.CallSiteFilePath, edge.CallSiteLine)}");
                }
                sb.AppendLine();
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"综合检索失败: {ex.Message}").Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexFindDefinition, "Find the definition location of a C# symbol in AST index", "code_index")]
    public async Task<ToolResult> FindDefinitionAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try
        {
            var definition = await _indexer.Searcher.FindDefinitionAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (definition is null)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.SymbolDefinitionNotFound, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.LabelSymbolDefinition, definition.Name));
            sb.AppendLine(L.T(StringKey.SyncLabelType, definition.Kind));
            sb.AppendLine(L.T(StringKey.LabelLocation, definition.FilePath, definition.StartLine));

            if (!string.IsNullOrEmpty(definition.ParentSymbol))
            {
                sb.AppendLine(L.T(StringKey.LabelParentSymbol, definition.ParentSymbol));
            }

            if (!string.IsNullOrEmpty(definition.Namespace))
            {
                sb.AppendLine(L.T(StringKey.LabelNamespace, definition.Namespace));
            }

            if (!string.IsNullOrEmpty(definition.Accessibility))
            {
                sb.AppendLine(L.T(StringKey.LabelAccessModifier, definition.Accessibility));
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FindDefinitionFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexFindReferences, "Find all references to a C# symbol in AST index", "code_index")]
    public async Task<ToolResult> FindReferencesAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try
        {
            var references = await _indexer.Searcher.FindReferencesAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (references.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.SymbolReferencesNotFound, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.FoundReferencesCount, references.Count));
            sb.AppendLine();

            var grouped = references.GroupBy(r => r.FilePath).ToList();

            foreach (var group in grouped)
            {
                sb.AppendLine($"{ObjectSymbol.File.ToValue()} {group.Key}");

                foreach (var symbol in group.OrderBy(s => s.StartLine))
                {
                    sb.AppendLine($"   {L.T(StringKey.LabelLine, symbol.StartLine, FormatSymbolKind(symbol.Kind), symbol.Name)}");
                }

                sb.AppendLine();
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FindReferencesFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexGetCallers, "Find all callers of a specified C# symbol in AST index", "code_index")]
    public async Task<ToolResult> GetCallersAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try
        {
            var callers = await _indexer.CallGraph.GetCallersAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (callers.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.CallersNotFound, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.CallersOfSymbol, symbol_name, callers.Count));
            sb.AppendLine();

            for (int i = 0; i < callers.Count; i++)
            {
                var edge = callers[i];
                sb.AppendLine($"{i + 1}. {edge.CallerSymbol} [{edge.CallKind}]");
                sb.AppendLine($"   {L.T(StringKey.LabelCallSite, edge.CallSiteFilePath, edge.CallSiteLine)}");
                sb.AppendLine();
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FindCallersFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexGetCallees, "Find all callees invoked by a specified C# symbol in AST index", "code_index")]
    public async Task<ToolResult> GetCalleesAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try
        {
            var callees = await _indexer.CallGraph.GetCalleesAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (callees.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.CalleesNotFound, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.CalleesOfSymbol, symbol_name, callees.Count));
            sb.AppendLine();

            for (int i = 0; i < callees.Count; i++)
            {
                var edge = callees[i];
                sb.AppendLine($"{i + 1}. {edge.CalleeSymbol} [{edge.CallKind}]");
                sb.AppendLine($"   {L.T(StringKey.LabelCallSite, edge.CallSiteFilePath, edge.CallSiteLine)}");
                sb.AppendLine();
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FindCalleesFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexGetCallChain, "Find the call chain between two symbols", "code_index")]
    public async Task<ToolResult> GetCallChainAsync(
        [McpToolParameter("Source symbol name")] string from,
        [McpToolParameter("Target symbol name")] string to,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(from))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FromCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(to))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ToCannotBeEmpty)).Build();
        }

        try
        {
            var chain = await _indexer.CallGraph.GetCallChainAsync(from, to, cancellationToken).ConfigureAwait(false);

            if (chain.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.CallChainNotFound, from, to)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.CallChainSteps, from, to, chain.Count));
            sb.AppendLine();

            for (int i = 0; i < chain.Count; i++)
            {
                var edge = chain[i];
                sb.AppendLine($"{i + 1}. {edge.CallerSymbol} → {edge.CalleeSymbol} [{edge.CallKind}]");
                sb.AppendLine($"   {L.T(StringKey.LabelLocation, edge.CallSiteFilePath, edge.CallSiteLine)}");
                sb.AppendLine();
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FindCallChainFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexGetImpactScope, "Analyze the impact scope of modifying a symbol", "code_index")]
    public async Task<ToolResult> GetImpactScopeAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try
        {
            var scope = await _indexer.CallGraph.GetImpactScopeAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (scope.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.ModifyNoImpact, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.ImpactScopeOfSymbol, symbol_name, scope.Count));
            sb.AppendLine();

            for (int i = 0; i < scope.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {scope[i]}");
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ImpactScopeAnalysisFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexGetInheritors, "Find types that inherit or implement a specified symbol", "code_index")]
    public async Task<ToolResult> GetInheritorsAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try
        {
            var inheritors = await _indexer.DependencyGraph.GetInheritorsAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (inheritors.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.InheritorsNotFound, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.InheritorsOfSymbol, symbol_name, inheritors.Count));
            sb.AppendLine();

            for (int i = 0; i < inheritors.Count; i++)
            {
                var edge = inheritors[i];
                sb.AppendLine($"{i + 1}. {edge.SourceSymbol} [{edge.DependencyKind}]");
                sb.AppendLine();
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FindInheritorsFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexGetDependencies, "Find dependencies of a specified symbol", "code_index")]
    public async Task<ToolResult> GetDependenciesAsync(
        [McpToolParameter("Symbol name")] string symbol_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.SymbolNameCannotBeEmpty)).Build();
        }

        try
        {
            var deps = await _indexer.DependencyGraph.GetDependenciesAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            if (deps.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.DependenciesNotFound, symbol_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.DependenciesOfSymbol, symbol_name, deps.Count));
            sb.AppendLine();

            for (int i = 0; i < deps.Count; i++)
            {
                var edge = deps[i];
                sb.AppendLine($"{i + 1}. → {edge.TargetSymbol} [{edge.DependencyKind}]");
                sb.AppendLine();
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FindDependenciesFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexGetAffectedFiles, "Analyze files affected by modifying a specified file", "code_index")]
    public async Task<ToolResult> GetAffectedFilesAsync(
        [McpToolParameter("File path")] string file_path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FilePathCannotBeEmpty)).Build();
        }

        try
        {
            var files = await _indexer.DependencyGraph.GetAffectedFilesAsync(file_path, cancellationToken).ConfigureAwait(false);

            if (files.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.ModifyFileNoImpact, file_path)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.AffectedFilesOfModify, file_path, files.Count));
            sb.AppendLine();

            for (int i = 0; i < files.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {files[i]}");
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.AffectedFilesAnalysisFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexRebuild, "Rebuild the code index", "code_index")]
    public async Task<ToolResult> RebuildAsync(
        [McpToolParameter("Workspace root directory path")] string workspace_root,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspace_root))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.WorkspaceRootCannotBeEmpty)).Build();
        }

        try
        {
            var options = new CodeIndexOptions { WorkspaceRoot = workspace_root };
            var result = await _indexer.BuildIndexAsync(options, cancellationToken).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.IndexRebuildComplete));
            sb.AppendLine(L.T(StringKey.UpdatedFiles, result.UpdatedCount));
            sb.AppendLine(L.T(StringKey.SkippedFiles, result.SkippedCount));
            sb.AppendLine(L.T(StringKey.DeletedFiles, result.DeletedCount));

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.IndexRebuildFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexStats, "Get code index statistics", "code_index")]
    public async Task<ToolResult> GetStatsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = await _indexer.GetStatsAsync(cancellationToken).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.CodeIndexStats));
            sb.AppendLine(L.T(StringKey.StatsFileCount, stats.FileCount));
            sb.AppendLine(L.T(StringKey.StatsSymbolCount, stats.SymbolCount));
            sb.AppendLine(L.T(StringKey.StatsCallEdgeCount, stats.CallEdgeCount));
            sb.AppendLine(L.T(StringKey.StatsDependencyEdgeCount, stats.DependencyEdgeCount));
            sb.AppendLine(L.T(StringKey.StatsProjectCount, stats.ProjectCount));
            sb.AppendLine(L.T(StringKey.StatsLastUpdated, stats.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss")));

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.GetStatsFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexExplore, "Progressively explore C# code: symbol index -> call relationships -> source code. AST index only.", "code_index")]
    public async Task<ToolResult> ExploreAsync(
        [McpToolParameter("Search query (symbol name or keyword)")] string query,
        [McpToolParameter("Disclosure level: index=symbol index only, relationships=with call graph, source=with source code")] string level = "index",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.QueryCannotBeEmpty)).Build();
        }

        if (_disclosure is null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ProgressiveDisclosureNotEnabled)).Build();
        }

        var disclosureLevel = level.ToLowerInvariant() switch
        {
            "index" => DisclosureLevel.Index,
            "relationships" => DisclosureLevel.Relationships,
            "source" => DisclosureLevel.Source,
            _ => DisclosureLevel.Index
        };

        try
        {
            var result = await _disclosure.DiscloseAsync(query, disclosureLevel, cancellationToken).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(result.FormattedContent);
            sb.AppendLine();
            sb.AppendLine($"---");
            sb.AppendLine(L.T(StringKey.DisclosureLevelInfo, result.Level, result.EstimatedTokens));

            if (result.HasMoreDetails)
            {
                sb.AppendLine(L.T(StringKey.NeedMoreInfoHint, ObjectSymbol.DiamondFilled.ToValue(), result.Level == DisclosureLevel.Index ? "relationships" : "source"));
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ProgressiveExploreFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexGetProjectDeps, "Find project dependencies of a specified project", "code_index")]
    public async Task<ToolResult> GetProjectDependenciesAsync(
        [McpToolParameter("Project file path (.csproj)")] string project_path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(project_path))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ProjectPathCannotBeEmpty)).Build();
        }

        try
        {
            var deps = await _indexer.ProjectDependencyGraph.GetProjectDependenciesAsync(project_path, cancellationToken).ConfigureAwait(false);

            if (deps.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.ProjectNoDependencies, project_path)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.ProjectDependenciesOf, project_path, deps.Count));
            sb.AppendLine();

            for (int i = 0; i < deps.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {deps[i].TargetProjectPath}");
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FindProjectDependenciesFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexGetProjectDependents, "Find projects that depend on a specified project", "code_index")]
    public async Task<ToolResult> GetProjectDependentsAsync(
        [McpToolParameter("Project file path (.csproj)")] string project_path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(project_path))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ProjectPathCannotBeEmpty)).Build();
        }

        try
        {
            var dependents = await _indexer.ProjectDependencyGraph.GetProjectDependentsAsync(project_path, cancellationToken).ConfigureAwait(false);

            if (dependents.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoProjectDependsOn, project_path)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.ProjectDependentsOf, project_path, dependents.Count));
            sb.AppendLine();

            for (int i = 0; i < dependents.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {dependents[i].SourceProjectPath}");
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FindProjectDependentsFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexGetAffectedProjects, "Analyze projects affected by modifying a specified file", "code_index")]
    public async Task<ToolResult> GetAffectedProjectsAsync(
        [McpToolParameter("File path")] string file_path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(file_path))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FilePathCannotBeEmpty)).Build();
        }

        try
        {
            var projects = await _indexer.ProjectDependencyGraph.GetAffectedProjectsAsync(file_path, cancellationToken).ConfigureAwait(false);

            if (projects.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.ModifyFileNoProjectImpact, file_path)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.AffectedProjectsOfModify, file_path, projects.Count));
            sb.AppendLine();

            for (int i = 0; i < projects.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {projects[i]}");
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.AffectedProjectsAnalysisFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexGetProjectNuGets, "Find NuGet packages referenced by a specified project", "code_index")]
    public async Task<ToolResult> GetProjectNuGetPackagesAsync(
        [McpToolParameter("Project file path (.csproj)")] string project_path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(project_path))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ProjectPathCannotBeEmpty)).Build();
        }

        try
        {
            var packages = await _indexer.ProjectDependencyGraph.GetProjectNuGetPackagesAsync(project_path, cancellationToken).ConfigureAwait(false);

            if (packages.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.ProjectNoNuGetPackages, project_path)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.ProjectNuGetPackages, project_path, packages.Count));
            sb.AppendLine();

            for (int i = 0; i < packages.Count; i++)
            {
                var pkg = packages[i];
                sb.AppendLine($"{i + 1}. {pkg.PackageName}{(pkg.Version is not null ? $" ({pkg.Version})" : "")}");
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FindNuGetPackagesFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexGetNuGetProjects, "Find all projects referencing a specified NuGet package", "code_index")]
    public async Task<ToolResult> GetProjectsUsingNuGetPackageAsync(
        [McpToolParameter("NuGet package name")] string package_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(package_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.PackageNameCannotBeEmpty)).Build();
        }

        try
        {
            var projects = await _indexer.ProjectDependencyGraph.GetProjectsUsingNuGetPackageAsync(package_name, cancellationToken).ConfigureAwait(false);

            if (projects.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoProjectUsingNuGet, package_name)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.ProjectsUsingNuGet, package_name, projects.Count));
            sb.AppendLine();

            for (int i = 0; i < projects.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {projects[i]}");
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.FindProjectsFailed, ex.Message)).Build();
        }
    }

    [McpTool(CodeToolNameConstants.CodeIndexGetAllProjects, "List all projects in the workspace", "code_index")]
    public async Task<ToolResult> GetAllProjectsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projects = await _indexer.ProjectDependencyGraph.GetAllProjectsAsync(cancellationToken).ConfigureAwait(false);

            if (projects.Count == 0)
            {
                return McpResultBuilder.Success().WithText(L.T(StringKey.NoIndexedProjects)).Build();
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(L.T(StringKey.WorkspaceProjects, projects.Count));
            sb.AppendLine();

            for (int i = 0; i < projects.Count; i++)
            {
                var project = projects[i];
                sb.AppendLine($"{i + 1}. {project.Name}");
                sb.AppendLine($"   {L.T(StringKey.LabelPath, project.FilePath)}");

                if (!string.IsNullOrEmpty(project.TargetFramework))
                {
                    sb.AppendLine($"   {L.T(StringKey.LabelTargetFramework, project.TargetFramework)}");
                }

                if (!string.IsNullOrEmpty(project.OutputType))
                {
                    sb.AppendLine($"   {L.T(StringKey.LabelOutputType, project.OutputType)}");
                }

                sb.AppendLine();
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ListProjectsFailed, ex.Message)).Build();
        }
    }

    private static string FormatSymbolKind(SymbolKind kind)
    {
        return kind switch
        {
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
}
