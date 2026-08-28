namespace JoinCode.Abstractions.Interfaces;

using JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// 插件 Agent 定义提供者 — 插件实现此接口以贡献 agent 定义
/// <para>对齐 TS 原版 loadPluginAgents: 从插件加载 agent 定义 + 安全限制</para>
/// <para>对齐 Cordis 框架声明式依赖: 插件声明"我提供什么"，框架管理生命周期</para>
/// </summary>
public interface IPluginAgentProvider
{
    /// <summary>获取插件提供的 agent 定义列表</summary>
    IReadOnlyList<AgentDefinition> GetAgentDefinitions();
}
