namespace JoinCode.CodeIndex.Ast;

/// <summary>
/// Markdown 文档切块器 — 按 heading（# ## ###）切分，每个 heading 段落一个块。
/// <para>向量库存 heading 段落块，召回后取整个 md 文件原文作为父文档上下文。</para>
/// <para>md 文档无符号/调用/依赖，ExtractionResult 只含 Chunks + ParentDocuments。</para>
/// </summary>
public sealed class MarkdownChunkExtractor {

    private const int MaxParentDocumentLines = 2000;

    /// <summary>
    /// 提取 Markdown 文档的块和父文档 — 按 heading 切块。
    /// </summary>
    /// <param name="sourceCode">Markdown 源文本。</param>
    /// <param name="filePath">文件路径。</param>
    /// <returns>提取结果（Symbols/Calls/Dependencies 为空，只有 Chunks + ParentDocuments）。</returns>
    public ExtractionResult ExtractAll(string sourceCode, string filePath) {
        ArgumentNullException.ThrowIfNull(sourceCode);
        ArgumentNullException.ThrowIfNull(filePath);

        if (string.IsNullOrWhiteSpace(sourceCode)) {
            return new ExtractionResult {
                Symbols = [],
                Calls = [],
                Dependencies = [],
                Chunks = [],
                ParentDocuments = []
            };
        }

        var lines = sourceCode.Split('\n');
        var chunks = new List<ChunkInfo>();
        var fileContentHash = HashUtility.ComputeContentHash(sourceCode);
        var fileParentChunkId = HashUtility.ComputeContentHash($"{filePath}|file|{fileContentHash}");

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
                ParentChunkId = fileParentChunkId
            });
        }

        var parentDocs = new List<ParentDocument> {
            CreateParentDocument(fileParentChunkId, filePath, filePath, 1, lines.Length, lines)
        };

        return new ExtractionResult {
            Symbols = [],
            Calls = [],
            Dependencies = [],
            Chunks = chunks,
            ParentDocuments = parentDocs
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

    private static ParentDocument CreateParentDocument(
        string chunkId, string filePath, string symbolFqn,
        int startLine, int endLine, string[] lines) {
        var lineCount = endLine - startLine + 1;
        var sourceText = ExtractLineRange(lines, startLine, endLine);
        if (lineCount <= MaxParentDocumentLines) {
            return new ParentDocument {
                ChunkId = chunkId,
                FilePath = filePath,
                SymbolFqn = symbolFqn,
                StartLine = startLine,
                EndLine = endLine,
                SourceText = sourceText
            };
        }
        var truncatedEnd = startLine + MaxParentDocumentLines - 1;
        var truncatedText = ExtractLineRange(lines, startLine, truncatedEnd)
            + $"\n<!-- ... truncated (original: {lineCount} lines) -->";
        return new ParentDocument {
            ChunkId = chunkId,
            FilePath = filePath,
            SymbolFqn = symbolFqn,
            StartLine = startLine,
            EndLine = truncatedEnd,
            SourceText = truncatedText,
            IsTruncated = true
        };
    }
}
