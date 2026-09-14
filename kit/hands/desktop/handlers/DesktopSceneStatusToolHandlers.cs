namespace Tools.Handlers;

/// <summary>
/// 桌面情景模式 status 工具处理器 — 查看夹逼进度（当前层/格子/历史路径）
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public sealed class DesktopSceneStatusToolHandlers
{
    private readonly IDesktopSceneStateStore _stateStore;

    /// <summary>
    /// 初始化 status 工具处理器
    /// </summary>
    /// <param name="stateStore">场景状态存储服务</param>
    public DesktopSceneStatusToolHandlers(IDesktopSceneStateStore stateStore)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    /// <summary>
    /// 查看当前夹逼进度 — 返回当前层/格子/历史路径，用于纠偏或恢复
    /// </summary>
    /// <param name="sceneId">场景 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果，含夹逼状态 JSON</returns>
    [McpTool("desktop_scene_status", "查看当前夹逼进度：当前层/格子/历史路径。用于纠偏或跨调用恢复。", "desktop")]
    public async Task<ToolResult> StatusAsync(
        [McpToolParameter("场景 ID", Required = true)] string sceneId,
        CancellationToken cancellationToken = default)
    {
        var state = await _stateStore.LoadAsync(sceneId, cancellationToken).ConfigureAwait(false);
        if (state is null)
            return ToolResultBuilder.Success().WithText($"场景 {sceneId} 不存在，请先调 desktop_look 创建场景。").Build();

        var historyJson = string.Join(", ", state.ZoomHistory.Select(h =>
            "{ \"depth\": " + h.Depth + ", \"cell\": \"" + h.CellCode + "\", \"quadrant\": " + h.Quadrant + " }"));

        var json = $$"""
            {
              "scene_id": "{{state.SceneId}}",
              "current_depth": {{state.CurrentDepth}},
              "current_cell": "{{state.CurrentCellCode}}",
              "last_action": "{{state.LastAction ?? ""}}",
              "zoom_history": [{{historyJson}}],
              "suggested_next": [{
                "tool": "desktop_zoom",
                "reason": "继续夹逼或退层重新选象限"
              }]
            }
            """;
        return ToolResultBuilder.Success().WithText(json).Build();
    }
}
