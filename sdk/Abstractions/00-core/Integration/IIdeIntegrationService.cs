namespace JoinCode.Abstractions.Interfaces;

public enum IdeType { VsCode, Cursor, Windsurf, JetBrains }

public sealed record IdeInfo
{
    public required IdeType Type { get; init; }
    public required string Name { get; init; }
    public required bool ExtensionInstalled { get; init; }
    public required bool IsConnected { get; init; }
}

public sealed record IdeDetectionDetail
{
    public required IdeType Type { get; init; }
    public required string Name { get; init; }
    public required bool FoundOnPath { get; init; }
    public string? Path { get; init; }
    public required bool IsRunning { get; init; }
    public required bool ExtensionInstalled { get; init; }
}

public interface IIdeIntegrationService
{
    IReadOnlyList<IdeInfo> DetectInstalledIdes();
    IReadOnlyList<IdeDetectionDetail> DetectInstalledIdesDetailed();
    Task<bool> ConnectAsync(IdeType ideType, CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task<bool> OpenFileAsync(string filePath, int? line = null, CancellationToken ct = default);
    IdeInfo? CurrentConnection { get; }
    string? CurrentFilePath { get; }
}
