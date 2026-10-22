namespace NativeTui.Core.Repl.Keybindings;

/// <summary>
/// 键位校验错误种类 — 对齐基准 validate.ts KeybindingWarningType
/// （parse_error→JsonSyntax、invalid_context→UnknownContext、invalid_action→UnknownAction、
/// parse_error 键名→UnknownKey、reserved→ReservedKey）+ IO 读取失败（IoRead，w2 补充）。
/// </summary>
public enum KeybindingErrorKind
{
    /// <summary>JSON 语法/结构非法（JsonException — 附行列信息）。</summary>
    JsonSyntax,

    /// <summary>未知 context 名（不在 20 个有效上下文清单内）。</summary>
    UnknownContext,

    /// <summary>未知 action 名（不在 105 个有效动作清单内）。</summary>
    UnknownAction,

    /// <summary>键位串解析失败（未知键名/修饰符/结构 — 见 KeyShortcut/KeyChord 解析层）。</summary>
    UnknownKey,

    /// <summary>保留键重绑（ctrl+c/ctrl+d/ctrl+m 不可重绑）。</summary>
    ReservedKey,

    /// <summary>配置文件读取失败（IO/权限 — 本次忽略用户自定义回退默认表）。</summary>
    IoRead,
}

/// <summary>
/// 键位校验错误 — 消息文本内建四要素（AGENTS 规范：为什么拒绝/哪个值触发/哪层拦截/正确写法），
/// 风格对齐项目 Rust 编译器报错理念（值定位 + 代码片段式引导）；
/// 语法错误附行列（1-based，来自 JsonException），语义错误以 context 块名定位（DTO 反序列化后不保留行号）。
/// </summary>
/// <param name="Kind">错误种类（拦截层判定用）。</param>
/// <param name="Message">完整四要素消息文本。</param>
/// <param name="Line">1-based 行号（仅 JsonSyntax）。</param>
/// <param name="Column">1-based 列号（仅 JsonSyntax）。</param>
public sealed record KeybindingError(KeybindingErrorKind Kind, string Message, int? Line = null, int? Column = null)
{
    /// <summary>JSON 语法错误（JsonException 携带 0-based 行列 → 转 1-based；附正确格式示例）。</summary>
    /// <param name="exception">反序列化抛出的 JSON 异常。</param>
    /// <returns>带行列的错误。</returns>
    public static KeybindingError JsonSyntax(JsonException exception) => new(
        KeybindingErrorKind.JsonSyntax,
        $"keybindings.json 语法错误（加载解析层拦截）：{exception.Message}。正确格式示例：{{\"chat\": {{\"chat:externalEditor\": [\"ctrl+g\", \"ctrl+x ctrl+e\"]}}}}（context 小写名 → action 原名 → 键位数组；支持 // 注释与尾逗号）",
        (int?)exception.LineNumber + 1,
        (int?)exception.BytePositionInLine + 1);

    /// <summary>未知 context（附全部有效 context 清单 — 正确写法引导）。</summary>
    /// <param name="contextName">用户 json 中的 context 键名。</param>
    /// <returns>错误。</returns>
    public static KeybindingError UnknownContext(string contextName) => new(
        KeybindingErrorKind.UnknownContext,
        $"未知 context \"{contextName}\"（键位加载校验层拦截）：不在 {KeybindingNames.Contexts.Length} 个有效上下文清单内（大小写不敏感匹配失败）。{KeybindingNames.ContextsHint}。正确写法示例：\"chat\"（全局兜底用 \"global\"）");

    /// <summary>未知 action（附同前缀可用 action 清单或全部有效前缀 — 正确写法引导）。</summary>
    /// <param name="context">所在上下文（定位提示）。</param>
    /// <param name="actionName">用户 json 中的 action 键名。</param>
    /// <returns>错误。</returns>
    public static KeybindingError UnknownAction(KeybindingContext context, string actionName) => new(
        KeybindingErrorKind.UnknownAction,
        $"未知 action \"{actionName}\"（{context} 块，键位加载校验层拦截）：不在 {KeybindingNames.Actions.Length} 个有效动作清单内（大小写不敏感匹配失败）。{KeybindingNames.SuggestActions(actionName)}。正确写法示例：\"chat:externalEditor\"");

    /// <summary>键位串解析失败（透传 KeyShortcut/KeyChord 解析层错误文本 — 已含四要素与段定位）。</summary>
    /// <param name="context">所在上下文。</param>
    /// <param name="action">目标动作（原名定位）。</param>
    /// <param name="keyText">用户键位串原文。</param>
    /// <param name="parseError">解析层错误文本。</param>
    /// <returns>错误。</returns>
    public static KeybindingError UnknownKey(KeybindingContext context, KeybindingAction action, string keyText, string parseError) => new(
        KeybindingErrorKind.UnknownKey,
        $"键位 \"{keyText}\" 无法解析（{context} 块 {action.ToName()}，键位加载校验层拦截）：{parseError}");

    /// <summary>保留键重绑（附保留原因与替代建议 — 正确写法引导）。</summary>
    /// <param name="context">所在上下文。</param>
    /// <param name="action">目标动作。</param>
    /// <param name="keyText">用户键位串原文。</param>
    /// <param name="reason">保留原因（ReservedShortcuts）。</param>
    /// <returns>错误。</returns>
    public static KeybindingError ReservedKey(KeybindingContext context, KeybindingAction action, string keyText, string reason) => new(
        KeybindingErrorKind.ReservedKey,
        $"保留键 \"{keyText}\" 不可重绑（{context} 块 {action.ToName()}，键位加载校验层拦截）：{reason}。请为该动作换一个键位，示例：\"ctrl+g\"；退出/中断语义由程序内置，无需用户绑定");

    /// <summary>IO 读取失败（本次回退默认表 — 引导检查权限）。</summary>
    /// <param name="path">配置文件路径。</param>
    /// <param name="reason">底层异常消息。</param>
    /// <returns>错误。</returns>
    public static KeybindingError IoRead(string path, string reason) => new(
        KeybindingErrorKind.IoRead,
        $"keybindings.json 读取失败（加载 IO 层拦截）：{reason}。本次忽略用户自定义、使用默认键位表。路径：{path}；请检查文件是否被占用或无读取权限后重启");

    /// <summary>带行列前缀的显示文本（语法错误附 "（第 x 行第 y 列）"）。</summary>
    /// <returns>可展示文本。</returns>
    public override string ToString() => Line is { } line
        ? $"{Message}（第 {line} 行第 {Column} 列）"
        : Message;
}
