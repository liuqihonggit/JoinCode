namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 临时文件作用域 — 预留临时文件路径，Dispose 时删除文件（若存在）。
/// <para>
/// 用法：<c>using var tmp = TempFileScope.Create(fs, "jcc_repl_", ".cs"); var path = tmp.Path;</c>
/// 消除手写 <c>var file = ...; try { } finally { if (FileExists) DeleteFile }</c> 样板。
/// 注意：本类只管理路径 + 删除，不创建文件（由调用方写入）。
/// 详见 ADR-0093、AGENTS.md「代码风格规范」。
/// </para>
/// </summary>
public sealed class TempFileScope : IDisposable
{
    /// <summary>临时文件路径。</summary>
    public string Path { get; }

    private readonly IFileSystem _fs;
    private bool _disposed;

    private TempFileScope(string path, IFileSystem fs)
    {
        Path = path;
        _fs = fs;
    }

    /// <summary>
    /// 预留临时文件路径（不创建文件）。路径 = 系统临时目录 + prefix + GUID + extension。
    /// </summary>
    /// <param name="fs">文件系统抽象。</param>
    /// <param name="prefix">文件名前缀，默认 "jcctmp_"。</param>
    /// <param name="extension">文件扩展名（含点），默认 ".tmp"。</param>
    public static TempFileScope Create(IFileSystem fs, string? prefix = null, string? extension = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var prefixStr = prefix ?? "jcctmp_";
        var ext = extension ?? ".tmp";
        var path = fs.CombinePath(System.IO.Path.GetTempPath(), prefixStr + Guid.NewGuid().ToString("N") + ext);
        return new TempFileScope(path, fs);
    }

    /// <summary>
    /// 删除临时文件（若存在）。幂等，删除失败不抛（best effort，记 Debug 日志）。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_fs.FileExists(Path)) _fs.DeleteFile(Path); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TempFileScope] 清理临时文件失败: {Path} - {ex.Message}"); }
    }
}
