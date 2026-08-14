namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 搜索工具名称枚举
/// </summary>
public enum SearchToolName
{
    [EnumValue("search_code")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    SearchCode,

    [EnumValue("search_text")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    SearchText,

    [EnumValue("search_files")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    SearchFiles,

    [EnumValue("SearchCodebase")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    SearchCodebase,

    [EnumValue("Glob")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    Glob,

    [EnumValue("Grep")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    Grep,

    [EnumValue("search")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    Search,

    [EnumValue("code_search")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeSearch,

    [EnumValue("symbol_search")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    SymbolSearch,
}
