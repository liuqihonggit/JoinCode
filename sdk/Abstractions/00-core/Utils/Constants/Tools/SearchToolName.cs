namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 搜索工具名称枚举
/// </summary>
public enum SearchToolName
{
    [EnumValue("search_code")] SearchCode,
    [EnumValue("search_text")] SearchText,
    [EnumValue("search_files")] SearchFiles,
    [EnumValue("SearchCodebase")] SearchCodebase,
    [EnumValue("Glob")] Glob,
    [EnumValue("Grep")] Grep,
    [EnumValue("search")] Search,
    [EnumValue("code_search")] CodeSearch,
    [EnumValue("symbol_search")] SymbolSearch,
}
