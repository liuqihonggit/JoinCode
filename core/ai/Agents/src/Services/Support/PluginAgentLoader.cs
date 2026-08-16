namespace Core.Agents;

using System.Collections.Frozen;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// 插件 Agent 加载器 — 维护 Map&lt;name, (AgentDefinition, pluginName)&gt;
/// <para>对齐 Cordis 框架:</para>
/// <para>- 可逆效应: LoadFromPlugin 返回撤销函数，插件卸载时自动移除 agent 定义</para>
/// <para>- 响应式协效应: Changed 事件通知消费方缓存失效</para>
/// </summary>
[Register(typeof(IPluginAgentLoader))]
public sealed class PluginAgentLoader : ServiceEntity, IPluginAgentLoader
{
    private FrozenDictionary<string, (AgentDefinition Def, string PluginName)> _pluginAgents
        = FrozenDictionary<string, (AgentDefinition, string)>.Empty;

    /// <summary>插件 agent 集合变化事件 — 响应式协效应</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 加载插件 agent — 可逆效应，返回撤销函数
    /// <para>插件卸载时调用返回的 Action，自动移除该插件贡献的 agent</para>
    /// </summary>
    public Action LoadFromPlugin(string pluginName, IPluginAgentProvider provider)
    {
        var definitions = provider.GetAgentDefinitions();
        var addedKeys = new List<string>(definitions.Count);

        var map = new Dictionary<string, (AgentDefinition, string)>(_pluginAgents);
        foreach (var def in definitions)
        {
            PluginAgentValidator.Validate(def);
            map[def.DisplayId] = (def, pluginName);
            addedKeys.Add(def.DisplayId);
        }
        _pluginAgents = map.ToFrozenDictionary();
        Changed?.Invoke(this, EventArgs.Empty);

        return () =>
        {
            if (addedKeys.Count == 0) return;
            var unloadMap = new Dictionary<string, (AgentDefinition, string)>(_pluginAgents);
            foreach (var key in addedKeys)
            {
                if (unloadMap.TryGetValue(key, out var entry) && entry.Item2 == pluginName)
                    unloadMap.Remove(key);
            }
            _pluginAgents = unloadMap.ToFrozenDictionary();
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>获取所有插件 agent 定义</summary>
    public IReadOnlyList<AgentDefinition> GetAll()
    {
        var snapshot = _pluginAgents;
        var list = new List<AgentDefinition>(snapshot.Count);
        foreach (var kv in snapshot)
            list.Add(kv.Value.Def);
        return list;
    }

    /// <summary>按名查找</summary>
    public AgentDefinition? Find(string name)
    {
        return _pluginAgents.TryGetValue(name, out var entry) ? entry.Def : null;
    }
}
