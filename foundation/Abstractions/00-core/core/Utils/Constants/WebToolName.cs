namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Web 工具名称枚举
/// </summary>
public enum WebToolName
{
    [EnumValue("WebFetch")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    WebFetch,

    [EnumValue("WebSearch")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    WebSearch,

    [EnumValue("web_to_markdown")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    WebToMarkdown,

    [EnumValue("web_browser")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    WebBrowser,
}
