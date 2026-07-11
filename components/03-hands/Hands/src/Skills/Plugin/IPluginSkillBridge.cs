
namespace Core.Skills.Plugin;

public interface IPluginSkillBridge : IDisposable
{
    Task RegisterPluginSkillsAsync(string pluginName, CancellationToken cancellationToken = default);

    Task UnregisterPluginSkillsAsync(string pluginName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SkillDefinition>> GetPluginSkillsAsync(string pluginName);

    IReadOnlyList<string> GetPluginsWithSkills();
}
