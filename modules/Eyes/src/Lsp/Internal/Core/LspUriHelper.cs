namespace Services.Lsp.Internal;

/// <summary>
/// LSP URI 工具方法 - 文件路径与 file:// URI 互转
/// </summary>
internal static class LspUriHelper
{
    /// <summary>
    /// 将文件路径转换为 file:// URI
    /// </summary>
    internal static string PathToFileUrl(string filePath)
    {
        return Uri.TryCreate(Path.GetFullPath(filePath), UriKind.Absolute, out var uri)
            ? uri.AbsoluteUri
            : filePath;
    }
}
