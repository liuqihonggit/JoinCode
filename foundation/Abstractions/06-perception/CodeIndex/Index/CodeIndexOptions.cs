using JoinCode.Abstractions.Attributes;

namespace JoinCode.Abstractions.CodeIndex;

[Register(typeof(CodeIndexOptions), ServiceLifetime.Singleton)]
public sealed record CodeIndexOptions
{
    public string WorkspaceRoot { get; init; } = Environment.CurrentDirectory;
    public bool EnableL1 { get; init; } = true;
    public bool EnableL2 { get; init; } = true;
    public int MaxMemoryMB { get; init; } = 600;
    public IEnumerable<string> FilePatterns { get; init; } = new[] { "*.cs" };
    public IEnumerable<string> ExcludePatterns { get; init; } = new[] { "bin/", "obj/", ".git/", ".x/" };
}
