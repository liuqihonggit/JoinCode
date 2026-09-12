namespace JoinCode.Tui.Tui;

/// <summary>问答选择输入解析状态</summary>
public enum AskUserSelectionStatus
{
    /// <summary>解析成功 — Indices 携带 1-based 选中序号</summary>
    Ok,

    /// <summary>用户取消（输入 0 或空白）</summary>
    Cancel,

    /// <summary>无效输入（越界/非数字/空 token）— 需提示后重新输入</summary>
    Invalid
}

/// <summary>
/// TUI 问答选择解析结果。
/// </summary>
/// <param name="Status">解析状态。</param>
/// <param name="Indices">选中的 1-based 选项序号（去重升序）；仅 Status=Ok 时有效。</param>
public sealed record AskUserSelectionResult(AskUserSelectionStatus Status, IReadOnlyList<int> Indices)
{
    /// <summary>取消快捷构造。</summary>
    public static AskUserSelectionResult Cancelled { get; } = new(AskUserSelectionStatus.Cancel, []);

    /// <summary>无效快捷构造。</summary>
    public static AskUserSelectionResult InvalidInput { get; } = new(AskUserSelectionStatus.Invalid, []);
}

/// <summary>
/// TUI 问答对话框的序号输入解析 — 纯函数无 UI 依赖，语义对齐 CLI TerminalInteractiveService：
/// 单选取单个序号；多选逗号分隔可去重；0 或空白 = 取消；越界/非数字 = 无效需重试。
/// </summary>
public static class AskUserSelectionParser
{
    /// <summary>
    /// 解析用户输入的选择文本。
    /// </summary>
    /// <param name="input">TextField 原始文本（可能含首尾空白）。</param>
    /// <param name="maxOptions">选项总数（1-based 上界）。</param>
    /// <param name="multiSelect">是否多选（逗号分隔多个序号）。</param>
    public static AskUserSelectionResult Parse(string input, int maxOptions, bool multiSelect)
    {
        var trimmed = input.Trim();

        // 无选项场景（服务层自由输入分流失败才会到这里）— 任何输入都视为无效
        if (maxOptions <= 0)
            return AskUserSelectionResult.InvalidInput;

        if (trimmed.Length == 0 || trimmed == "0")
            return AskUserSelectionResult.Cancelled;

        var tokens = multiSelect
            ? trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [trimmed];

        var indices = new List<int>();
        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out var idx) || idx < 1 || idx > maxOptions)
                return AskUserSelectionResult.InvalidInput;
            indices.Add(idx);
        }

        if (indices.Count == 0)
            return AskUserSelectionResult.Cancelled;

        return new AskUserSelectionResult(
            AskUserSelectionStatus.Ok,
            indices.Distinct().OrderBy(i => i).ToList());
    }
}
