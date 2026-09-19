namespace Core.Context;

/// <summary>
/// 易失性草稿区，存放当前会话的推理过程、计划状态和临时备注，会话结束即丢弃
/// </summary>
public sealed class VolatileScratch {
    /// <summary>
    /// 推理过程文本
    /// </summary>
    public string? Reasoning { get; set; }

    /// <summary>
    /// 计划状态字典，键为状态名，值为 JSON 元素
    /// </summary>
    public Dictionary<string, JsonElement> PlanState { get; set; } = [];

    /// <summary>
    /// 临时备注列表
    /// </summary>
    public List<string> Notes { get; } = [];

    /// <summary>
    /// 重置草稿区，清空推理、计划状态和备注
    /// </summary>
    public void Reset() {
        Reasoning = null;
        PlanState = [];
        Notes.Clear();
    }
}