namespace JoinCode.CodeIndex.Parsing;

/// <summary>
/// 按固定行数切块 — 向量嵌入用，预嵌入 AST 符号 FQN 组成知识图谱。
/// <para>与 CSharpSymbolExtractor 的 AST 符号切块互补：</para>
/// <para>  向量索引 = 固定行数块（粗粒度，快速召回，~500行/块）</para>
/// <para>  符号索引 = AST 符号（细粒度，精确查询，调用图/依赖图）</para>
/// <para>  关联方式 = 块内预嵌入 ContainedSymbolFqns，搜索时直接查图谱</para>
/// <para>Span 优化：用 ReadOnlySpan 切片替代 Split+Join，消除中间 string[] 分配。</para>
/// </summary>
public static class LineBasedChunkExtractor {

    /// <summary>
    /// 按固定行数切块，每块记录覆盖的 AST 符号 FQN。
    /// <para>Span 优化：遍历一次记录行起始偏移，按偏移 Slice 取块文本，0 中间 GC。</para>
    /// <para>重叠切块：相邻块有 overlap 行重叠，避免函数被切断在块边界。步长=chunkSize-overlap。</para>
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="sourceCode">源代码文本。</param>
    /// <param name="symbols">该文件的 AST 符号列表（用于预嵌入 FQN）。</param>
    /// <param name="parentDocuments">该文件的父文档列表（用于关联块的 ParentChunkId）。</param>
    /// <param name="chunkSize">每块行数（默认 500）。</param>
    /// <param name="overlap">相邻块重叠行数（默认 25，即 5%），避免函数跨越块边界被切断。</param>
    /// <param name="languageId">语言标识。</param>
    /// <returns>代码块列表。</returns>
    public static IReadOnlyList<ChunkInfo> Extract(
        string filePath,
        string sourceCode,
        IReadOnlyList<SymbolInfo> symbols,
        IReadOnlyList<ParentDocument>? parentDocuments = null,
        int chunkSize = 500,
        int overlap = 25,
        string languageId = "c-sharp") {
        if (string.IsNullOrEmpty(sourceCode)) return [];

        var span = sourceCode.AsSpan();

        var lineStarts = new List<int>(Math.Min(span.Length / 40 + 1, 1024)) { 0 };
        for (var i = 0; i < span.Length; i++) {
            if (span[i] == '\n') lineStarts.Add(i + 1);
        }
        var totalLines = lineStarts.Count;
        if (totalLines == 0) return [];

        var step = chunkSize - Math.Clamp(overlap, 0, chunkSize - 1);
        var estimatedChunks = (totalLines + step - 1) / step;
        var chunks = new List<ChunkInfo>(estimatedChunks);

        for (var start = 0; start < totalLines; start += step) {
            var end = Math.Min(start + chunkSize - 1, totalLines - 1);
            var startLine = start + 1;
            var endLine = end + 1;

            var startOffset = lineStarts[start];
            var endOffset = (end + 1 < totalLines) ? lineStarts[end + 1] - 1 : span.Length;
            var length = endOffset - startOffset;
            if (length <= 0) continue;
            var chunkSpan = span.Slice(startOffset, length);
            if (chunkSpan.IsWhiteSpace()) continue;

            var sourceText = chunkSpan.ToString();
            var contentHash = HashUtility.ComputeContentHash(sourceText);
            var chunkId = HashUtility.ComputeContentHash($"{filePath}|{startLine}|{endLine}|{contentHash}");

            var containedFqns = symbols
                .Where(s => s.StartLine <= endLine && s.EndLine >= startLine)
                .Select(s => s.FullyQualifiedName)
                .ToList();

            chunks.Add(new ChunkInfo {
                ChunkId = chunkId,
                SymbolFqn = $"{Path.GetFileName(filePath)}:{startLine}-{endLine}",
                Kind = SymbolKind.Document,
                FilePath = filePath,
                StartLine = startLine,
                EndLine = endLine,
                LanguageId = languageId,
                ContentHash = contentHash,
                SourceText = sourceText,
                ContainedSymbolFqns = containedFqns,
                ParentChunkId = FindParentChunkId(parentDocuments, startLine, endLine),
            });
        }

        return chunks;
    }

    /// <summary>
    /// 查找包含指定行号范围的父文档 ChunkId — 优先返回最小包含范围的父文档（最精确匹配）。
    /// </summary>
    private static string? FindParentChunkId(
        IReadOnlyList<ParentDocument>? parentDocuments,
        int chunkStartLine,
        int chunkEndLine) {
        if (parentDocuments is null || parentDocuments.Count == 0) return null;
        string? bestId = null;
        var bestSpan = int.MaxValue;
        foreach (var pd in parentDocuments) {
            if (pd.StartLine <= chunkStartLine && pd.EndLine >= chunkEndLine) {
                var span = pd.EndLine - pd.StartLine;
                if (span < bestSpan) {
                    bestSpan = span;
                    bestId = pd.ChunkId;
                }
            }
        }
        return bestId;
    }
}
