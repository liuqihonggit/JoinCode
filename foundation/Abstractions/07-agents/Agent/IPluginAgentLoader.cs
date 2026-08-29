namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 插件 Agent 加载器 — 维护 Map&lt;name, AgentDefinition&gt;，支持可逆效应和响应式协效应
/// <para>对齐 Cordis 框架:</para>
/// <para>- 可逆效应: LoadFromPlugin 返回撤销函数，插件卸载时自动移除 agent 定义</para>
/// <para>- 响应式协效应: Changed 事件通知消费方缓存失效</para>
/// </summary>
public interface IPluginAgentLoader
{
    /// <summary>插件 agent 集合变化事件 — 响应式协效应</summary>
    event EventHandler? Changed;

    /// <summary>
    /// 加载插件 agent — 可逆效应，返回撤销函数
    /// <para>插件卸载时调用返回的 Action，自动移除该插件贡献的 agent</para>
    /// </summary>
    Action LoadFromPlugin(string pluginName, IPluginAgentProvider provider);

    /// <summary>获取所有插件 agent 定义</summary>
    IReadOnlyList<AgentDefinition> GetAll();

    /// <summary>按名查找</summary>
    AgentDefinition? Find(string name);
}
