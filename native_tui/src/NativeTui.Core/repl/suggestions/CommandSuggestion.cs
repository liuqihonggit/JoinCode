namespace NativeTui.Core.Repl;

/// <summary>斜杠命令建议项 — 宿主注入的纯数据（name 不含 / 前缀），NativeTui.Core 零依赖桥接形态。</summary>
/// <param name="Name">命令名（不含 /）。</param>
/// <param name="Description">单行描述（建议列表右列）。</param>
/// <param name="Aliases">别名（精确/前缀/子序列切名匹配用）。</param>
/// <param name="IsHidden">隐藏命令（默认不参与建议，输入全名时前插）。</param>
/// <param name="ArgumentHint">参数提示（命令后空格时显示的单条提示，如 "model-id"；无静态参数清单的命令用）。</param>
/// <param name="Arguments">静态参数值清单（如 theme 的 dark/light/auto — 参数模式前缀过滤用；null 视为空）。</param>
/// <param name="Source">命令来源（空查询分组视图分组用 — builtin/user/project/policy；默认内置，宿主接线时注入）。</param>
public sealed record CommandSuggestion(
    string Name,
    string Description,
    string[] Aliases,
    bool IsHidden,
    string ArgumentHint = "",
    string[]? Arguments = null,
    CommandSource Source = CommandSource.Builtin)
{
    /// <summary>静态参数值清单（null 安全化 — 空数组）。</summary>
    public string[] ArgumentValues => Arguments ?? [];
}
