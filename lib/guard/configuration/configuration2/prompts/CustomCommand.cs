namespace Core.Configuration;

public sealed record CustomCommand
{
    public required string Name { get; init; }
    public required string Content { get; init; }
    public string Description { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public bool DisableModelInvocation { get; init; }
    public string? Namespace { get; init; }

    public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}:{Name}";

    public string ApplyArguments(string arguments)
    {
        return Content.Replace("$ARGUMENTS", arguments, StringComparison.Ordinal);
    }
}
