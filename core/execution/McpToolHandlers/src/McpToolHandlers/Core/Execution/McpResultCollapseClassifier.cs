

namespace McpToolHandlers;

/// <summary>
/// 折叠分类类型
/// </summary>
public enum CollapseCategory
{
    /// <summary>
    /// 不折叠
    /// </summary>
    [EnumValue("none")] None,

    /// <summary>
    /// 短文本
    /// </summary>
    [EnumValue("shortText")] ShortText,

    /// <summary>
    /// 长文本
    /// </summary>
    [EnumValue("longText")] LongText,

    /// <summary>
    /// 代码块
    /// </summary>
    [EnumValue("codeBlock")] CodeBlock,

    /// <summary>
    /// JSON 数据
    /// </summary>
    [EnumValue("jsonData")] JsonData,

    /// <summary>
    /// 列表数据
    /// </summary>
    [EnumValue("listData")] ListData,

    /// <summary>
    /// 表格数据
    /// </summary>
    [EnumValue("tableData")] TableData,

    /// <summary>
    /// 错误信息
    /// </summary>
    [EnumValue("error")] Error,

    /// <summary>
    /// 二进制数据
    /// </summary>
    [EnumValue("binaryData")] BinaryData,

    /// <summary>
    /// 图像数据
    /// </summary>
    [EnumValue("imageData")] ImageData
}

/// <summary>
/// 折叠分类结果
/// </summary>
public sealed record CollapseClassification(
    CollapseCategory Category,
    bool ShouldCollapse,
    int Priority,
    string? CollapseTitle = null,
    string? PreviewText = null);

/// <summary>
/// MCP 结果折叠分类器 - 根据内容类型和长度决定是否需要折叠显示
/// </summary>
public static class McpResultCollapseClassifier
{
    private const int ShortTextThreshold = WorkflowConstants.Collapse.ShortTextThreshold;
    private const int LongTextThreshold = WorkflowConstants.Collapse.LongTextThreshold;
    private const int ListItemThreshold = WorkflowConstants.Collapse.ListItemThreshold;
    private const int LineCountThreshold = WorkflowConstants.ContextCompression.LineCountThreshold;

    private static readonly Regex CodeBlockPattern = new(
        @"```[\s\S]*?```|`[^`]+`",
        RegexOptions.Compiled);

    private static readonly Regex TablePattern = new(
        @"\|[^\r\n]+\|\r?\n\|[-:\s|]+\|\r?\n(?:\|[^\r\n]+\|\r?\n?)+",
        RegexOptions.Compiled);

    private static readonly Regex ListPattern = new(
        @"^(\s*[-*+]\s+|\s*\d+\.\s+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// 对 MCP 工具调用结果进行分类
    /// </summary>
    public static CollapseClassification Classify(ToolResult result)
    {
        if (result == null)
        {
            return new CollapseClassification(CollapseCategory.None, false, 0);
        }

        if (result.IsError)
        {
            return ClassifyError(result);
        }

        var allText = string.Join("", result.Content
            .Where(c => c.Type == ToolContentType.Text)
            .Select(c => c.Text ?? ""));

        return ClassifyText(allText);
    }

    /// <summary>
    /// 对文本内容进行分类
    /// </summary>
    public static CollapseClassification ClassifyText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new CollapseClassification(CollapseCategory.None, false, 0);
        }

        var trimmedText = text.Trim();
        if (string.IsNullOrEmpty(trimmedText))
        {
            return new CollapseClassification(CollapseCategory.None, false, 0);
        }

        var length = trimmedText.Length;
        var lineCount = StringTruncator.CountLines(trimmedText.AsSpan());

        // 检查是否为 JSON 数据
        if (IsJsonData(trimmedText))
        {
            return new CollapseClassification(
                CollapseCategory.JsonData,
                length > ShortTextThreshold || lineCount > WorkflowConstants.Collapse.ListItemThreshold,
                80,
                "JSON 数据",
                GetPreviewText(trimmedText, WorkflowConstants.Limits.PreviewTextShortLength));
        }

        // 检查是否为代码块
        var codeBlockMatch = CodeBlockPattern.Match(trimmedText);
        if (codeBlockMatch.Success && codeBlockMatch.Length > length * 0.3)
        {
            var codeLines = StringTruncator.CountLines(codeBlockMatch.Value.AsSpan());
            return new CollapseClassification(
                CollapseCategory.CodeBlock,
                codeLines > WorkflowConstants.Collapse.ListItemThreshold || codeBlockMatch.Length > WorkflowConstants.Limits.CodeBlockCollapseThreshold,
                70,
                "代码块",
                GetPreviewText(codeBlockMatch.Value, WorkflowConstants.Limits.PreviewTextShortLength));
        }

        // 检查是否为表格数据
        if (TablePattern.IsMatch(trimmedText))
        {
            var tableMatches = TablePattern.Matches(trimmedText);
            var tableRows = tableMatches.Count > 0
                ? StringTruncator.CountLines(tableMatches[0].Value.AsSpan())
                : 0;

            return new CollapseClassification(
                CollapseCategory.TableData,
                tableRows > WorkflowConstants.Collapse.ListItemThreshold,
                60,
                "表格数据",
                GetPreviewText(trimmedText, WorkflowConstants.Limits.PreviewTextShortLength));
        }

        // 检查是否为列表数据
        var listMatches = ListPattern.Matches(trimmedText);
        if (listMatches.Count >= ListItemThreshold)
        {
            return new CollapseClassification(
                CollapseCategory.ListData,
                listMatches.Count > ListItemThreshold || lineCount > LineCountThreshold,
                50,
                $"列表 ({listMatches.Count} 项)",
                GetPreviewText(trimmedText, WorkflowConstants.Limits.PreviewTextShortLength));
        }

        // 根据长度分类文本
        if (length <= ShortTextThreshold && lineCount <= 5)
        {
            return new CollapseClassification(
                CollapseCategory.ShortText,
                false,
                10);
        }

        if (length > LongTextThreshold || lineCount > LineCountThreshold)
        {
            return new CollapseClassification(
                CollapseCategory.LongText,
                true,
                40,
                $"长文本 ({length} 字符, {lineCount} 行)",
                GetPreviewText(trimmedText, WorkflowConstants.Limits.PreviewTextMediumLength));
        }

        return new CollapseClassification(
            CollapseCategory.ShortText,
            false,
            20);
    }

    /// <summary>
    /// 对二进制数据进行分类
    /// </summary>
    public static CollapseClassification ClassifyBinary(byte[] data, string? mimeType = null)
    {
        var size = data?.Length ?? 0;
        var sizeText = FormatBytes(size);

        if (IsImageMimeType(mimeType))
        {
            return new CollapseClassification(
                CollapseCategory.ImageData,
                size > 1024 * 1024, // 大于 1MB 的图像折叠
                90,
                $"图像数据 ({sizeText})",
                $"[{mimeType}] {sizeText}");
        }

        return new CollapseClassification(
            CollapseCategory.BinaryData,
            true,
            85,
            $"二进制数据 ({sizeText})",
            $"{sizeText}");
    }

    /// <summary>
    /// 批量分类多个结果
    /// </summary>
    public static Dictionary<string, CollapseClassification> ClassifyBatch(
        Dictionary<string, ToolResult> results)
    {
        return results.ToDictionary(
            kvp => kvp.Key,
            kvp => Classify(kvp.Value));
    }

    private static CollapseClassification ClassifyError(ToolResult result)
    {
        var errorText = string.Join("", result.Content
            .Where(c => c.Type == ToolContentType.Text)
            .Select(c => c.Text ?? ""));

        var length = errorText?.Length ?? 0;

        return new CollapseClassification(
            CollapseCategory.Error,
            length > ShortTextThreshold,
            100,
            "错误信息",
            GetPreviewText(errorText ?? "", WorkflowConstants.Limits.PreviewTextShortLength));
    }

    private static bool IsJsonData(string text)
    {
        text = text.Trim();

        if ((text.StartsWith('{') && text.EndsWith('}')) ||
            (text.StartsWith('[') && text.EndsWith(']')))
        {
            try
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                var reader = new Utf8JsonReader(bytes);
                return reader.Read();
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsImageMimeType(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
            return false;

        return mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPreviewText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        // 使用 StringBuilder 避免多次字符串分配
        using var pooled = StringBuilderPool.Rent();
        var builder = pooled.Builder;

        var lineCount = 0;
        var textSpan = text.AsSpan();
        var start = 0;

        // 手动遍历行，避免 Split 分配
        for (var i = 0; i <= textSpan.Length && lineCount < 3; i++)
        {
            if (i == textSpan.Length || textSpan[i] == '\n')
            {
                var lineSpan = textSpan.Slice(start, i - start).Trim();

                if (!lineSpan.IsEmpty)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(' ');
                    }
                    builder.Append(lineSpan);
                    lineCount++;
                }

                start = i + 1;
            }
        }

        if (builder.Length > maxLength)
        {
            builder.Length = maxLength - 3;
            builder.Append("...");
        }

        return builder.ToString();
    }

    private static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        return bytes switch
        {
            >= GB => $"{bytes / (double)GB:F2} GB",
            >= MB => $"{bytes / (double)MB:F2} MB",
            >= KB => $"{bytes / (double)KB:F2} KB",
            _ => $"{bytes} B"
        };
    }

}

/// <summary>
/// 折叠分类器扩展方法
/// </summary>
public static class CollapseClassifierExtensions
{
    /// <summary>
    /// 获取折叠建议
    /// </summary>
    public static string GetCollapseRecommendation(this CollapseClassification classification)
    {
        if (!classification.ShouldCollapse)
        {
            return "无需折叠";
        }

        return classification.Category switch
        {
            CollapseCategory.LongText => "建议折叠长文本内容",
            CollapseCategory.CodeBlock => "建议折叠代码块",
            CollapseCategory.JsonData => "建议折叠 JSON 数据",
            CollapseCategory.ListData => "建议折叠列表数据",
            CollapseCategory.TableData => "建议折叠表格数据",
            CollapseCategory.Error => "错误信息已折叠",
            CollapseCategory.BinaryData => "二进制数据已折叠",
            CollapseCategory.ImageData => "图像数据已折叠",
            _ => "建议折叠"
        };
    }

    /// <summary>
    /// 获取优先级描述
    /// </summary>
    public static string GetPriorityDescription(this CollapseClassification classification)
    {
        return classification.Priority switch
        {
            >= 90 => "极高",
            >= 70 => "高",
            >= 50 => "中",
            >= 30 => "低",
            _ => "极低"
        };
    }
}
