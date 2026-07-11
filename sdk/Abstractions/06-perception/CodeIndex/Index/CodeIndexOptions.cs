using JoinCode.Abstractions.Attributes;

namespace JoinCode.Abstractions.CodeIndex;

[Register]
public sealed record CodeIndexOptions
{
    public string WorkspaceRoot { get; init; } = Environment.CurrentDirectory;
    public bool EnableL1 { get; init; } = true;
    public bool EnableL2 { get; init; } = true;
    public int MaxMemoryMB { get; init; } = 600;
    public IReadOnlyList<string> FilePatterns { get; init; } = ["*.cs"];
    public IReadOnlyList<string> ExcludePatterns { get; init; } = ["bin/", "obj/", ".git/", ".x/"];
}
