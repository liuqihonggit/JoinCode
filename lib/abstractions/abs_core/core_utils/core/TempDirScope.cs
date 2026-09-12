namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 临时目录作用域 — 创建时建立临时目录，DisposeAsync 时递归删除。
/// <para>
/// 用法：<c>await using var tmp = TempDirScope.Create(fs, "csharp_sandbox_");</c>
/// 消除手写 <c>var dir = ...; CreateDirectory(dir); try { } finally { DeleteDirectory(dir, true) }</c> 样板。
/// 详见 ADR-0093、AGENTS.md「代码风格规范」。
/// </para>
/// </summary>
public sealed class TempDirScope : IAsyncDisposable
{
    /// <summary>临时目录路径。</summary>
    public string Path { get; }

    private readonly IFileSystem _fs;
    private bool _disposed;

    private TempDirScope(string path, IFileSystem fs)
    {
        Path = path;
        _fs = fs;
    }

    /// <summary>
    /// 创建临时目录作用域。目录名 = 系统临时目录 + prefix + GUID。
    /// </summary>
    /// <param name="fs">文件系统抽象（生产 PhysicalFileSystem / 测试 InMemoryFileSystem）。</param>
    /// <param name="prefix">目录名前缀，默认 "jcctmp_"。</param>
    public static TempDirScope Create(IFileSystem fs, string? prefix = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var prefixStr = prefix ?? "jcctmp_";
        var path = fs.CombinePath(System.IO.Path.GetTempPath(), prefixStr + Guid.NewGuid().ToString("N"));
        fs.CreateDirectory(path);
        return new TempDirScope(path, fs);
    }

    /// <summary>
    /// 递归删除临时目录。幂等，清理失败不抛（best effort，记 Debug 日志）。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        try { if (_fs.DirectoryExists(Path)) _fs.DeleteDirectory(Path, recursive: true); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[TempDirScope] 清理临时目录失败: {Path} - {ex.Message}"); }
        return ValueTask.CompletedTask;
    }
}
