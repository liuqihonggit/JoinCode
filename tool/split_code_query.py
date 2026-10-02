"""拆分 code_query → search_hybrid + search_document，删除 4 个冗余模式分支。
用文本标记定位，不依赖行号。"""
import pathlib
import sys

f = pathlib.Path(r"D:\project\w2\kit\mcp_tool_dispatch\code_tools\CodeIndexToolHandlers.cs")
content = f.read_text(encoding='utf-8')

start_marker = "    /// <summary>\n    /// 统一代码检索"
end_marker = "    private async Task AppendGraphRelationsAsync"

try:
    start = content.index(start_marker)
    end = content.index(end_marker)
except ValueError as e:
    print(f"ERROR: marker not found: {e}", file=sys.stderr)
    sys.exit(1)

deleted = content[start:end]
print(f"Will delete {len(deleted)} chars ({deleted.count(chr(10))} lines)")

new_methods = '''    /// <summary>
    /// 混合检索 — 符号+向量并行召回，去重合并。符号结果优先，向量结果补充。
    /// </summary>
    /// <param name="query">搜索查询（符号名、自然语言或代码片段）。</param>
    /// <param name="top_k">最大结果数。</param>
    /// <param name="symbol_kind">符号类型过滤（class/method/constructor/...）。</param>
    /// <param name="file_type">文件类型过滤（cs/md/...）。</param>
    /// <param name="namespace_filter">命名空间前缀过滤。</param>
    /// <param name="include_source_text">包含块原文。</param>
    /// <param name="include_graph">包含图谱关系（调用方/被调用方）。</param>
    /// <param name="persist_dir">索引目录（默认自动发现 git 工作区）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>包含混合检索结果的工具结果。</returns>
    [McpTool(CodeToolNameEnumConstants.SearchHybrid, "Hybrid search: symbol + vector parallel recall, dedup merge. Symbol results first, vector fills in. Set include_graph=true for caller/callee relations.", "code_index")]
    public async Task<ToolResult> SearchHybridAsync(
        [McpToolParameter("Search query (symbol name, natural language, or code snippet)")] string query,
        [McpToolParameter("Maximum results", Required = false, DefaultValue = "10")] int top_k = 10,
        [McpToolParameter("Filter by symbol kind (class/method/constructor/...)")] string? symbol_kind = null,
        [McpToolParameter("Filter by file extension (cs/md/...)")] string? file_type = null,
        [McpToolParameter("Filter by namespace prefix")] string? namespace_filter = null,
        [McpToolParameter("Include source text in results")] bool include_source_text = false,
        [McpToolParameter("Include graph relations (callers/callees)")] bool include_graph = false,
        [McpToolParameter("Index directory (default: auto-discover git workspace)")] string? persist_dir = null,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(query)) {
            return ToolResultBuilder.Error().WithText(L.T(StringKey.QueryCannotBeEmpty)).Build();
        }

        try {
            await EnsureLoadedAsync(cancellationToken, persist_dir).ConfigureAwait(false);

            var symbolTask = _indexer.Searcher.SearchAsync(query, cancellationToken);
            var vectorOptions = new SearchOptions {
                IncludeSourceText = include_source_text,
                FileType = file_type,
                Namespace = namespace_filter,
                SymbolKind = symbol_kind
            };
            var vectorTask = _indexer.SearchSemanticAsync(query, top_k, cancellationToken, vectorOptions);
            await Task.WhenAll(symbolTask, vectorTask).ConfigureAwait(false);

            var symbolResult = await symbolTask.ConfigureAwait(false);
            var vectorResult = await vectorTask.ConfigureAwait(false);
            var sb = new StringBuilder();
            sb.AppendLine($"Hybrid results for: \\"{query}\\"");
            sb.AppendLine();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var idx = 0;
            var symbolLimit = Math.Min(symbolResult.Items.Count, top_k);
            for (var i = 0; i < symbolLimit; i++) {
                var s = symbolResult.Items[i];
                var key = $"{s.FilePath}:{s.StartLine}";
                if (!seen.Add(key)) continue;
                idx++;
                sb.AppendLine($"{idx}. [symbol] {s.Kind} {s.FullyQualifiedName}");
                sb.AppendLine($"   {s.FilePath.Replace('\\\\', '/')}:{s.StartLine}-{s.EndLine}");
                if (include_graph) {
                    await AppendGraphRelationsAsync(sb, s.FullyQualifiedName, cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (var r in vectorResult) {
                var key = $"{r.FilePath}:{r.StartLine}";
                if (!seen.Add(key)) continue;
                idx++;
                sb.AppendLine($"{idx}. [vector {r.Score:F4}] {r.SymbolFqn}");
                sb.AppendLine($"   {r.FilePath.Replace('\\\\', '/')}:{r.StartLine}-{r.EndLine}");
                if (include_source_text && !string.IsNullOrEmpty(r.SourceText)) {
                    sb.AppendLine("   --- Source ---");
                    foreach (var line in r.SourceText.Split('\\n')) {
                        sb.AppendLine($"   {line}");
                    }
                    sb.AppendLine("   --- End Source ---");
                }
                if (include_graph && !string.IsNullOrEmpty(r.SymbolFqn)) {
                    await AppendGraphRelationsAsync(sb, r.SymbolFqn, cancellationToken).ConfigureAwait(false);
                }
                if (idx >= top_k) break;
            }

            if (idx == 0) {
                sb.AppendLine("No results found.");
            }
            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText($"search_hybrid failed: {ex.Message}").Build();
        }
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
                return ToolResultBuilder.Success().WithText($"No document matches for: \\"{query}\\"").Build();
            }
            var sb = FormatVectorResults(results, query, "document", include_source_text);
            return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
        } catch (Exception ex) {
            return ToolResultBuilder.Error().WithText($"search_document failed: {ex.Message}").Build();
        }
    }

'''

new_content = content[:start] + new_methods + content[end:]
f.write_text(new_content, encoding='utf-8')
print(f"Done. Original: {len(content)} chars, New: {len(new_content)} chars")
