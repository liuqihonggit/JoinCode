
namespace Core.Skills.Plugin;

public interface IPluginSkillBridge : IDisposable
{
    /// <summary>注册插件技能 — 返回撤销函数(可逆效应)</summary>
    Task<Action> RegisterPluginSkillsAsync(string pluginName, CancellationToken cancellationToken = default);

    Task UnregisterPluginSkillsAsync(string pluginName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SkillDefinition>> GetPluginSkillsAsync(string pluginName);

    IEnumerable<string> GetPluginsWithSkills();
}
