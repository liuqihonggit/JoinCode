namespace NativeTui.Core.Repl;

/// <summary>斜杠命令建议项 — 宿主注入的纯数据（name 不含 / 前缀），NativeTui.Core 零依赖桥接形态。</summary>
/// <param name="Name">命令名（不含 /）。</param>
/// <param name="Description">单行描述（建议列表右列）。</param>
/// <param name="Aliases">别名（精确/前缀/子序列切名匹配用）。</param>
/// <param name="IsHidden">隐藏命令（默认不参与建议，输入全名时前插）。</param>
public sealed record CommandSuggestion(string Name, string Description, string[] Aliases, bool IsHidden);
