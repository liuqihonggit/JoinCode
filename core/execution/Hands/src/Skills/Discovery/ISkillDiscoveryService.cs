
namespace Core.Skills.Discovery;

public interface ISkillDiscoveryService : IDisposable
{
    Task<IReadOnlyList<DiscoveredSkill>> DiscoverAsync(CancellationToken cancellationToken = default);

    Task<DiscoveredSkill?> LoadSkillAsync(string skillName, CancellationToken cancellationToken = default);

    Task<SkillValidationResult> ValidateSkillAsync(string filePath, CancellationToken cancellationToken = default);

    Task StartWatchingAsync(CancellationToken cancellationToken = default);

    void StopWatching();

    event EventHandler<SkillDiscoveredEventArgs>? SkillDiscovered;

    event EventHandler<SkillChangedEventArgs>? SkillChanged;

    event EventHandler<SkillRemovedEventArgs>? SkillRemoved;
}

public sealed class SkillDiscoveredEventArgs : EventArgs
{
    public required DiscoveredSkill Skill { get; init; }
}

public sealed class SkillChangedEventArgs : EventArgs
{
    public required DiscoveredSkill Skill { get; init; }
}

public sealed class SkillRemovedEventArgs : EventArgs
{
    public required string SkillName { get; init; }
    public required string SourcePath { get; init; }
}

public sealed class SkillValidationResult
{
    public required string FilePath { get; init; }
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public SkillDefinition? SkillDefinition { get; init; }

    public static SkillValidationResult Success(string filePath, SkillDefinition definition, IReadOnlyList<string>? warnings = null)
        => new()
        {
            FilePath = filePath,
            IsValid = true,
            SkillDefinition = definition,
            Warnings = warnings ?? Array.Empty<string>()
        };

    public static SkillValidationResult Failure(string filePath, IReadOnlyList<string> errors, IReadOnlyList<string>? warnings = null)
        => new()
        {
            FilePath = filePath,
            IsValid = false,
            Errors = errors,
            Warnings = warnings ?? Array.Empty<string>()
        };
}
