namespace JoinCode.Abstractions.LLM;

/// <summary>
/// LLM 侧工具分组视图 — 同步操作，Plugin/Group 概念
/// 关系: 本接口是 IToolRegistry (03-hands) 的 LLM 侧只读投影，后者是执行侧完整注册表（异步+执行）
/// </summary>
public interface IToolCollection {
    /// <summary>按名称获取工具分组。</summary>
    IToolGroup? GetPlugin(string name);
    /// <summary>添加工具分组。</summary>
    void Add(IToolGroup plugin);
    /// <summary>按名称移除工具分组。</summary>
    bool Remove(string name);
    /// <summary>获取所有插件名称集合。</summary>
    IEnumerable<string> PluginNames { get; }
}
