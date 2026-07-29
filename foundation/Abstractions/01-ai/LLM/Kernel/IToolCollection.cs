namespace JoinCode.Abstractions.LLM;

/// <summary>
/// LLM 侧工具分组视图 — 同步操作，Plugin/Group 概念
/// 关系: 本接口是 IToolRegistry (03-hands) 的 LLM 侧只读投影，后者是执行侧完整注册表（异步+执行）
/// </summary>
public interface IToolCollection
{
    IToolGroup? GetPlugin(string name);
    void Add(IToolGroup plugin);
    bool Remove(string name);
    IReadOnlyList<string> PluginNames { get; }
}
