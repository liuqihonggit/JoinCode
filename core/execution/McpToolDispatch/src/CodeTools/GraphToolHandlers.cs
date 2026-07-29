namespace McpToolDispatch;

[McpToolDispatch(ToolCategory.Graph, Optional = true)]
public sealed class GraphToolHandlers
{
    private readonly ICodeIndexer _indexer;

    public GraphToolHandlers(ICodeIndexer indexer)
    {
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
    }

    [McpTool(CodeToolNameConstants.GraphDetectCommunities, "Detect code communities (modules/subsystems) using label propagation algorithm on the call graph", "graph")]
    public async Task<ToolResult> DetectCommunitiesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var communities = await _indexer.Analytics.DetectCommunitiesAsync(cancellationToken).ConfigureAwait(false);

            if (communities.Count == 0)
                return McpResultBuilder.Success().WithText("No communities detected (index may be empty).").Build();

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

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"Community detection failed: {ex.Message}").Build();
        }
    }

    [McpTool(CodeToolNameConstants.GraphGetHubNodes, "Find hub nodes with highest connectivity (in-degree + out-degree) in the call graph", "graph")]
    public async Task<ToolResult> GetHubNodesAsync(
        [McpToolParameter("Number of top hub nodes to return (default 10)")] int top_n = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var hubs = await _indexer.Analytics.GetHubNodesAsync(top_n, cancellationToken).ConfigureAwait(false);

            if (hubs.Count == 0)
                return McpResultBuilder.Success().WithText("No hub nodes found (index may be empty).").Build();

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

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"Hub analysis failed: {ex.Message}").Build();
        }
    }

    [McpTool(CodeToolNameConstants.GraphDetectDeadCode, "Detect potentially dead code: methods with no callers that are not entry points", "graph")]
    public async Task<ToolResult> DetectDeadCodeAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var dead = await _indexer.Analytics.DetectDeadCodeAsync(cancellationToken).ConfigureAwait(false);

            if (dead.Count == 0)
                return McpResultBuilder.Success().WithText("No dead code detected.").Build();

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

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"Dead code detection failed: {ex.Message}").Build();
        }
    }

    [McpTool(CodeToolNameConstants.GraphExtractSubgraph, "Extract a subgraph centered on a symbol with N hops radius", "graph")]
    public async Task<ToolResult> ExtractSubgraphAsync(
        [McpToolParameter("Center symbol name")] string center_symbol,
        [McpToolParameter("Number of hops (radius, default 2)")] int hops = 2,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(center_symbol))
            return McpResultBuilder.Error().WithText("center_symbol cannot be empty.").Build();

        try
        {
            var result = await _indexer.Analytics.ExtractSubgraphAsync(center_symbol, hops, cancellationToken).ConfigureAwait(false);

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

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"Subgraph extraction failed: {ex.Message}").Build();
        }
    }

    [McpTool(CodeToolNameConstants.GraphAnalyzeChangeImpact, "Analyze the blast radius of changes to specified files", "graph")]
    public async Task<ToolResult> AnalyzeChangeImpactAsync(
        [McpToolParameter("Comma-separated list of changed file paths")] string changed_files,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(changed_files))
            return McpResultBuilder.Error().WithText("changed_files cannot be empty.").Build();

        try
        {
            var files = changed_files.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = await _indexer.Analytics.AnalyzeChangeImpactAsync(files, cancellationToken).ConfigureAwait(false);

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

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"Change impact analysis failed: {ex.Message}").Build();
        }
    }

    [McpTool(CodeToolNameConstants.GraphSave, "Save the code index to disk for fast reload on next startup", "graph")]
    public async Task<ToolResult> SaveAsync(
        [McpToolParameter("Directory path to save the index (default: .jcc/graph)")] string directory = ".jcc/graph",
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _indexer.Persistence.SaveAsync(directory, cancellationToken).ConfigureAwait(false);
            return McpResultBuilder.Success().WithText($"Index saved to {directory}").Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"Save failed: {ex.Message}").Build();
        }
    }

    [McpTool(CodeToolNameConstants.GraphLoad, "Load a previously saved code index from disk", "graph")]
    public async Task<ToolResult> LoadAsync(
        [McpToolParameter("Directory path to load the index from (default: .jcc/graph)")] string directory = ".jcc/graph",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var loaded = await _indexer.Persistence.LoadAsync(directory, cancellationToken).ConfigureAwait(false);
            return loaded
                ? McpResultBuilder.Success().WithText($"Index loaded from {directory}").Build()
                : McpResultBuilder.Error().WithText($"No valid index found at {directory}").Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"Load failed: {ex.Message}").Build();
        }
    }

    [McpTool(CodeToolNameConstants.GraphExportDot, "Export the call graph as DOT format for Graphviz visualization", "graph")]
    public async Task<ToolResult> ExportDotAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var dot = await _indexer.Visualization.ExportDotAsync(cancellationToken).ConfigureAwait(false);
            return McpResultBuilder.Success().WithText(dot).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"DOT export failed: {ex.Message}").Build();
        }
    }

    [McpTool(CodeToolNameConstants.GraphExportHtml, "Export the call graph as interactive HTML with D3.js force-directed layout", "graph")]
    public async Task<ToolResult> ExportHtmlAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var html = await _indexer.Visualization.ExportHtmlAsync(cancellationToken).ConfigureAwait(false);
            return McpResultBuilder.Success().WithText(html).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"HTML export failed: {ex.Message}").Build();
        }
    }

    [McpTool(CodeToolNameConstants.GraphQuery, "Query the code graph with natural language to find related symbols and subgraph summaries", "graph")]
    public async Task<ToolResult> QueryAsync(
        [McpToolParameter("Natural language query (e.g. 'how does auth work')")] string query,
        [McpToolParameter("Maximum number of results (default 20)")] int max_results = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return McpResultBuilder.Error().WithText("query cannot be empty.").Build();

        try
        {
            var result = await _indexer.Analytics.QueryAsync(query, max_results, cancellationToken).ConfigureAwait(false);

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

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"Graph query failed: {ex.Message}").Build();
        }
    }

    [McpTool(CodeToolNameConstants.GraphPath, "Find the shortest path between two symbols in the call graph", "graph")]
    public async Task<ToolResult> FindPathAsync(
        [McpToolParameter("Starting symbol name")] string from_symbol,
        [McpToolParameter("Target symbol name")] string to_symbol,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(from_symbol))
            return McpResultBuilder.Error().WithText("from_symbol cannot be empty.").Build();
        if (string.IsNullOrWhiteSpace(to_symbol))
            return McpResultBuilder.Error().WithText("to_symbol cannot be empty.").Build();

        try
        {
            var result = await _indexer.Analytics.FindPathAsync(from_symbol, to_symbol, cancellationToken).ConfigureAwait(false);

            if (!result.PathFound)
                return McpResultBuilder.Success().WithText($"No path found from '{result.FromSymbol}' to '{result.ToSymbol}'.").Build();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Path from '{result.FromSymbol}' to '{result.ToSymbol}' (length={result.PathLength}):");
            sb.AppendLine();

            for (int i = 0; i < result.PathNodes.Count; i++)
            {
                sb.AppendLine($"  {i}: {result.PathNodes[i]}");
                if (i < result.PathEdges.Count)
                    sb.AppendLine($"     └─[{result.PathEdges[i].CallKind}]→");
            }

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"Path search failed: {ex.Message}").Build();
        }
    }

    [McpTool(CodeToolNameConstants.GraphExplain, "Explain a symbol's role and relationships in the codebase (callers, callees, community, same-file)", "graph")]
    public async Task<ToolResult> ExplainAsync(
        [McpToolParameter("Symbol name to explain")] string symbol_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol_name))
            return McpResultBuilder.Error().WithText("symbol_name cannot be empty.").Build();

        try
        {
            var result = await _indexer.Analytics.ExplainAsync(symbol_name, cancellationToken).ConfigureAwait(false);

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

            return McpResultBuilder.Success().WithText(sb.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"Explain failed: {ex.Message}").Build();
        }
    }
}
