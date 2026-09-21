namespace JoinCode.Abstractions.Interfaces;

public enum IdeType { VsCode, Cursor, Windsurf, JetBrains }

public sealed record IdeInfo {
    /// <summary>获取 IDE 类型。</summary>
    public required IdeType Type { get; init; }
    /// <summary>获取 IDE 名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取是否已安装扩展。</summary>
    public required bool ExtensionInstalled { get; init; }
    /// <summary>获取是否已连接。</summary>
    public required bool IsConnected { get; init; }
}

public sealed record IdeDetectionDetail {
    /// <summary>获取 IDE 类型。</summary>
    public required IdeType Type { get; init; }
    /// <summary>获取 IDE 名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取是否在路径中找到。</summary>
    public required bool FoundOnPath { get; init; }
    /// <summary>获取路径。</summary>
    public string? Path { get; init; }
    /// <summary>获取是否正在运行。</summary>
    public required bool IsRunning { get; init; }
    /// <summary>获取是否已安装扩展。</summary>
    public required bool ExtensionInstalled { get; init; }
}

public interface IIdeIntegrationService {
    /// <summary>检测已安装的 IDE 列表。</summary>
    IReadOnlyList<IdeInfo> DetectInstalledIdes();
    /// <summary>检测已安装 IDE 的详细信息列表。</summary>
    IReadOnlyList<IdeDetectionDetail> DetectInstalledIdesDetailed();
    /// <summary>连接到指定类型的 IDE。</summary>
    Task<bool> ConnectAsync(IdeType ideType, CancellationToken ct = default);
    /// <summary>断开 IDE 连接。</summary>
    Task DisconnectAsync(CancellationToken ct = default);
    /// <summary>在 IDE 中打开指定文件。</summary>
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

    /// <summary>获取当前 IDE 连接信息。</summary>
    IdeInfo? CurrentConnection { get; }
    /// <summary>获取当前文件路径。</summary>
    string? CurrentFilePath { get; }
}