namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// HTML 转 Markdown 转换器接口
/// </summary>
public interface IHtmlToMarkdownConverter
{
    /// <summary>
    /// 将 HTML 内容转换为 Markdown 格式
    /// </summary>
    /// <param name="html">HTML 内容</param>
    /// <param name="maxLength">最大长度限制（可选）</param>
    /// <returns>转换后的 Markdown 文本</returns>
    string Convert(string html, int? maxLength = null);
}
