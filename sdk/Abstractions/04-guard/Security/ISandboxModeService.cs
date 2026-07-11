namespace JoinCode.Abstractions.Security;

public interface ISandboxModeService : IDisposable
{
    Task<SecuritySandboxInfo> EnterSandboxAsync(SandboxOptions options, CancellationToken ct = default);
    Task ExitSandboxAsync(CancellationToken ct = default);
    bool IsInSandbox { get; }
    SecuritySandboxInfo? CurrentSandbox { get; }
    string ResolvePath(string path);
}

public sealed partial class SandboxOptions
{
    public required SandboxType Type { get; init; }
    public string? SandboxRoot { get; init; }
    public bool RestrictNetwork { get; init; } = true;
    public bool RestrictFileSystem { get; init; } = true;
    public List<string>? AllowedPaths { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<SandboxType>))]
public enum SandboxType { [EnumValue("none")] None, [EnumValue("process")] Process, [EnumValue("docker")] Docker, [EnumValue("bubblewrap")] Bubblewrap }

public sealed partial class SecuritySandboxInfo
{
    public required SandboxType Type { get; init; }
    public required string RootPath { get; init; }
    public required DateTime EnteredAt { get; init; }
    public required bool IsRestricted { get; init; }
}
