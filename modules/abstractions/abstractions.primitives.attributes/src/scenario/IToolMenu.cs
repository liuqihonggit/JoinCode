namespace JoinCode.Abstractions.Scenarios;

/// <summary>
/// 工具菜单提供者接口 — 实现此接口的 handler 声明自身为情景模式菜单入口，
/// 通过 ScenarioRegistry 提供菜单数据，由 <c>ToolMenuRenderer</c> 统一渲染。
/// </summary>
public interface IToolMenu {
    /// <summary>获取该菜单对应的情景模式信息。</summary>
    ScenarioInfo GetScenario();
}
