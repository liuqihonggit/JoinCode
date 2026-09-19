namespace Tools.Handlers;

/// <summary>
/// 桌面情景模式 click 工具处理器 — 点击指定坐标，场景完成后推荐 look 继续下一步
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public sealed class DesktopSceneClickToolHandlers {
    private readonly IDesktopInputService _inputService;
    private readonly IDesktopSceneStateStore _stateStore;

    /// <summary>
    /// 初始化 click 工具处理器
    /// </summary>
    /// <param name="inputService">桌面输入服务</param>
    /// <param name="stateStore">场景状态存储</param>
    public DesktopSceneClickToolHandlers(IDesktopInputService inputService, IDesktopSceneStateStore stateStore) {
        _inputService = inputService ?? throw new ArgumentNullException(nameof(inputService));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    /// <summary>
    /// 点击指定坐标 — 场景完成后推荐 desktop_look 继续下一步操作
    /// </summary>
    /// <param name="sceneId">场景 ID</param>
    /// <param name="x">X 坐标</param>
    /// <param name="y">Y 坐标</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果，含点击结果 + suggested_next(推荐 look 继续下一步)</returns>
    [McpTool("desktop_click", "点击指定坐标。场景完成后调 desktop_look 继续下一步操作。", "desktop")]
    public async Task<ToolResult> ClickAsync(
        [McpToolParameter("场景 ID", Required = true)] string sceneId,
        [McpToolParameter("X 坐标", Required = true)] int x,
        [McpToolParameter("Y 坐标", Required = true)] int y,
        CancellationToken cancellationToken = default) {
        var op = await _inputService.ClickAsync(x, y, MouseAction.Click, cancellationToken).ConfigureAwait(false);

        var state = await _stateStore.LoadAsync(sceneId, cancellationToken).ConfigureAwait(false);
        if (state is not null) {
            var newState = state with { LastAction = $"click({x},{y})" };
            await _stateStore.SaveAsync(newState, cancellationToken).ConfigureAwait(false);
        }

        if (!op.Succeeded) {
            var errorJson = $$"""
                {
                  "scene_id": "{{sceneId}}",
                  "succeeded": false,
                  "error": "{{op.Error}}",
                  "suggested_next": [{
                    "tool": "desktop_look",
                    "reason": "点击失败，重新截图确认目标位置"
                  }]
                }
                """;
            return ToolResultBuilder.Error().WithText(errorJson).Build();
        }

        var json = $$"""
            {
              "scene_id": "{{sceneId}}",
              "succeeded": true,
              "x": {{x}},
              "y": {{y}},
              "suggested_next": [{
                "tool": "desktop_look",
                "reason": "点击完成，重新截图查看结果或继续下一步"
              }]
            }
            """;
        return ToolResultBuilder.Success().WithText(json).Build();
    }
}