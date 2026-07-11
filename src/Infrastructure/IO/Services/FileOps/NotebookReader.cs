
namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// Notebook 读取结果
/// 对齐 TS: notebook.ts readNotebook 返回结构化数据
/// </summary>
public sealed record NotebookReadResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 格式化的文本内容（成功时）
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// 提取的图像列表（成功时，来自 cell 输出中的 image/png 和 image/jpeg）
    /// 对齐 TS: extractImage — 从 execute_result/display_data 输出中提取图像
    /// </summary>
    public List<NotebookImage>? Images { get; init; }

    /// <summary>
    /// 错误消息（失败时）
    /// </summary>
    public string? ErrorMessage { get; init; }

    public static NotebookReadResult Ok(string text, List<NotebookImage> images) =>
        new() { Success = true, Text = text, Images = images };

    public static NotebookReadResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}

/// <summary>
/// Notebook 输出中的图像
/// 对齐 TS: extractImage 返回的 NotebookOutputImage
/// </summary>
public sealed record NotebookImage
{
    /// <summary>
    /// base64 编码的图像数据（已去除空白字符）
    /// 对齐 TS: data['image/png'].replace(/\s/g, '')
    /// </summary>
    public required string Base64Data { get; init; }

    /// <summary>
    /// 媒体类型（"image/png" 或 "image/jpeg"）
    /// </summary>
    public required string MediaType { get; init; }
}

/// <summary>
/// Notebook 读取器
/// 对齐 TS: notebook.ts readNotebook + mapNotebookCellsToToolResult
/// 将 .ipynb 文件解析为格式化的文本输出 + 图像列表
/// </summary>
public static class NotebookReader
{
    /// <summary>
    /// 大输出阈值（对齐 TS: LARGE_OUTPUT_THRESHOLD = 10000）
    /// </summary>
    private const int LargeOutputThreshold = 10000;

    /// <summary>
    /// 判断文件扩展名是否为 Notebook
    /// </summary>
    public static bool IsNotebookExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath.AsSpan());
        return ext.Equals(".ipynb", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 读取并格式化 Notebook 文件
    /// 对齐 TS: readNotebook + mapNotebookCellsToToolResult
    /// 返回格式化文本 + 提取的图像列表
    /// </summary>
    public static async Task<NotebookReadResult> ReadNotebookAsync(string filePath, IFileSystem fs, CancellationToken cancellationToken = default)
    {
        if (!fs.FileExists(filePath))
            return NotebookReadResult.Fail($"Notebook file not found: {filePath}");

        var json = await fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        NotebookDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize(json, NotebookDocumentJsonContext.Default.NotebookDocument);
        }
        catch (JsonException)
        {
            return NotebookReadResult.Fail($"Failed to parse notebook: {filePath}");
        }
        if (doc is null)
            return NotebookReadResult.Fail($"Failed to parse notebook: {filePath}");

        var language = doc.Metadata?.LanguageInfo?.Name ?? "python";
        var sb = new StringBuilder();
        var images = new List<NotebookImage>();

        for (int i = 0; i < doc.Cells.Count; i++)
        {
            var cell = doc.Cells[i];
            var cellId = cell.Id ?? $"cell-{i}";
            var sourceText = cell.SourceText;

            // 对齐 TS: cellContentToToolResult — <cell id="..."> 格式
            var metadata = new List<string>();
            if (cell.Type != NotebookCellType.Code)
            {
                metadata.Add($"<cell_type>{cell.CellType}</cell_type>");
            }
            if (cell.Type == NotebookCellType.Code && language != "python")
            {
                metadata.Add($"<language>{language}</language>");
            }

            sb.AppendLine($"<cell id=\"{cellId}\">{string.Join("", metadata)}{sourceText}</cell id=\"{cellId}\">");

            // 对齐 TS: 处理 code cell 的输出
            if (cell.Type == NotebookCellType.Code && cell.Outputs is { Count: > 0 })
            {
                var includeLargeOutputs = !IsLargeOutputs(cell.Outputs);
                if (!includeLargeOutputs)
                {
                    sb.AppendLine("  Outputs are too large to include. Use the notebook_edit tool to view outputs.");
                }
                else
                {
                    foreach (var output in cell.Outputs)
                    {
                        var (outputText, outputImages) = ProcessOutput(output);
                        if (!string.IsNullOrEmpty(outputText))
                        {
                            sb.AppendLine(outputText);
                        }
                        if (outputImages is not null)
                        {
                            images.AddRange(outputImages);
                        }
                    }
                }
            }

            sb.AppendLine();
        }

        return NotebookReadResult.Ok(sb.ToString(), images);
    }

    /// <summary>
    /// 处理输出（对齐 TS: processOutput）
    /// 返回文本和提取的图像
    /// </summary>
    private static (string Text, List<NotebookImage>? Images) ProcessOutput(NotebookOutput output)
    {
        return output.OutputType switch
        {
            "stream" => (ProcessOutputText(output.Text), null),
            "execute_result" or "display_data" => ProcessOutputWithData(output.Data, output.Text),
            "error" => ($"{output.ErrorName}: {output.ErrorValue}\n{string.Join("\n", output.Traceback ?? [])}", null),
            _ => (string.Empty, null)
        };
    }

    private static (string Text, List<NotebookImage>? Images) ProcessOutputWithData(
        Dictionary<string, JsonElement>? data, List<string>? fallbackText)
    {
        List<NotebookImage>? images = null;

        // 对齐 TS: extractImage — 从 data 中提取 image/png 和 image/jpeg
        if (data is not null)
        {
            if (data.TryGetValue("image/png", out var pngData) && pngData.ValueKind == JsonValueKind.String)
            {
                var base64 = pngData.GetString() ?? "";
                // 对齐 TS: data['image/png'].replace(/\s/g, '') — 去除空白字符
                images ??= new List<NotebookImage>();
                images.Add(new NotebookImage
                {
                    Base64Data = RemoveWhitespace(base64),
                    MediaType = "image/png"
                });
            }
            if (data.TryGetValue("image/jpeg", out var jpegData) && jpegData.ValueKind == JsonValueKind.String)
            {
                var base64 = jpegData.GetString() ?? "";
                images ??= new List<NotebookImage>();
                images.Add(new NotebookImage
                {
                    Base64Data = RemoveWhitespace(base64),
                    MediaType = "image/jpeg"
                });
            }
        }

        var text = ProcessOutputText(data, fallbackText);
        return (text, images);
    }

    /// <summary>
    /// 去除 base64 字符串中的空白字符
    /// 对齐 TS: data['image/png'].replace(/\s/g, '')
    /// Jupyter notebook 的 base64 有时会包含换行符
    /// </summary>
    private static string RemoveWhitespace(string base64)
    {
        var len = base64.Length;
        var sb = new StringBuilder(len);
        for (var i = 0; i < len; i++)
        {
            var c = base64[i];
            if (!char.IsWhiteSpace(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static string ProcessOutputText(List<string>? text)
    {
        if (text is null or []) return string.Empty;
        var rawText = string.Join("", text);
        return TruncateIfLarge(rawText);
    }

    private static string ProcessOutputText(Dictionary<string, JsonElement>? data, List<string>? fallbackText)
    {
        if (data is not null && data.TryGetValue("text/plain", out var plainText))
        {
            var text = plainText.ValueKind == JsonValueKind.String
                ? plainText.GetString() ?? ""
                : plainText.GetRawText();
            return TruncateIfLarge(text);
        }
        return ProcessOutputText(fallbackText);
    }

    private static string TruncateIfLarge(string text)
    {
        if (text.Length <= LargeOutputThreshold)
            return text;
        return string.Concat(text.AsSpan(0, LargeOutputThreshold), $"\n... (truncated, {text.Length} total characters)");
    }

    /// <summary>
    /// 检查输出是否过大（对齐 TS: isLargeOutputs）
    /// </summary>
    private static bool IsLargeOutputs(List<NotebookOutput> outputs)
    {
        int size = 0;
        foreach (var output in outputs)
        {
            if (output.Text is not null)
                size += string.Join("", output.Text).Length;
            if (output.Data is not null && output.Data.TryGetValue("text/plain", out var plainText))
                size += plainText.GetRawText().Length;
            if (size > LargeOutputThreshold)
                return true;
        }
        return false;
    }
}
