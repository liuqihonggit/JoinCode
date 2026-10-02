namespace JoinCode.CodeIndex.Ast;

/// <summary>
/// Markdown 文档切块器 — 按 heading（# ## ###）切分，每个 heading 段落一个块。
/// <para>向量库存 heading 段落块，父文档定位信息（整个 md 文件）直接写入 ChunkInfo，搜索时从文件系统读取。</para>
/// <para>md 文档无符号/调用/依赖，ExtractionResult 只含 Chunks。</para>
/// </summary>
public sealed class MarkdownChunkExtractor {

    /// <summary>
    /// 提取 Markdown 文档的块 — 按 heading 切块，父文档定位信息指向整个文件。
    /// </summary>
    /// <param name="sourceCode">Markdown 源文本。</param>
    /// <param name="filePath">文件路径。</param>
    /// <returns>提取结果（Symbols/Calls/Dependencies 为空，只有 Chunks）。</returns>
    public ExtractionResult ExtractAll(string sourceCode, string filePath) {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(filePath);

        if (string.IsNullOrWhiteSpace(sourceCode)) {
            return new ExtractionResult {
                Symbols = [],
                Calls = [],
                Dependencies = [],
                Chunks = []
            };
        }

        var lines = sourceCode.Split('\n');
        var chunks = new List<ChunkInfo>();

        var sections = SplitByHeadings(lines, filePath);
        foreach (var section in sections) {
            var sourceText = ExtractLineRange(lines, section.StartLine, section.EndLine);
            if (string.IsNullOrWhiteSpace(sourceText)) continue;

            var contentHash = HashUtility.ComputeContentHash(sourceText);
            var chunkId = HashUtility.ComputeContentHash($"{filePath}|{section.Fqn}|{contentHash}");

            chunks.Add(new ChunkInfo {
                ChunkId = chunkId,
                SymbolFqn = section.Fqn,
                Kind = SymbolKind.Document,
                FilePath = filePath,
                StartLine = section.StartLine,
                EndLine = section.EndLine,
                LanguageId = "markdown",
                ContentHash = contentHash,
                SourceText = sourceText,
                ParentFilePath = filePath,
                ParentStartLine = 1,
                ParentEndLine = lines.Length,
                ParentSymbolFqn = filePath
            });
        }

        return new ExtractionResult {
            Symbols = [],
            Calls = [],
            Dependencies = [],
            Chunks = chunks
        };
    }

    private static IEnumerable<(string Fqn, int StartLine, int EndLine)> SplitByHeadings(string[] lines, string filePath) {
        var currentStart = -1;
        var currentFqn = string.Empty;

        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i].TrimStart();
            if (line.StartsWith('#')) {
                if (currentStart > 0) {
                    yield return (currentFqn, currentStart, i);
                }
                currentStart = i + 1;
                var headingText = line.TrimStart('#').Trim();
                currentFqn = string.IsNullOrEmpty(headingText)
                    ? $"{filePath}#section-{i + 1}"
                    : $"{filePath}#{headingText}";
            } else if (currentStart < 0 && !string.IsNullOrWhiteSpace(line)) {
                currentStart = i + 1;
                currentFqn = $"{filePath}#intro";
            }
        }

        if (currentStart > 0 && currentStart <= lines.Length) {
            yield return (currentFqn, currentStart, lines.Length);
        }
    }

    private static string ExtractLineRange(string[] lines, int startLine, int endLine) {
        if (startLine < 1 || endLine < startLine || startLine > lines.Length) {
            return string.Empty;
        }
        var end = Math.Min(endLine, lines.Length);
        var span = lines.AsSpan((startLine - 1), (end - startLine + 1));
        return string.Join('\n', span);
    }
}
