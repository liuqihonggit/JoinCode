namespace JoinCode.CodeIndex.Parsing;

/// <summary>
/// 按固定行数切块 — 向量嵌入用，预嵌入 AST 符号 FQN 组成知识图谱。
/// <para>与 CSharpSymbolExtractor 的 AST 符号切块互补：</para>
/// <para>  向量索引 = 固定行数块（粗粒度，快速召回，~500行/块）</para>
/// <para>  符号索引 = AST 符号（细粒度，精确查询，调用图/依赖图）</para>
/// <para>  关联方式 = 块内预嵌入 ContainedSymbolFqns，搜索时直接查图谱</para>
/// </summary>
public static class LineBasedChunkExtractor {

    /// <summary>
    /// 按固定行数切块，每块记录覆盖的 AST 符号 FQN。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="sourceCode">源代码文本。</param>
    /// <param name="symbols">该文件的 AST 符号列表（用于预嵌入 FQN）。</param>
    /// <param name="chunkSize">每块行数（默认 500）。</param>
    /// <param name="languageId">语言标识。</param>
    /// <returns>代码块列表。</returns>
    public static IReadOnlyList<ChunkInfo> Extract(
        string filePath,
        string sourceCode,
        IReadOnlyList<SymbolInfo> symbols,
        int chunkSize = 500,
        string languageId = "c-sharp") {
        if (string.IsNullOrEmpty(sourceCode)) return [];
        var lines = sourceCode.Split('\n');
        if (lines.Length == 0) return [];

        var chunks = new List<ChunkInfo>((lines.Length + chunkSize - 1) / chunkSize);

        for (var start = 0; start < lines.Length; start += chunkSize) {
            var end = Math.Min(start + chunkSize - 1, lines.Length - 1);
            var startLine = start + 1;
            var endLine = end + 1;

            var sourceText = string.Join('\n', lines, start, end - start + 1);
            if (string.IsNullOrWhiteSpace(sourceText)) continue;

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
            });
        }

        return chunks;
    }
}
