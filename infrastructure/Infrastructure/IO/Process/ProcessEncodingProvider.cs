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
    private volatile Encoding _output = Encoding.UTF8;
    private volatile Encoding _error = Encoding.UTF8;
    private volatile Encoding _input = Encoding.UTF8;
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
        _output = Encoding.UTF8;
        _error = Encoding.UTF8;
        _input = Encoding.UTF8;
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
    public void SetEncoding(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        _output = encoding;
        _error = encoding;
        _input = encoding;
        _isUtf8Mode = string.Equals(encoding.WebName, "utf-8", StringComparison.OrdinalIgnoreCase);
    }
}
