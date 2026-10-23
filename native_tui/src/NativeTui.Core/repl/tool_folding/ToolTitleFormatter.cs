namespace NativeTui.Core.Repl;

/// <summary>
/// 工具标题行摘要派生器 — per-tool 从 Args 字符串解析主参数（对齐基准各工具 renderToolUseMessage）：
/// Bash 命令 2 行/160 字符截断+多行显示（BashTool/UI.tsx L26-27 + L104-127）；
/// Read/Write/Edit 路径（FileReadTool/UI.tsx L30-65）；Grep/Glob pattern+path 引号包裹
/// （GrepTool/UI.tsx L127-146 `pattern: "x", path: "y"`）；Edit Create/Update 派生
/// （FileEditTool/UI.tsx L24-45 old_string 空 = Create）。
/// Args 兼容双形态：JSON 对象（真实 bridge）按字段提取；纯文本（模拟引擎/旧调用）原样降级。
/// JSON 解析走 JsonDocument 动态字段提取（ADR 0133 保留例外 — 工具参数字段集合动态无法 DTO 化）。
/// </summary>
public static class ToolTitleFormatter
{
    /// <summary>命令标题显示行数上限（BashTool/UI.tsx MAX_COMMAND_DISPLAY_LINES）。</summary>
    public const int MaxCommandDisplayLines = 2;

    /// <summary>命令标题显示字符上限（BashTool/UI.tsx MAX_COMMAND_DISPLAY_CHARS）。</summary>
    public const int MaxCommandDisplayChars = 160;

    /// <summary>Read 工具名（引擎侧契约名）。</summary>
    public const string ReadTool = "Read";

    /// <summary>Grep 工具名。</summary>
    public const string GrepTool = "Grep";

    /// <summary>Glob 工具名。</summary>
    public const string GlobTool = "Glob";

    /// <summary>Edit 工具名。</summary>
    public const string EditTool = "Edit";

    /// <summary>Write 工具名。</summary>
    public const string WriteTool = "Write";

    /// <summary>Bash 工具名。</summary>
    public const string BashTool = "Bash";

    /// <summary>
    /// 派生工具标题行主参数摘要 — 未知工具名降级返回 Args 原文（防御引擎新增工具名不崩）。
    /// </summary>
    public static string Format(string toolName, string args) => toolName switch
    {
        BashTool => FormatCommand(args),
        ReadTool or EditTool or WriteTool => FormatPath(args),
        GrepTool or GlobTool => FormatSearch(args),
        _ => args,
    };

    /// <summary>
    /// 派生 Edit 操作显示名 — Args 的 old_string 为空 = Create，否则 Update
    /// （FileEditTool/UI.tsx userFacingName；纯文本降级 Update — 基准 input null 同值）。
    /// </summary>
    public static string DeriveEditLabel(string args) =>
        TryGetStringField(args, "old_string") is { Length: 0 } ? "Create" : "Update";

    /// <summary>
    /// 派生工具显示名 — Edit 走 Create/Update（<see cref="DeriveEditLabel"/>），其余原名
    /// （FileEditTool/UI.tsx userFacingName — 基准 AgentTool/UI.tsx:852 userFacingToolName 同源）。
    /// 唯一数据源：ToolCallItem 标题行与 spinner lastToolInfo（TASK066 F4）共用，消双源漂移。
    /// </summary>
    public static string DisplayToolName(string toolName, string args) =>
        toolName is EditTool ? DeriveEditLabel(args) : toolName;

    /// <summary>
    /// spinner lastToolInfo 行文本 — 基准 AgentTool/UI.tsx:858 `${userFacingToolName}: ${summary}`
    /// 形态：显示名 + 主参数摘要首行（多行命令单行化 — Bash 两行命令取首行）；
    /// 未知工具 Args 原文超长按 <see cref="MaxCommandDisplayChars"/> 截断加 …（防御引擎新工具超长参数）。
    /// </summary>
    public static string FormatToolInfo(string toolName, string args)
    {
        var display = DisplayToolName(toolName, args);
        var firstLine = Format(toolName, args).Split('\n')[0];
        return firstLine.Length > MaxCommandDisplayChars
            ? $"{display}: {firstLine[..MaxCommandDisplayChars]}…"
            : $"{display}: {firstLine}";
    }

    /// <summary>
    /// 派生 Write 结果摘要句式 — "写入 N 行到 {path}"（FileWriteTool/UI.tsx "Wrote N lines to path" 汉化）。
    /// 供 Driver 组装 ToolResult Summary 消费（行数口径 = countLines 尾换行终结语义）。
    /// </summary>
    public static string WriteResultSummary(int lineCount, string path) => $"写入 {lineCount} 行到 {path}";

    /// <summary>
    /// 统计文件内容行数 — 尾部换行视为行终结符不计新行（FileWriteTool/UI.tsx countLines L35-38）。
    /// </summary>
    public static int CountLines(string content) =>
        content.EndsWith('\n') ? content.Split('\n').Length - 1 : content.Split('\n').Length;

    /// <summary>
    /// Bash 命令摘要 — 超 2 行先截行、仍超 160 字符再截字符，截断发生则尾加 …
    /// （BashTool/UI.tsx L104-127 needsLineTruncation/needsCharTruncation 两级截断）；
    /// 命令来自 JSON command 字段，提取失败时 Args 原文即命令（纯文本降级）。
    /// </summary>
    public static string FormatCommand(string args)
    {
        var command = TryGetStringField(args, "command") ?? args;
        var lines = command.Split('\n');
        var needsLineTruncation = lines.Length > MaxCommandDisplayLines;
        var needsCharTruncation = command.Length > MaxCommandDisplayChars;
        if (!needsLineTruncation && !needsCharTruncation)
        {
            return command;
        }

        var truncated = needsLineTruncation
            ? string.Join("\n", lines[..MaxCommandDisplayLines])
            : command;
        if (truncated.Length > MaxCommandDisplayChars)
        {
            truncated = truncated[..MaxCommandDisplayChars];
        }

        return $"{truncated.Trim()}…";
    }

    /// <summary>Read/Write/Edit 路径摘要 — JSON file_path/path 字段，纯文本降级原文（FileReadTool/UI.tsx displayPath）。</summary>
    private static string FormatPath(string args) =>
        TryGetStringField(args, "file_path", "path") ?? args;

    /// <summary>
    /// Grep/Glob 搜索摘要 — `pattern: "x"` + 可选 `, path: "y"` 引号包裹
    /// （GrepTool/UI.tsx L141-145 parts.join(', ')）。
    /// </summary>
    private static string FormatSearch(string args)
    {
        var pattern = TryGetStringField(args, "pattern");
        if (pattern is null)
        {
            return args;
        }

        var path = TryGetStringField(args, "path");
        return path is null
            ? $"pattern: \"{pattern}\""
            : $"pattern: \"{pattern}\", path: \"{path}\"";
    }

    /// <summary>
    /// JSON 对象字符串字段提取 — 依序尝试候选字段名取首个非 null 字符串值；
    /// Args 空/非 JSON/非对象/字段缺失返回 null（调用方降级）。using 释放 JsonDocument。
    /// internal = 同域聚合组件（ToolGroupAggregator hint 派生）与测试消费。
    /// </summary>
    internal static string? TryGetStringField(string args, params ReadOnlySpan<string> fieldNames)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(args);
            if (doc.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }

            foreach (var fieldName in fieldNames)
            {
                if (doc.RootElement.TryGetProperty(fieldName, out var value)
                    && value.ValueKind is JsonValueKind.String)
                {
                    return value.GetString();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
