namespace Services.Web;

/// <summary>
/// HTML 转 Markdown 转换器 - 使用 ReverseMarkdown.Aot 库
/// 对齐 TS 版 turndown 库的 DOM 遍历转换方式
/// </summary>
[Register]
public sealed class HtmlToMarkdownConverter : IHtmlToMarkdownConverter
{
    private readonly Converter _converter;

    public HtmlToMarkdownConverter()
    {
        var config = new ReverseMarkdown.Config
        {
            // 对齐 TS 版 turndown 默认配置
            UnknownTags = Config.UnknownTagsOption.Bypass,  // 保留未知标签内容
            GithubFlavored = true,  // 启用 GFM（表格、任务列表等）
            RemoveComments = true,  // 移除 HTML 注释
            SmartHrefHandling = true,  // 智能链接处理
        };
        _converter = new Converter(config);
    }

    public string Convert(string html, int? maxLength = null)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        var result = _converter.Convert(html);

        if (maxLength.HasValue && result.Length > maxLength.Value)
            result = result[..maxLength.Value];

        return result;
    }
}
