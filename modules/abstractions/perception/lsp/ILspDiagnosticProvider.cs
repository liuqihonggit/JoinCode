namespace JoinCode.Abstractions.Interfaces.Lsp;

public interface ILspDiagnosticProvider {
    /// <summary>检查待处理的诊断信息。</summary>
    List<LspPendingDiagnostics> CheckPendingDiagnostics();
    /// <summary>清除指定文件的已投递记录。</summary>
    /// <param name="fileUri">文件 URI。</param>
    void ClearDeliveredForFile(string fileUri);
}

public sealed class LspDiagnosticSummary {
    /// <summary>获取文件 URI。</summary>
    public required string Uri { get; init; }
    /// <summary>获取诊断条目列表。</summary>
    public required List<LspDiagnosticEntry> Diagnostics { get; init; } = [];
}

public sealed class LspDiagnosticEntry {
    /// <summary>获取诊断消息。</summary>
    public required string Message { get; init; }
    /// <summary>获取严重级别。</summary>
    public string? Severity { get; init; }
    /// <summary>获取起始行号。</summary>
    public int? StartLine { get; init; }
    /// <summary>获取起始字符位置。</summary>
    public int? StartCharacter { get; init; }
    /// <summary>获取诊断来源。</summary>
    public string? Source { get; init; }
    /// <summary>获取诊断代码。</summary>
    public string? Code { get; init; }
}

/// <summary>
/// 某台 LSP 服务器的待投递诊断 — 服务器名与按文件聚合的诊断摘要。
/// </summary>
/// <param name="ServerName">LSP 服务器名。</param>
/// <param name="Files">按文件聚合的诊断摘要。</param>
public sealed record LspPendingDiagnostics(string ServerName, List<LspDiagnosticSummary> Files);
