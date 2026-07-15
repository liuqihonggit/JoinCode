using JoinCode.Abstractions.Attributes;

namespace Core.Skills.Discovery;

[Register]
public sealed partial class SkillDiscoveryOptions
{
    public string SkillsDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppDataConstants.AppDataFolder,
        "skills");

    public bool EnableFileWatching { get; init; } = true;

    public int WatchDebounceMs { get; init; } = 500;

    public bool ValidateOnLoad { get; init; } = true;

    public IReadOnlyList<string> SupportedExtensions { get; init; } = new List<string> { ".json", ".md" }.AsReadOnly();

    public SkillDiscoveryOptions() { }

    public SkillDiscoveryOptions(WorkflowConfig? config)
    {
        if (config is not null && !string.IsNullOrEmpty(config.SkillsDirectory))
        {
            SkillsDirectory = config.SkillsDirectory;
        }
    }
}
