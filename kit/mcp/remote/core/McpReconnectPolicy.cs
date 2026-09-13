namespace McpToolRegistry;

/// <summary>
/// MCP 重连策略 — 根据工具漂移报告和接受级别决策是否接受同步结果
/// </summary>
public static class McpReconnectPolicy
{
    /// <summary>
    /// 根据漂移报告和接受级别决策是否接受同步
    /// </summary>
    /// <param name="driftReport">工具漂移报告</param>
    /// <param name="acceptLevel">重连接受级别</param>
    /// <returns>重连决策结果</returns>
    public static McpReconnectResult Decide(ToolDriftReport driftReport, McpReconnectAcceptLevel acceptLevel)
    {
        ArgumentNullException.ThrowIfNull(driftReport);

        return driftReport.Kind switch
        {
            ToolDriftKind.Identity => new McpReconnectResult
            {
                Accepted = true,
                DriftKind = driftReport.Kind,
                Reason = string.Empty
            },

            ToolDriftKind.Append => acceptLevel >= McpReconnectAcceptLevel.IdentityAndAppend
                ? new McpReconnectResult
                {
                    Accepted = true,
                    DriftKind = driftReport.Kind,
                    Reason = string.Empty
                }
                : new McpReconnectResult
                {
                    Accepted = false,
                    DriftKind = driftReport.Kind,
                    Reason = $"Append drift rejected: accept level is {acceptLevel}. {driftReport.Summary}"
                },

            ToolDriftKind.Reorder => acceptLevel >= McpReconnectAcceptLevel.IdentityAppendAndReorder
                ? new McpReconnectResult
                {
                    Accepted = true,
                    DriftKind = driftReport.Kind,
                    Reason = "Reorder accepted: stable sorting normalizes tool order for prefix cache"
                }
                : new McpReconnectResult
                {
                    Accepted = false,
                    DriftKind = driftReport.Kind,
                    Reason = $"Reorder drift rejected: accept level is {acceptLevel}. {driftReport.Summary}"
                },

            ToolDriftKind.Edit => new McpReconnectResult
            {
                Accepted = false,
                DriftKind = driftReport.Kind,
                Reason = $"Edit drift rejected (cache impact: moderate). {driftReport.Summary}"
            },

            ToolDriftKind.Remove => new McpReconnectResult
            {
                Accepted = false,
                DriftKind = driftReport.Kind,
                Reason = $"Remove drift rejected (cache impact: catastrophic). {driftReport.Summary}"
            },

            _ => new McpReconnectResult
            {
                Accepted = false,
                DriftKind = driftReport.Kind,
                Reason = $"Unknown drift kind rejected. {driftReport.Summary}"
            }
        };
    }
}
