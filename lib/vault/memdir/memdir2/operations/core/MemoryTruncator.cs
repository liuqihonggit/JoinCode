
namespace Core.Memdir;

/// <summary>
/// 记忆截断器接口
/// 处理入口点截断（200 行 / 25KB 阈值）
/// </summary>
public interface IMemoryTruncator {
    /// <summary>
    /// 截断记忆内容
    /// </summary>
    /// <param name="content">原始内容</param>
    /// <param name="threshold">截断阈值</param>
    /// <returns>截断后的内容</returns>
    string Truncate(string content, TruncationThreshold? threshold = null);

    /// <summary>
    /// 智能截断 - 保留最相关的部分
    /// </summary>
    /// <param name="content">原始内容</param>
    /// <param name="query">查询内容</param>
    /// <param name="threshold">截断阈值</param>
    /// <returns>截断后的内容</returns>
    string SmartTruncate(string content, string query, TruncationThreshold? threshold = null);
}

/// <summary>
/// 截断阈值配置
/// </summary>
public sealed record TruncationThreshold {
    /// <summary>
    /// 最大行数
    /// </summary>
    public int MaxLines { get; init; } = 200;

    /// <summary>
    /// 最大字节数
    /// </summary>
    public int MaxBytes { get; init; } = 25 * 1024; // 25KB

    /// <summary>
    /// 默认阈值
    /// </summary>
    public static TruncationThreshold Default => new();
}

/// <summary>
/// 记忆截断器实现
/// </summary>
[Register(typeof(IMemoryTruncator), ServiceLifetime.Singleton)]
public sealed partial class MemoryTruncator : ServiceEntity, IMemoryTruncator {
    private readonly ILogger<MemoryTruncator>? _logger;

    /// <summary>
    /// 创建记忆截断器实例
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    public MemoryTruncator(ILogger<MemoryTruncator>? logger = null) {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Truncate(string content, TruncationThreshold? threshold = null) {
        var config = threshold ?? TruncationThreshold.Default;

        if (string.IsNullOrEmpty(content)) {
            return content;
        }

        // 快速行数计数 — 避免分配数组
        var lineCount = StringTruncator.CountLines(content.AsSpan());
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(content);

        // 检查是否需要截断
        if (lineCount <= config.MaxLines && byteCount <= config.MaxBytes) {
            return content;
        }

        _logger?.LogDebug(
            "Truncating memory: {Lines} lines, {Bytes} bytes -> max {MaxLines} lines, {MaxBytes} bytes",
            lineCount, byteCount, config.MaxLines, config.MaxBytes);

        // 优先按行数截断
        if (lineCount > config.MaxLines) {
            // 仅在需要截断时才 Split
            var lines = content.Split('\n');
            var result = string.Join('\n', lines.Take(config.MaxLines)) + L.T(StringKey.VaultTruncatedMaxLines);

            // 再次检查字节数
            var resultBytes = System.Text.Encoding.UTF8.GetByteCount(result);
            if (resultBytes > config.MaxBytes) {
                return TruncateByBytes(result, config.MaxBytes);
            }

            return result;
        }

        // 按字节数截断
        return TruncateByBytes(content, config.MaxBytes);
    }

    /// <inheritdoc />
    public string SmartTruncate(string content, string query, TruncationThreshold? threshold = null) {
        var config = threshold ?? TruncationThreshold.Default;

        if (string.IsNullOrEmpty(content)) {
            return content;
        }

        // 快速行数计数 — 避免分配数组
        var lineCount = StringTruncator.CountLines(content.AsSpan());
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(content);

        // 检查是否需要截断
        if (lineCount <= config.MaxLines && byteCount <= config.MaxBytes) {
            return content;
        }

        _logger?.LogDebug(
            "Smart truncating memory for query: {Query}",
            query.AsSpan(0, Math.Min(50, query.Length)).ToString());

        // 仅在需要截断时才 Split
        var lines = content.Split('\n');

        // 找到最相关的行并组装截断输出
        var queryWords = QueryWordHelper.ExtractQueryWords(query);
        var scoredLines = ScoreAndSelectLines(lines, queryWords, config.MaxLines);
        var resultLines = AssembleTruncatedLines(scoredLines, lineCount, config.MaxLines);

        var result = string.Join('\n', resultLines);

        // 检查字节数
        var resultBytes = System.Text.Encoding.UTF8.GetByteCount(result);
        if (resultBytes > config.MaxBytes) {
            return TruncateByBytes(result, config.MaxBytes);
        }

        return result;
    }

    /// <summary>
    /// 评分后的行 — 纯数据,用于 SmartTruncate 中间结果传递
    /// </summary>
    internal sealed record ScoredLine(string Line, int Index, double Score);

    /// <summary>
    /// 对行按查询相关性评分并选择 top-N — 纯计算
    /// 流程:评分 → 按分数降序 → 取 maxLines/2 → 按原索引升序恢复顺序
    /// </summary>
    internal static List<ScoredLine> ScoreAndSelectLines(string[] lines, string[] queryWords, int maxLines) {
        return lines
            .Select((line, index) => new ScoredLine(line, index, CalculateLineRelevance(line, queryWords)))
            .OrderByDescending(x => x.Score)
            .Take(maxLines / 2)
            .OrderBy(x => x.Index)
            .ToList();
    }

    /// <summary>
    /// 组装截断后的行列表 — 纯计算
    /// 不连续行号间插入省略号;行数超阈值时追加截断提示
    /// </summary>
    internal static List<string> AssembleTruncatedLines(List<ScoredLine> scoredLines, int totalLineCount, int maxLines) {
        var resultLines = new List<string>();
        int? lastIndex = null;

        foreach (var item in scoredLines) {
            // 如果行号不连续，添加省略号
            if (lastIndex.HasValue && item.Index > lastIndex.Value + 1) {
                resultLines.Add("...");
            }

            resultLines.Add(item.Line);
            lastIndex = item.Index;
        }

        // 添加截断提示
        if (totalLineCount > maxLines) {
            resultLines.Add("");
            resultLines.Add(L.T(StringKey.VaultTruncatedTotalLines, totalLineCount));
        }

        return resultLines;
    }

    /// <summary>
    /// 计算行相关性分数
    /// </summary>
    internal static double CalculateLineRelevance(string line, string[] queryWords) {
        if (string.IsNullOrWhiteSpace(line) || queryWords.Length == 0) {
            return 0;
        }

        var matches = queryWords.Count(qw => line.Contains(qw, StringComparison.OrdinalIgnoreCase));

        return (double)matches / queryWords.Length;
    }

    /// <summary>
    /// 按字节数截断
    /// </summary>
    internal static string TruncateByBytes(string content, int maxBytes) {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);

        if (bytes.Length <= maxBytes) {
            return content;
        }

        // 留出一些空间给截断提示
        var cutLength = Math.Max(0, maxBytes - 100);

        // 在 byte 层面找到最后一个完整的 UTF-8 字符边界
        // UTF-8 起始字节: 0xxxxxxx (1字节), 110xxxxx (2字节), 1110xxxx (3字节), 11110xxx (4字节)
        // 续字节: 10xxxxxx
        while (cutLength > 0 && (bytes[cutLength] & 0xC0) == 0x80) {
            cutLength--;
        }
        // 如果 cutLength 指向的是续字节之后的起始字节，需要再回退到前一个字符的起始位置
        // 以确保截断点在完整字符之后
        if (cutLength > 0 && (bytes[cutLength] & 0xC0) != 0x80 && (bytes[cutLength] & 0x80) != 0) {
            // cutLength 指向一个多字节字符的起始字节，回退到前一个字符结束
            cutLength--;
            // 再次跳过可能的续字节
            while (cutLength > 0 && (bytes[cutLength] & 0xC0) == 0x80) {
                cutLength--;
            }
        }

        var result = System.Text.Encoding.UTF8.GetString(bytes.AsSpan(0, cutLength));

        return result + L.T(StringKey.VaultTruncatedMaxBytes);
    }

}