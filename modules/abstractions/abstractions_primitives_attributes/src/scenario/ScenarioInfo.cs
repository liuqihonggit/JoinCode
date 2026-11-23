namespace JoinCode.Abstractions.Scenarios;

/// <summary>
/// 情景模式信息 — 一个情景模式包含一组工具 + 建议编排流程 + 提示。
/// 由 <c>ScenarioGenerator</c> 源码生成器从 <c>[Scenario]</c> 特性扫描收集，注入 ScenarioRegistry 静态注册表。
/// </summary>
/// <param name="Name">情景模式名称 — 唯一标识。</param>
/// <param name="Description">情景模式描述。</param>
/// <param name="Tools">涉及的工具集，按建议调用顺序排列。</param>
/// <param name="SuggestedFlow">建议编排流程。</param>
/// <param name="Tips">使用提示。</param>
public sealed record ScenarioInfo(
    string Name,
    string Description,
    string[] Tools,
    string SuggestedFlow,
    string Tips);
