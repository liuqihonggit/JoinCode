namespace IO.ProcessService;

/// <summary>
/// 进程编码统一管理实现 — DI 单例，提供 UTF-8 / 本地编码随时切换
/// <para>
/// 线程安全策略：volatile 字段 + 原子赋值，读取端无锁。
/// 切换编码后，新启动的进程立即生效；已运行的进程不受影响（编码在 ProcessStartInfo 创建时快照）。
/// </para>
/// </summary>
public sealed class ProcessEncodingProvider : IProcessEncodingProvider
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private volatile Encoding _output = Utf8NoBom;
    private volatile Encoding _error = Utf8NoBom;
    private volatile Encoding _input = Utf8NoBom;
    private volatile bool _isUtf8Mode = true;

    /// <inheritdoc />
    public Encoding Output => _output;

    /// <inheritdoc />
    public Encoding Error => _error;

    /// <inheritdoc />
    public Encoding Input => _input;

    /// <inheritdoc />
    public bool IsUtf8Mode => _isUtf8Mode;

    /// <inheritdoc />
    public void UseUtf8()
    {
        _output = Utf8NoBom;
        _error = Utf8NoBom;
        _input = Utf8NoBom;
        _isUtf8Mode = true;
    }

    /// <inheritdoc />
    public void UseLocal()
    {
        var local = Encoding.Default;
        _output = local;
        _error = local;
        _input = local;
        _isUtf8Mode = false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 如果传入带 BOM 的 UTF-8（如 <see cref="System.Text.Encoding.UTF8"/>），
    /// 自动转换为无 BOM 变体，防止管道通信被 BOM 字节破坏。
    /// </remarks>
    public void SetEncoding(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        var safeEncoding = StripBomIfUtf8(encoding);
        _output = safeEncoding;
        _error = safeEncoding;
        _input = safeEncoding;
        _isUtf8Mode = string.Equals(safeEncoding.WebName, "utf-8", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 如果编码是带 BOM 的 UTF-8，返回无 BOM 变体；否则原样返回。
    /// </summary>
    private static Encoding StripBomIfUtf8(Encoding encoding)
    {
        if (encoding.Preamble.Length == 0) return encoding;
        if (!string.Equals(encoding.WebName, "utf-8", StringComparison.OrdinalIgnoreCase)) return encoding;
        return Utf8NoBom;
    }
}
