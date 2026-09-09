namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 工作目录切换作用域 — Enter 时切换到新目录，Dispose 时恢复原目录。
/// <para>
/// 用法：<c>using var cwd = CwdScope.Enter(fs, newPath);</c>
/// 消除手写 <c>var prev = GetCurrentDirectory(); SetCurrentDirectory(new); try { } finally { SetCurrentDirectory(prev) }</c> 样板。
/// 详见 ADR-0093、AGENTS.md「代码风格规范」。
/// </para>
/// </summary>
public sealed class CwdScope : IDisposable
{
    private readonly IFileSystem _fs;
    private readonly string _original;
    private bool _disposed;

    private CwdScope(IFileSystem fs, string original)
    {
        _fs = fs;
        _original = original;
    }

    /// <summary>
    /// 切换到新工作目录，返回作用域（Dispose 时恢复原目录）。
    /// </summary>
    /// <param name="fs">文件系统抽象。</param>
    /// <param name="newCwd">新工作目录路径。</param>
    public static CwdScope Enter(IFileSystem fs, string newCwd)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var original = fs.GetCurrentDirectory();
        fs.SetCurrentDirectory(newCwd);
        return new CwdScope(fs, original);
    }

    /// <summary>
    /// 恢复到原工作目录。幂等，恢复失败不抛（best effort，记 Debug 日志）。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _fs.SetCurrentDirectory(_original); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CwdScope] 恢复工作目录失败: {_original} - {ex.Message}"); }
    }
}
