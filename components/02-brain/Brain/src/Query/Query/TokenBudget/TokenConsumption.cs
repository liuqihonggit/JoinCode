namespace Core.Query;

/// <summary>
/// Token消耗信息
/// </summary>
public class TokenConsumption
{
    /// <summary>
    /// 消耗数量
    /// </summary>
    public long Amount { get; set; }

    /// <summary>
    /// 消耗原因
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 工具名称（可选）
    /// </summary>
    public string? ToolName { get; set; }
}
