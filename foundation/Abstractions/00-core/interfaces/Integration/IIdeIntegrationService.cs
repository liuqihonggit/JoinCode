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

    /// <summary>
    /// 在 IDE 中设置选区 — 对齐 TS bridgeMessaging.ts setSelection
    /// 通过 IDE CLI --goto 参数定位光标到起始位置（endLine/endCol 当前未使用，保留接口扩展位）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="startLine">起始行（1-based）</param>
    /// <param name="startCol">起始列（1-based，部分 IDE 不支持）</param>
    /// <param name="endLine">结束行（当前忽略）</param>
    /// <param name="endCol">结束列（当前忽略）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>true=成功定位光标；false=未连接 IDE 或调用失败</returns>
    Task<bool> SetSelectionAsync(string filePath, int startLine, int startCol, int endLine, int endCol, CancellationToken ct = default);

    IdeInfo? CurrentConnection { get; }
    string? CurrentFilePath { get; }
}
