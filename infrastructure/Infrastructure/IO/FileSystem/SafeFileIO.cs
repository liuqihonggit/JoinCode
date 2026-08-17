namespace IO.FileSystem;

/// <summary>
/// 文件读写辅助类 — 所有方法用 FileShare.ReadWrite，避免跨进程/同进程读-写冲突
/// 适用于无法注入 IFileSystem 的静态上下文（如 Program.Main、诊断日志、崩溃处理）
/// </summary>
public static class SafeFileIO
{
    /// <summary>读取全部文本 — FileShare.ReadWrite 允许并发写入者</summary>
    public static string ReadAllText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>写入全部文本 — FileShare.ReadWrite 允许并发读取者</summary>
    public static void WriteAllText(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
        writer.Flush();
    }

    /// <summary>异步写入全部文本 — FileShare.ReadWrite 允许并发读取者</summary>
    public static async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>追加文本 — FileShare.ReadWrite 允许并发读取/写入者</summary>
    public static void AppendAllText(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
        writer.Flush();
    }

    /// <summary>异步追加文本 — FileShare.ReadWrite 允许并发读取/写入者</summary>
    public static async Task AppendAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
