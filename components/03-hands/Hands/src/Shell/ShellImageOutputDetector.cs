namespace Tools.Shell;

/// <summary>
/// Shell 命令输出图片检测 — 对齐 TS BashTool/utils.ts isImageOutput/parseDataUri
/// 检测 stdout 中的 Data URI 格式 base64 图片数据
/// </summary>
public static class ShellImageOutputDetector
{
    /// <summary>
    /// 检测 stdout 是否为 Data URI 格式的图片输出 — 对齐 TS isImageOutput
    /// 格式: data:image/xxx;base64,...
    /// </summary>
    public static bool IsImageOutput(string stdout)
    {
        if (string.IsNullOrEmpty(stdout))
            return false;

        var trimmed = stdout.AsSpan().Trim();
        if (!trimmed.StartsWith("data:image/".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;

        var semicolonIndex = trimmed.IndexOf(';');
        if (semicolonIndex < 0)
            return false;

        var base64Index = trimmed.Slice(semicolonIndex + 1);
        return base64Index.StartsWith("base64,".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析 Data URI — 对齐 TS parseDataUri
    /// 返回 (mediaType, base64Data) 或 null
    /// </summary>
    public static (string MediaType, string Base64Data)? ParseDataUri(string dataUri)
    {
        if (string.IsNullOrEmpty(dataUri))
            return null;

        var trimmed = dataUri.Trim();

        // data:image/png;base64,iVBOR...
        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        var semicolonIndex = trimmed.IndexOf(';');
        if (semicolonIndex < 0)
            return null;

        var mediaType = trimmed[5..semicolonIndex]; // "image/png"
        var afterSemicolon = trimmed[(semicolonIndex + 1)..];

        if (!afterSemicolon.StartsWith("base64,", StringComparison.OrdinalIgnoreCase))
            return null;

        var base64Data = afterSemicolon[7..]; // skip "base64,"
        if (string.IsNullOrEmpty(base64Data))
            return null;

        return (mediaType, base64Data);
    }
}
