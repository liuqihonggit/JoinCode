namespace Core.Agents;

using System.Collections.Frozen;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// 插件 Agent 加载器 — 维护 Map&lt;name, (AgentDefinition, pluginName)&gt;
/// <para>对齐 Cordis 框架:</para>
/// <para>- 可逆效应: LoadFromPlugin 返回撤销函数，按注册逆序执行连带卸载</para>
/// <para>- 响应式协效应: Changed 事件通知消费方缓存失效</para>
/// <para>- Theorem 63 (Ordering): 卸载提供者时，先连带卸载所有依赖方，最后卸载提供者本身</para>
/// </summary>
[Register(typeof(IPluginAgentLoader))]
public sealed class PluginAgentLoader : ServiceEntity, IPluginAgentLoader
{
    private FrozenDictionary<string, (AgentDefinition Def, string PluginName)> _pluginAgents
        = FrozenDictionary<string, (AgentDefinition, string)>.Empty;

    /// <summary>
    /// 撤销链 — 按注册顺序记录，卸载时按逆序遍历（Cordis Effect 系统）
    /// <para>每项记录插件名和该插件贡献的 agent key 列表</para>
    /// </summary>
    private readonly List<(string PluginName, List<string> AgentKeys)> _loadOrder = new();

    /// <summary>插件 agent 集合变化事件 — 响应式协效应</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 加载插件 agent — 可逆效应，返回撤销函数
    /// <para>插件卸载时调用返回的 Action，触发连带卸载（Cordis Theorem 63）</para>
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
        _loadOrder.Add((pluginName, addedKeys));
        Changed?.Invoke(this, EventArgs.Empty);

        return () => UnloadWithCascade(pluginName);
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

    /// <summary>
    /// 连带卸载 — 对齐 Cordis Theorem 63 (Ordering):
    /// <para>1. 找到被卸载插件贡献的 agent 名集合（提供者 agent）</para>
    /// <para>2. 按注册逆序遍历，先连带卸载所有依赖提供者 agent 的消费者插件</para>
    /// <para>3. 最后卸载提供者本身（Theorem 63: 提供者最后卸载）</para>
    /// </summary>
    private void UnloadWithCascade(string pluginName)
    {
        // 1. 找到此插件贡献的 agent 名集合
        var providerAgentNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in _loadOrder)
        {
            if (entry.PluginName == pluginName)
            {
                foreach (var key in entry.AgentKeys)
                    providerAgentNames.Add(key);
            }
        }

        if (providerAgentNames.Count == 0)
            return;

        // 2. 标记需要卸载的插件名（连带依赖 + 提供者本身）— 不动点迭代支持传递依赖
        var pluginsToRemove = new HashSet<string>(StringComparer.Ordinal) { pluginName };
        // 被卸载的 agent 名集合（随连带卸载动态扩展，支持传递依赖）
        var removedAgentNames = new HashSet<string>(providerAgentNames, StringComparer.Ordinal);

        // 不动点迭代：每轮按注册逆序遍历，标记新依赖方，直到无新标记
        bool changed;
        do
        {
            changed = false;
            for (int i = _loadOrder.Count - 1; i >= 0; i--)
            {
                var entry = _loadOrder[i];
                if (pluginsToRemove.Contains(entry.PluginName))
                    continue;

                if (PluginDependsOnAgents(entry.PluginName, removedAgentNames))
                {
                    pluginsToRemove.Add(entry.PluginName);
                    foreach (var key in entry.AgentKeys)
                        removedAgentNames.Add(key);
                    changed = true;
                }
            }
        } while (changed);

        // 3. 从 Map 中移除所有被标记插件的 agent（Theorem 63: 提供者最后卸载）
        var unloadMap = new Dictionary<string, (AgentDefinition, string)>(_pluginAgents);
        var newLoadOrder = new List<(string PluginName, List<string> AgentKeys)>(_loadOrder.Count);
        foreach (var entry in _loadOrder)
        {
            if (pluginsToRemove.Contains(entry.PluginName))
            {
                foreach (var key in entry.AgentKeys)
                {
                    if (unloadMap.TryGetValue(key, out var existing) && existing.Item2 == entry.PluginName)
                        unloadMap.Remove(key);
                }
            }
            else
            {
                newLoadOrder.Add(entry);
            }
        }
        _pluginAgents = unloadMap.ToFrozenDictionary();
        _loadOrder.Clear();
        _loadOrder.AddRange(newLoadOrder);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 检查插件消费者的 agent 是否依赖被卸载的 agent 集合（Reactive Coeffects）
    /// <para>依赖关系分析:</para>
    /// <para>1. 消费者 agent 的 Skills 列表引用了被卸载的 agent 名</para>
    /// <para>2. 消费者 agent 的 Tools 列表引用了被卸载的 agent 名（agent 专属工具）</para>
    /// </summary>
    private bool PluginDependsOnAgents(string consumerPlugin, HashSet<string> providerAgentNames)
    {
        var snapshot = _pluginAgents;
        foreach (var kv in snapshot)
        {
            if (kv.Value.PluginName != consumerPlugin)
                continue;

            var agent = kv.Value.Def;

            // 检查1: Skills 引用了被卸载的 agent 名
            if (agent.Skills is not null)
            {
                foreach (var skill in agent.Skills)
                {
                    if (providerAgentNames.Contains(skill))
                        return true;
                }
            }

            // 检查2: Tools 引用了被卸载的 agent 名（agent 专属工具，如 "agent:worker"）
            if (agent.Tools is not null)
            {
                foreach (var tool in agent.Tools)
                {
                    if (providerAgentNames.Contains(tool))
                        return true;
                }
            }
        }
        return false;
    }
}
