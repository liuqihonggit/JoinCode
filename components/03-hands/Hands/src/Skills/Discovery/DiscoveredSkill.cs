
namespace Core.Skills.Discovery;

public sealed record DiscoveredSkill
{
    public required string Name { get; init; }
    public required string SourcePath { get; init; }
    public required SkillSourceFormat SourceFormat { get; init; }
    public required DateTime LastModified { get; init; }
    public required SkillDefinition Definition { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ValidationWarnings { get; init; } = Array.Empty<string>();
    public bool IsValid => ValidationErrors.Count == 0;
}
