
namespace JoinCode.Abstractions.Commands;

/// <summary>
/// Web搜索命令
/// </summary>
public sealed record WebSearchCommand(
    [Required(ErrorMessage = "query 不能为空")]
    [StringLength(500, ErrorMessage = "搜索查询过长")]
    string Query,
    [Range(1, 20, ErrorMessage = "结果数量必须在 1-20 之间")]
    int? NumResults = null,
    [StringLength(10, ErrorMessage = "语言代码过长")]
    string? Language = null);

/// <summary>
/// Web内容获取命令
/// </summary>
public sealed record WebFetchCommand(
    [Required(ErrorMessage = "url 不能为空")]
    [StringLength(2048, ErrorMessage = "URL 过长")]
    [Url(ErrorMessage = "无效的 URL 格式")]
    string Url,
    [Range(100, 100000, ErrorMessage = "最大长度必须在 100-100000 之间")]
    int? MaxLength = null,
    bool ConvertToMarkdown = true);
