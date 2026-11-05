namespace Services.Web;

/// <summary>
/// HTML 转 Markdown 转换器 - 使用 ReverseMarkdown.Aot 库
/// 对齐 TS 版 turndown 库的 DOM 遍历转换方式
/// </summary>
[Register(typeof(IHtmlToMarkdownConverter), ServiceLifetime.Singleton)]
public sealed partial class HtmlToMarkdownConverter : ServiceEntity, IHtmlToMarkdownConverter {
    private readonly Converter _converter;

    /// <summary>
    /// 初始化 <see cref="HtmlToMarkdownConverter"/> 实例，配置对齐 TS 版 turndown 默认行为。
    /// </summary>
    public HtmlToMarkdownConverter() {
        var config = new ReverseMarkdown.Config {
            // 对齐 TS 版 turndown 默认配置
            UnknownTags = Config.UnknownTagsOption.Bypass,  // 保留未知标签内容
            GithubFlavored = true,  // 启用 GFM（表格、任务列表等）
            RemoveComments = true,  // 移除 HTML 注释
            SmartHrefHandling = true,  // 智能链接处理
        };
        _converter = new Converter(config);
    }

    /// <summary>
    /// 将 HTML 字符串转换为 Markdown 文本，可选截断到指定最大长度。
    /// </summary>
    /// <param name="html">待转换的 HTML 字符串。</param>
    /// <param name="maxLength">可选的最大长度，超出时截断。</param>
    /// <returns>转换后的 Markdown 文本。</returns>
    public string Convert(string html, int? maxLength = null) {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        var result = _converter.Convert(html);

        if (maxLength.HasValue && result.Length > maxLength.Value)
            result = result[..maxLength.Value];

        return result;
    }
}