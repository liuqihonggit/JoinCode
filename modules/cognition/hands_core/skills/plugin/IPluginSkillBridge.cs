
namespace Core.Skills.Plugin;

/// <summary>
/// 插件技能桥接接口 — 管理插件技能的注册、注销和查询
/// </summary>
public interface IPluginSkillBridge : IAsyncDisposable {
    /// <summary>注册插件技能 — 返回撤销函数(可逆效应)</summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>撤销注册的函数，调用后注销已注册的插件技能</returns>
    Task<Action> RegisterPluginSkillsAsync(string pluginName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步注销插件技能
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task UnregisterPluginSkillsAsync(string pluginName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取指定插件的技能列表
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <returns>插件技能定义列表</returns>
    Task<IReadOnlyList<SkillDefinition>> GetPluginSkillsAsync(string pluginName);

    /// <summary>
    /// 获取所有已注册技能的插件名称的快照拷贝
    /// </summary>
    /// <returns>插件名称数组快照</returns>
    string[] GetPluginsWithSkills();
}