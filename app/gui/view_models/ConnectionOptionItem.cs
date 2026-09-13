namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 连接下拉候选 — 表示一个可切换的引擎连接（Mock 演示引擎或真实供应商引擎）。
/// 让用户随时知道当前会话背后是 Mock 服务器还是真实环境。
/// </summary>
public sealed class ConnectionOptionItem
{
    public required string Id { get; init; }

    /// <summary>下拉展示文本（如 "🧪 Mock 引擎（演示）" 或 "DeepSeek（真实）"）</summary>
    public required string DisplayText { get; init; }

    /// <summary>是否为 Mock 引擎（驱动状态提示与切换行为）</summary>
    public bool IsMock { get; init; }

    public override bool Equals(object? obj)
        => obj is ConnectionOptionItem other && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Id);
}
