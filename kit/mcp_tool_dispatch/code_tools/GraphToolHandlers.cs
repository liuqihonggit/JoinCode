namespace McpToolDispatch;

/// <summary>
/// 图分析工具处理器 - 提供社区检测、枢纽节点分析、死代码检测、变更影响分析、子图提取等图分析功能
/// </summary>
[McpToolDispatch(ToolCategory.Graph, Optional = true)]
public sealed class GraphToolHandlers
{
    private readonly ICodeIndexer _indexer;
    private readonly ICodeIndexerRegistry? _registry;

    /// <summary>
    /// 初始化 <see cref="GraphToolHandlers"/> 实例
    /// </summary>
    /// <param name="indexer">代码索引器</param>
    /// <param name="registry">多仓库索引器注册表（可选）</param>
    public GraphToolHandlers(ICodeIndexer indexer, ICodeIndexerRegistry? registry = null)
    {
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
        _registry = registry;
    }

    private async Task<ICodeIndexer> ResolveIndexerAsync(string? repoId, CancellationToken ct)
    {
        ICodeIndexer indexer;
        if (string.IsNullOrWhiteSpace(repoId) || repoId == "default")
            indexer = _indexer;
        else
        {
            if (_registry is null)
                throw new InvalidOperationException("Multi-repo is not available (no ICodeIndexerRegistry registered).");
            indexer = _registry.GetIndexer(repoId) ?? throw new InvalidOperationException($"Repository '{repoId}' is not registered. Use graph_register first.");
        }
        await indexer.EnsureIndexLoadedAsync(ct).ConfigureAwait(false);
        return indexer;
    }

    /// <summary>
    /// 使用标签传播算法在调用图上检测代码社区（模块/子系统）
    /// </summary>
    /// <param name="repo_id">仓库 ID（默认为 default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含检测到的社区列表的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphDetectCommunities, "Detect code communities (modules/subsystems) using label propagation algorithm on the call graph", "graph")]
    public async Task<ToolResult> DetectCommunitiesAsync(
        [McpToolParameter("Repository ID (default: default)")] string? repo_id = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexer = await ResolveIndexerAsync(repo_id, cancellationToken).ConfigureAwait(false);
            var communities = await indexer.Analytics.DetectCommunitiesAsync(cancellationToken).ConfigureAwait(false);

            if (communities.Count == 0)
                return ToolResultBuilder.Success().WithText("No communities detected (index may be empty).").Build();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Detected {communities.Count} communities:");
            sb.AppendLine();

            for (int i = 0; i < communities.Count; i++)
            {
                var c = communities[i];
                sb.AppendLine($"Community {c.CommunityId}: {c.MemberCount} members, {c.InternalEdges} internal edges, {c.ExternalEdges} external edges");
                var preview = c.Members.Take(5);
                foreach (var m in preview)
                    sb.AppendLine($"  - {m}");
                if (c.MemberCount > 5)
                    sb.AppendLine($"  ... and {c.MemberCount - 5} more");
                sb.AppendLine();
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"Community detection failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 查找调用图中连接度最高的枢纽节点
    /// </summary>
    /// <param name="top_n">返回的枢纽节点数量</param>
    /// <param name="repo_id">仓库 ID（默认为 default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含枢纽节点列表的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphGetHubNodes, "Find hub nodes with highest connectivity (in-degree + out-degree) in the call graph", "graph")]
    public async Task<ToolResult> GetHubNodesAsync(
        [McpToolParameter("Number of top hub nodes to return (default 10)")] int top_n = 10,
        [McpToolParameter("Repository ID (default: default)")] string? repo_id = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexer = await ResolveIndexerAsync(repo_id, cancellationToken).ConfigureAwait(false);
            var hubs = await indexer.Analytics.GetHubNodesAsync(top_n, cancellationToken).ConfigureAwait(false);

            if (hubs.Count == 0)
                return ToolResultBuilder.Success().WithText("No hub nodes found (index may be empty).").Build();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Top {hubs.Count} hub nodes:");
            sb.AppendLine();

            for (int i = 0; i < hubs.Count; i++)
            {
                var h = hubs[i];
                sb.AppendLine($"{i + 1}. {h.SymbolName} (in={h.InDegree}, out={h.OutDegree}, total={h.TotalDegree})");
                if (!string.IsNullOrEmpty(h.FilePath))
                    sb.AppendLine($"   {h.FilePath}");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"Hub analysis failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 检测潜在死代码：无调用方且非入口点的方法
    /// </summary>
    /// <param name="repo_id">仓库 ID（默认为 default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含潜在死代码列表的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphDetectDeadCode, "Detect potentially dead code: methods with no callers that are not entry points", "graph")]
    public async Task<ToolResult> DetectDeadCodeAsync(
        [McpToolParameter("Repository ID (default: default)")] string? repo_id = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexer = await ResolveIndexerAsync(repo_id, cancellationToken).ConfigureAwait(false);
            var dead = await indexer.Analytics.DetectDeadCodeAsync(cancellationToken).ConfigureAwait(false);

            if (dead.Count == 0)
                return ToolResultBuilder.Success().WithText("No dead code detected.").Build();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Detected {dead.Count} potentially dead code entries:");
            sb.AppendLine();

            var grouped = dead.GroupBy(d => d.FilePath).ToList();
            foreach (var group in grouped)
            {
                sb.AppendLine($"File: {group.Key}");
                foreach (var entry in group.OrderBy(e => e.Line))
                    sb.AppendLine($"  Line {entry.Line}: {entry.SymbolName} ({entry.Reason})");
                sb.AppendLine();
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"Dead code detection failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 提取以指定符号为中心、N 跳半径的子图
    /// </summary>
    /// <param name="center_symbol">中心符号名称</param>
    /// <param name="hops">跳数（半径）</param>
    /// <param name="repo_id">仓库 ID（默认为 default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含子图节点和边的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphExtractSubgraph, "Extract a subgraph centered on a symbol with N hops radius", "graph")]
    public async Task<ToolResult> ExtractSubgraphAsync(
        [McpToolParameter("Center symbol name")] string center_symbol,
        [McpToolParameter("Number of hops (radius, default 2)")] int hops = 2,
        [McpToolParameter("Repository ID (default: default)")] string? repo_id = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(center_symbol))
            return ToolResultBuilder.Error().WithText("center_symbol cannot be empty.").Build();

        try
        {
            var indexer = await ResolveIndexerAsync(repo_id, cancellationToken).ConfigureAwait(false);
            var result = await indexer.Analytics.ExtractSubgraphAsync(center_symbol, hops, cancellationToken).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Subgraph centered on '{result.CenterSymbol}' ({result.Hops} hops): {result.Nodes.Count} nodes, {result.Edges.Count} edges");
            sb.AppendLine();

            sb.AppendLine("Nodes:");
            foreach (var node in result.Nodes)
                sb.AppendLine($"  - {node}");
            sb.AppendLine();

            sb.AppendLine("Edges:");
            foreach (var edge in result.Edges)
                sb.AppendLine($"  {edge.CallerSymbol} -> {edge.CalleeSymbol} [{edge.CallKind}]");

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"Subgraph extraction failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 分析指定文件变更的影响范围（爆炸半径）
    /// </summary>
    /// <param name="changed_files">变更文件路径的逗号分隔列表</param>
    /// <param name="repo_id">仓库 ID（默认为 default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含受影响符号、文件和项目列表的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphAnalyzeChangeImpact, "Analyze the blast radius of changes to specified files", "graph")]
    public async Task<ToolResult> AnalyzeChangeImpactAsync(
        [McpToolParameter("Comma-separated list of changed file paths")] string changed_files,
        [McpToolParameter("Repository ID (default: default)")] string? repo_id = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(changed_files))
            return ToolResultBuilder.Error().WithText("changed_files cannot be empty.").Build();

        try
        {
            var indexer = await ResolveIndexerAsync(repo_id, cancellationToken).ConfigureAwait(false);
            var files = changed_files.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = await indexer.Analytics.AnalyzeChangeImpactAsync(files, cancellationToken).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Change impact analysis for {result.ChangedFiles.Count} file(s):");
            sb.AppendLine($"  Affected symbols: {result.AffectedSymbols.Count}");
            sb.AppendLine($"  Affected files: {result.AffectedFiles.Count}");
            sb.AppendLine($"  Affected projects: {result.AffectedProjects.Count}");
            sb.AppendLine();

            if (result.AffectedSymbols.Count > 0)
            {
                sb.AppendLine("Affected symbols:");
                foreach (var sym in result.AffectedSymbols.Take(30))
                    sb.AppendLine($"  - {sym}");
                if (result.AffectedSymbols.Count > 30)
                    sb.AppendLine($"  ... and {result.AffectedSymbols.Count - 30} more");
                sb.AppendLine();
            }

            if (result.AffectedProjects.Count > 0)
            {
                sb.AppendLine("Affected projects:");
                foreach (var proj in result.AffectedProjects)
                    sb.AppendLine($"  - {proj}");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"Change impact analysis failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 将代码索引保存到磁盘，以便下次启动时快速重载
    /// </summary>
    /// <param name="directory">保存索引的目录路径</param>
    /// <param name="repo_id">仓库 ID（默认为 default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含保存结果的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphSave, "Save the code index to disk for fast reload on next startup", "graph")]
    public async Task<ToolResult> SaveAsync(
        [McpToolParameter("Directory path to save the index (default: .jcc/graph)")] string directory = ".jcc/graph",
        [McpToolParameter("Repository ID (default: default)")] string? repo_id = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexer = await ResolveIndexerAsync(repo_id, cancellationToken).ConfigureAwait(false);
            await indexer.Persistence.SaveAsync(directory, cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText($"Index saved to {directory}").Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"Save failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 从磁盘加载之前保存的代码索引
    /// </summary>
    /// <param name="directory">加载索引的目录路径</param>
    /// <param name="repo_id">仓库 ID（默认为 default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含加载结果的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphLoad, "Load a previously saved code index from disk", "graph")]
    public async Task<ToolResult> LoadAsync(
        [McpToolParameter("Directory path to load the index from (default: .jcc/graph)")] string directory = ".jcc/graph",
        [McpToolParameter("Repository ID (default: default)")] string? repo_id = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexer = await ResolveIndexerAsync(repo_id, cancellationToken).ConfigureAwait(false);
            var loaded = await indexer.Persistence.LoadAsync(directory, cancellationToken).ConfigureAwait(false);
            return loaded
                ? ToolResultBuilder.Success().WithText($"Index loaded from {directory}").Build()
                : ToolResultBuilder.Error().WithText($"No valid index found at {directory}").Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"Load failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 将调用图导出为 DOT 格式，用于 Graphviz 可视化
    /// </summary>
    /// <param name="repo_id">仓库 ID（默认为 default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含 DOT 格式内容的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphExportDot, "Export the call graph as DOT format for Graphviz visualization", "graph")]
    public async Task<ToolResult> ExportDotAsync(
        [McpToolParameter("Repository ID (default: default)")] string? repo_id = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexer = await ResolveIndexerAsync(repo_id, cancellationToken).ConfigureAwait(false);
            var dot = await indexer.Visualization.ExportDotAsync(cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(dot).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"DOT export failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 将调用图导出为带 D3.js 力导向布局的交互式 HTML
    /// </summary>
    /// <param name="repo_id">仓库 ID（默认为 default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含 HTML 内容的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphExportHtml, "Export the call graph as interactive HTML with D3.js force-directed layout", "graph")]
    public async Task<ToolResult> ExportHtmlAsync(
        [McpToolParameter("Repository ID (default: default)")] string? repo_id = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexer = await ResolveIndexerAsync(repo_id, cancellationToken).ConfigureAwait(false);
            var html = await indexer.Visualization.ExportHtmlAsync(cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(html).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"HTML export failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 基于社区结构将代码架构导出为 Markdown wiki
    /// </summary>
    /// <param name="repo_id">仓库 ID（默认为 default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含 wiki 内容的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphExportWiki, "Export code architecture as Markdown wiki based on community structure", "graph")]
    public async Task<ToolResult> ExportWikiAsync(
        [McpToolParameter("Repository ID (default: default)")] string? repo_id = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var indexer = await ResolveIndexerAsync(repo_id, cancellationToken).ConfigureAwait(false);
            var wiki = await indexer.Visualization.ExportWikiAsync(cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText(wiki).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"Wiki export failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 使用自然语言查询代码图，查找相关符号和子图摘要
    /// </summary>
    /// <param name="query">自然语言查询</param>
    /// <param name="max_results">最大结果数</param>
    /// <param name="repo_id">仓库 ID（默认为 default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含匹配符号列表的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphQuery, "Query the code graph with natural language to find related symbols and subgraph summaries", "graph")]
    public async Task<ToolResult> QueryAsync(
        [McpToolParameter("Natural language query (e.g. 'how does auth work')")] string query,
        [McpToolParameter("Maximum number of results (default 20)")] int max_results = 20,
        [McpToolParameter("Repository ID (default: default)")] string? repo_id = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return ToolResultBuilder.Error().WithText("query cannot be empty.").Build();

        try
        {
            var indexer = await ResolveIndexerAsync(repo_id, cancellationToken).ConfigureAwait(false);
            var result = await indexer.Analytics.QueryAsync(query, max_results, cancellationToken).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Query: \"{result.Query}\" — {result.TotalMatches} total matches, showing {result.Matches.Count}:");
            sb.AppendLine();

            for (int i = 0; i < result.Matches.Count; i++)
            {
                var m = result.Matches[i];
                sb.AppendLine($"{i + 1}. {m.SymbolName} [{m.Kind}] (score={m.RelevanceScore})");
                sb.AppendLine($"   {m.FilePath}");
                if (m.RelatedSymbols.Count > 0)
                    sb.AppendLine($"   Related: {string.Join(", ", m.RelatedSymbols.Take(5))}");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"Graph query failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 查找调用图中两个符号之间的最短路径
    /// </summary>
    /// <param name="from_symbol">起始符号名称</param>
    /// <param name="to_symbol">目标符号名称</param>
    /// <param name="repo_id">仓库 ID（默认为 default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含路径节点和边的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphPath, "Find the shortest path between two symbols in the call graph", "graph")]
    public async Task<ToolResult> FindPathAsync(
        [McpToolParameter("Starting symbol name")] string from_symbol,
        [McpToolParameter("Target symbol name")] string to_symbol,
        [McpToolParameter("Repository ID (default: default)")] string? repo_id = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(from_symbol))
            return ToolResultBuilder.Error().WithText("from_symbol cannot be empty.").Build();
        if (string.IsNullOrWhiteSpace(to_symbol))
            return ToolResultBuilder.Error().WithText("to_symbol cannot be empty.").Build();

        try
        {
            var indexer = await ResolveIndexerAsync(repo_id, cancellationToken).ConfigureAwait(false);
            var result = await indexer.Analytics.FindPathAsync(from_symbol, to_symbol, cancellationToken).ConfigureAwait(false);

            if (!result.PathFound)
                return ToolResultBuilder.Success().WithText($"No path found from '{result.FromSymbol}' to '{result.ToSymbol}'.").Build();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Path from '{result.FromSymbol}' to '{result.ToSymbol}' (length={result.PathLength}):");
            sb.AppendLine();

            for (int i = 0; i < result.PathNodes.Count; i++)
            {
                sb.AppendLine($"  {i}: {result.PathNodes[i]}");
                if (i < result.PathEdges.Count)
                    sb.AppendLine($"     └─[{result.PathEdges[i].CallKind}]→");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"Path search failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 解释符号在代码库中的角色和关系（调用方、被调用方、社区、同文件符号）
    /// </summary>
    /// <param name="symbol_name">要解释的符号名称</param>
    /// <param name="repo_id">仓库 ID（默认为 default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含符号角色和关系信息的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphExplain, "Explain a symbol's role and relationships in the codebase (callers, callees, community, same-file)", "graph")]
    public async Task<ToolResult> ExplainAsync(
        [McpToolParameter("Symbol name to explain")] string symbol_name,
        [McpToolParameter("Repository ID (default: default)")] string? repo_id = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol_name))
            return ToolResultBuilder.Error().WithText("symbol_name cannot be empty.").Build();

        try
        {
            var indexer = await ResolveIndexerAsync(repo_id, cancellationToken).ConfigureAwait(false);
            var result = await indexer.Analytics.ExplainAsync(symbol_name, cancellationToken).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Symbol: {result.SymbolName}");
            sb.AppendLine($"Kind:   {result.Kind}");
            sb.AppendLine($"File:   {result.FilePath}");
            if (result.Namespace is not null)
                sb.AppendLine($"NS:     {result.Namespace}");
            sb.AppendLine($"Degree: in={result.InDegree}, out={result.OutDegree}");
            sb.AppendLine();

            if (result.Callers.Count > 0)
            {
                sb.AppendLine($"Callers ({result.Callers.Count}):");
                foreach (var c in result.Callers.Take(15))
                    sb.AppendLine($"  ← {c}");
                if (result.Callers.Count > 15)
                    sb.AppendLine($"  ... and {result.Callers.Count - 15} more");
                sb.AppendLine();
            }

            if (result.Callees.Count > 0)
            {
                sb.AppendLine($"Callees ({result.Callees.Count}):");
                foreach (var c in result.Callees.Take(15))
                    sb.AppendLine($"  → {c}");
                if (result.Callees.Count > 15)
                    sb.AppendLine($"  ... and {result.Callees.Count - 15} more");
                sb.AppendLine();
            }

            if (result.SameCommunity.Count > 0)
            {
                sb.AppendLine($"Same community ({result.SameCommunity.Count}):");
                foreach (var c in result.SameCommunity.Take(10))
                    sb.AppendLine($"  ~ {c}");
                if (result.SameCommunity.Count > 10)
                    sb.AppendLine($"  ... and {result.SameCommunity.Count - 10} more");
                sb.AppendLine();
            }

            if (result.SameFile.Count > 0)
            {
                sb.AppendLine($"Same file ({result.SameFile.Count}):");
                foreach (var c in result.SameFile.Take(10))
                    sb.AppendLine($"  # {c}");
                if (result.SameFile.Count > 10)
                    sb.AppendLine($"  ... and {result.SameFile.Count - 10} more");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"Explain failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 注册仓库以进行多仓库图分析
    /// </summary>
    /// <param name="repo_id">仓库标识符</param>
    /// <param name="workspace_root">仓库的工作区根路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含注册结果的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphRegister, "Register a repository for multi-repo graph analysis", "graph")]
    public async Task<ToolResult> RegisterRepoAsync(
        [McpToolParameter("Repository identifier (e.g. 'frontend', 'backend')")] string repo_id,
        [McpToolParameter("Workspace root path of the repository")] string workspace_root,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repo_id))
            return ToolResultBuilder.Error().WithText("repo_id cannot be empty.").Build();
        if (string.IsNullOrWhiteSpace(workspace_root))
            return ToolResultBuilder.Error().WithText("workspace_root cannot be empty.").Build();

        if (_registry is null)
            return ToolResultBuilder.Error().WithText("Multi-repo registry is not available.").Build();

        try
        {
            var reg = await _registry.RegisterAsync(repo_id, workspace_root, cancellationToken).ConfigureAwait(false);
            return ToolResultBuilder.Success().WithText($"Repository '{reg.RepoId}' registered (root: {reg.WorkspaceRoot}).").Build();
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultBuilder.Error().WithText(ex.Message).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"Register failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 从多仓库图分析中注销指定仓库
    /// </summary>
    /// <param name="repo_id">要移除的仓库标识符</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含注销结果的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphUnregister, "Unregister a repository from multi-repo graph analysis", "graph")]
    public async Task<ToolResult> UnregisterRepoAsync(
        [McpToolParameter("Repository identifier to remove")] string repo_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repo_id))
            return ToolResultBuilder.Error().WithText("repo_id cannot be empty.").Build();

        if (_registry is null)
            return ToolResultBuilder.Error().WithText("Multi-repo registry is not available.").Build();

        try
        {
            var removed = await _registry.UnregisterAsync(repo_id, cancellationToken).ConfigureAwait(false);
            return removed
                ? ToolResultBuilder.Success().WithText($"Repository '{repo_id}' unregistered.").Build()
                : ToolResultBuilder.Error().WithText($"Repository '{repo_id}' not found.").Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"Unregister failed: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 列出多仓库图分析中所有已注册的仓库
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>包含已注册仓库列表的工具结果</returns>
    [McpTool(CodeToolNameConstants.GraphRepos, "List all registered repositories for multi-repo graph analysis", "graph")]
    public async Task<ToolResult> ListReposAsync(
        CancellationToken cancellationToken = default)
    {
        if (_registry is null)
            return ToolResultBuilder.Success().WithText("Multi-repo not available. Only default repository is in use.").Build();

        try
        {
            var repos = await _registry.ListReposAsync(cancellationToken).ConfigureAwait(false);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Registered repositories ({repos.Count}):");
            sb.AppendLine();

            foreach (var repo in repos)
            {
                var marker = repo.IsDefault ? " (default)" : "";
                sb.AppendLine($"  {repo.RepoId}{marker}: {repo.WorkspaceRoot}");
            }

            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"List repos failed: {ex.Message}").Build();
        }
    }
}
