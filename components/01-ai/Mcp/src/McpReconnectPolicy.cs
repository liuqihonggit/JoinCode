namespace JoinCode.Abstractions.LLM.Chat;

public static class McpReconnectPolicy
{
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
