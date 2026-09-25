
namespace Core.Context.Compression;

/// <summary>
/// 引用索引压缩策略
/// </summary>
[Register(typeof(ICompressionStrategy), ServiceLifetime.Transient)]
public sealed partial class ReferenceIndexCompressor : CompressionStrategyBase {
    /// <summary>
    /// 策略名称
    /// </summary>
    public override string Name => "ReferenceIndexCompressor";
    /// <summary>
    /// 策略描述
    /// </summary>
    public override string Description => "Compresses code reference index by generating compact index while preserving file paths and key identifiers";
    /// <summary>
    /// 策略优先级
    /// </summary>
    public override int Priority => 100;

    private static readonly HashSet<ContentType> _supportedTypes = new()
    {
        ContentType.ReferenceIndex
    };

    private static readonly string[] s_filePathPatterns =
    [
        @"^文件[:：]\s*(.+)$",
        @"^File[:：]\s*(.+)$",
        @"^路径[:：]\s*(.+)$",
        @"^Path[:：]\s*(.+)$",
        @"^(.+\.(cs|js|ts|py|java|cpp|c|h|hpp|go|rs|rb|php|swift|kt|scala))\s*$",
        @"^[-=]{3,}\s*(.+?)\s*[-=]{3,}$"
    ];

    private static readonly string[] s_identifierPatterns =
    [
        @"^\s*[-*]\s*(class|interface|struct|enum|function|method|def)\s+(\w+)",
        @"^\s*[-*]\s*(\w+)\s*\(",
        @"^\s*(public|private|protected|internal|static)?\s*\w+\s+(\w+)\s*\(",
        @"^\s*[-*]\s*(\w+):"
    ];

    private static readonly string[] s_referencePatterns =
    [
        @"引用[:：]\s*(.+)",
        @"Reference[:：]\s*(.+)",
        @"@\s*(.+)",
        @"->\s*(.+)"
    ];

    /// <summary>
    /// 支持的内容类型
    /// </summary>
    public override IReadOnlySet<ContentType> SupportedContentTypes => _supportedTypes;

    /// <summary>
    /// 压缩引用索引：生成紧凑索引同时保留文件路径与关键标识
    /// </summary>
    /// <param name="content">原始引用索引内容</param>
    /// <param name="options">压缩选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩后的引用索引</returns>
    public override Task<string> CompressAsync(
        string content,
        CompressionOptions options,
        CancellationToken cancellationToken = default) {
        ValidateOptions(options);

        if (string.IsNullOrWhiteSpace(content)) {
            return Task.FromResult(content);
        }

        var entries = ParseReferenceEntries(content);
        if (entries.Count == 0) {
            return Task.FromResult(content);
        }

        var prioritizedEntries = PrioritizeEntries(entries);
        var selectedEntries = prioritizedEntries.Take(options.MaxReferenceEntries).ToList();

        var result = new StringBuilder();
        result.AppendLine("[引用索引]");
        result.AppendLine($"总计: {entries.Count} 个引用 | 显示: {selectedEntries.Count} 个重要引用");
        result.AppendLine();

        var groupedByFile = selectedEntries.GroupBy(e => e.FilePath);

        foreach (var fileGroup in groupedByFile) {
            cancellationToken.ThrowIfCancellationRequested();

            result.AppendLine($"文件: {fileGroup.Key}");

            foreach (var entry in fileGroup) {
                var line = FormatEntry(entry, options);
                if (!string.IsNullOrWhiteSpace(line)) {
                    result.AppendLine($"  {line}");
                }
            }

            result.AppendLine();
        }

        if (entries.Count > options.MaxReferenceEntries) {
            result.AppendLine($"... 还有 {entries.Count - options.MaxReferenceEntries} 个引用未显示 ...");
        }

        return Task.FromResult(result.ToString().TrimEnd());
    }

    /// <summary>
    /// 预估引用索引的压缩比率
    /// </summary>
    /// <param name="content">原始内容</param>
    /// <param name="options">压缩选项</param>
    /// <returns>预估压缩比率 (0-1)</returns>
    public override double EstimateCompressionRatio(string content, CompressionOptions options) {
        if (string.IsNullOrWhiteSpace(content))
            return 1.0;

        var entries = ParseReferenceEntries(content);
        if (entries.Count == 0)
            return 1.0;

        var ratio = (double)Math.Min(entries.Count, options.MaxReferenceEntries) / entries.Count;
        return Math.Max(ratio, options.TargetCompressionRatio);
    }

    private static List<ReferenceEntry> ParseReferenceEntries(string content) {
        var entries = new List<ReferenceEntry>();
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        ReferenceEntry? currentEntry = null;

        foreach (var line in lines) {
            var trimmedLine = line.Trim();

            if (IsFilePathLine(trimmedLine, out var filePath)) {
                if (currentEntry != null) {
                    entries.Add(currentEntry);
                }

                currentEntry = new ReferenceEntry { FilePath = filePath };
            } else if (currentEntry != null && IsIdentifierLine(trimmedLine, out var identifier)) {
                currentEntry.Identifiers.Add(identifier);
            } else if (currentEntry != null && IsReferenceLine(trimmedLine, out var reference)) {
                currentEntry.References.Add(reference);
            }
        }

        if (currentEntry != null) {
            entries.Add(currentEntry);
        }

        if (entries.Count == 0) {
            entries = ParseAlternativeFormat(content);
        }

        return entries;
    }

    private static bool IsFilePathLine(string line, out string filePath) {
        filePath = string.Empty;

        foreach (var pattern in s_filePathPatterns) {
            var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (match.Success) {
                filePath = match.Groups[match.Groups.Count - 1].Value.Trim();
                return true;
            }
        }

        return false;
    }

    private static bool IsIdentifierLine(string line, out string identifier) {
        identifier = string.Empty;

        foreach (var pattern in s_identifierPatterns) {
            var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (match.Success) {
                identifier = match.Groups[match.Groups.Count - 1].Value.Trim();
                return true;
            }
        }

        return false;
    }

    private static bool IsReferenceLine(string line, out string reference) {
        reference = string.Empty;

        foreach (var pattern in s_referencePatterns) {
            var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (match.Success) {
                reference = match.Groups[1].Value.Trim();
                return true;
            }
        }

        return false;
    }

    private static List<ReferenceEntry> ParseAlternativeFormat(string content) {
        var entries = new List<ReferenceEntry>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        var filePathPattern = @"([a-zA-Z]:\\)?([\\/][^\\/:*?""<>|]+)+\.[a-zA-Z0-9]+";
        var matches = Regex.Matches(content, filePathPattern);

        foreach (Match match in matches) {
            var filePath = match.Value;
            if (seenPaths.Add(filePath)) {
                entries.Add(new ReferenceEntry { FilePath = filePath });
            }
        }

        return entries;
    }

    private static List<ReferenceEntry> PrioritizeEntries(List<ReferenceEntry> entries) {
        return entries
            .OrderByDescending(e => CalculatePriority(e))
            .ToList();
    }

    private static int CalculatePriority(ReferenceEntry entry) {
        var priority = 0;

        if (entry.FilePath.Contains("Program.cs", StringComparison.OrdinalIgnoreCase) ||
            entry.FilePath.Contains("Main", StringComparison.OrdinalIgnoreCase) ||
            entry.FilePath.Contains("Index", StringComparison.OrdinalIgnoreCase)) {
            priority += 10;
        }

        if (entry.FilePath.Contains("Interface", StringComparison.OrdinalIgnoreCase) ||
            entry.FilePath.Contains("Service", StringComparison.OrdinalIgnoreCase)) {
            priority += 5;
        }

        priority += entry.Identifiers.Count;
        priority += entry.References.Count;

        if (entry.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            entry.FilePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
            entry.FilePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) {
            priority += 2;
        }

        return priority;
    }

    private static string FormatEntry(ReferenceEntry entry, CompressionOptions options) {
        var parts = new List<string>();

        if (options.PreserveSignatures && entry.Identifiers.Count > 0) {
            parts.Add(string.Join(", ", entry.Identifiers.Take(3)));
        }

        if (entry.References.Count > 0) {
            parts.Add($"引用: {entry.References.Count}");
        }

        return string.Join(" | ", parts);
    }

    private class ReferenceEntry {
        /// <summary>获取或设置文件路径。</summary>
        public string FilePath { get; set; } = string.Empty;
        /// <summary>获取或设置标识符列表。</summary>
        public List<string> Identifiers { get; set; } = new();
        /// <summary>获取或设置引用列表。</summary>
        public List<string> References { get; set; } = new();
    }
}