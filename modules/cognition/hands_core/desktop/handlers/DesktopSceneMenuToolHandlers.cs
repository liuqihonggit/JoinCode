namespace Tools.Handlers;

/// <summary>
/// 桌面情景模式菜单工具处理器 — AI 通过菜单发现桌面操作场景的工具集与编排流程
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
[Scenario("desktop", "桌面操作场景：通过四叉树夹逼法定位屏幕元素并点击/输入/拖拽。看不清就反复 zoom，每次选 1/2/3/4 缩小到目标所在象限。",
    Tools = new[] { "desktop_look", "desktop_zoom", "desktop_detect", "desktop_click", "desktop_type", "desktop_drag" },
    SuggestedFlow = "look → zoom(重复直到看清) → detect → click",
    Tips = "看不清就反复 zoom，每次选 1/2/3/4 缩小到目标所在象限。点错了调 desktop_scene_status 看历史再 zoom back。")]
public sealed class DesktopSceneMenuToolHandlers : IToolMenu {
    /// <summary>
    /// 获取该菜单对应的情景模式信息 — 从 ScenarioRegistry 查询，数据源唯一。
    /// </summary>
    public ScenarioInfo GetScenario()
        => ScenarioRegistry.Find("desktop")
           ?? throw new InvalidOperationException("desktop scenario not registered in ScenarioRegistry");

    /// <summary>
    /// 获取桌面操作场景菜单 — 返回场景清单、工具集、建议流程与提示
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果，含场景菜单 JSON</returns>
    [McpTool("desktop_scene_menu", "桌面操作场景入口。调用获取可用动作菜单、场景说明（四叉树夹逼法）、建议编排流程。AI 通过此菜单发现桌面操作工具集。", "desktop")]
    public Task<ToolResult> SceneMenuAsync(CancellationToken cancellationToken = default) {
        var menu = ToolMenuRenderer.ToJson(GetScenario());
        return Task.FromResult(ToolResultBuilder.Success().WithText(menu).Build());
    }
}