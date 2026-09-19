namespace JoinCode.Transport;

/// <summary>
/// 传输健康检查接口 — 检测传输是否可用
/// </summary>
public interface ITransportHealthCheck {
    /// <summary>传输类型名称</summary>
    string TransportType { get; }

    /// <summary>
    /// 执行健康检查
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>健康检查结果</returns>
    Task<TransportHealthResult> CheckAsync(CancellationToken ct = default);
}

/// <summary>
/// 传输健康检查结果
/// </summary>
public sealed class TransportHealthResult {
    /// <summary>是否可用</summary>
    public required bool IsAvailable { get; init; }
    /// <summary>传输类型名称</summary>
    public required string TransportType { get; init; }
    /// <summary>不可用原因描述（可用时为 null）</summary>
    public string? UnavailableReason { get; init; }
    /// <summary>检查耗时</summary>
    public TimeSpan CheckDuration { get; init; }
    /// <summary>不可用分类（可用时为 null）</summary>
    public TransportUnavailabilityCategory? Category { get; init; }

    /// <summary>
    /// 创建可用结果
    /// </summary>
    /// <param name="transportType">传输类型名称</param>
    /// <param name="duration">检查耗时</param>
    /// <returns>表示可用的健康检查结果</returns>
    public static TransportHealthResult Available(string transportType, TimeSpan duration) => new() {
        IsAvailable = true,
        TransportType = transportType,
        CheckDuration = duration,
    };

    /// <summary>
    /// 创建不可用结果
    /// </summary>
    /// <param name="transportType">传输类型名称</param>
    /// <param name="category">不可用分类</param>
    /// <param name="reason">不可用原因描述</param>
    /// <param name="duration">检查耗时</param>
    /// <returns>表示不可用的健康检查结果</returns>
    public static TransportHealthResult Unavailable(
        string transportType,
        TransportUnavailabilityCategory category,
        string reason,
        TimeSpan duration) => new() {
            IsAvailable = false,
            TransportType = transportType,
            Category = category,
            UnavailableReason = reason,
            CheckDuration = duration,
        };
}

/// <summary>
/// 传输不可用分类 — 描述传输不可用的具体原因类别
/// </summary>
public enum TransportUnavailabilityCategory {
    /// <summary>网络不可达</summary>
    [EnumValue("networkUnreachable")]
    NetworkUnreachable,
    /// <summary>沙箱拦截</summary>
    [EnumValue("sandboxBlocked")]
    SandboxBlocked,
    /// <summary>配置缺失</summary>
    [EnumValue("configMissing")]
    ConfigMissing,
    /// <summary>端口冲突</summary>
    [EnumValue("portConflict")]
    PortConflict,
    /// <summary>依赖缺失</summary>
    [EnumValue("dependencyMissing")]
    DependencyMissing,
}

/// <summary>
/// Stdio 传输健康检查 — 检测命令是否配置且可执行
/// </summary>
public sealed class StdioHealthCheck : ITransportHealthCheck {
    private readonly string? _command;
    private readonly IFileSystem _fs;

    /// <inheritdoc/>
    public string TransportType => "stdio";

    /// <summary>
    /// 构造 Stdio 健康检查器
    /// </summary>
    /// <param name="command">可执行命令路径或名称，null 表示未配置</param>
    /// <param name="fs">文件系统抽象</param>
    public StdioHealthCheck(string? command, IFileSystem fs) {
        _command = command;
        _fs = fs;
    }

    /// <inheritdoc/>
    public Task<TransportHealthResult> CheckAsync(CancellationToken ct = default) {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(_command)) {
            return Task.FromResult(TransportHealthResult.Unavailable(
                TransportType, TransportUnavailabilityCategory.ConfigMissing,
                "No command configured for Stdio transport", sw.Elapsed));
        }

        var commandName = _command!;
        var isPath = commandName.Contains(Path.DirectorySeparatorChar) ||
                     commandName.Contains(Path.AltDirectorySeparatorChar);

        if (isPath && !_fs.FileExists(commandName)) {
            return Task.FromResult(TransportHealthResult.Unavailable(
                TransportType, TransportUnavailabilityCategory.ConfigMissing,
                $"Command path does not exist: {commandName}", sw.Elapsed));
        }

        return Task.FromResult(TransportHealthResult.Available(TransportType, sw.Elapsed));
    }
}

/// <summary>
/// HTTP 监听器健康检查 — 通过 TCP 连接检测端口是否可达
/// </summary>
public sealed class HttpListenerHealthCheck : ITransportHealthCheck {
    private readonly string _prefix;
    private readonly string _host;
    private readonly int _port;

    /// <inheritdoc/>
    public string TransportType => "http";

    /// <summary>
    /// 构造 HTTP 监听器健康检查器
    /// </summary>
    /// <param name="prefix">HTTP 监听前缀 URL</param>
    public HttpListenerHealthCheck(string prefix) {
        _prefix = prefix;
        try {
            var uri = new Uri(prefix);
            _host = uri.Host;
            _port = uri.Port > 0 ? uri.Port : 80;
        } catch (UriFormatException) {
            _host = "localhost";
            _port = 0;
        }
    }

    /// <summary>
    /// 通过 TCP 连接检测端口是否可达 — 端口被占用说明服务正在运行（可用），而非不可用
    /// 旧逻辑（HttpListener.Start）在端口被占用时抛异常导致误判为 Unavailable，已修复
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>健康检查结果</returns>
    public async Task<TransportHealthResult> CheckAsync(CancellationToken ct = default) {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
            client.Close();
            return TransportHealthResult.Available(TransportType, sw.Elapsed);
        } catch (System.Net.Sockets.SocketException ex) {
            return TransportHealthResult.Unavailable(
                TransportType, TransportUnavailabilityCategory.NetworkUnreachable,
                $"TCP connect to {_host}:{_port} failed: {ex.Message} (SocketError={ex.SocketErrorCode})",
                sw.Elapsed);
        } catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException) {
            return TransportHealthResult.Unavailable(
                TransportType, TransportUnavailabilityCategory.NetworkUnreachable,
                $"Health check timed out for {_host}:{_port}", sw.Elapsed);
        }
    }
}

/// <summary>
/// TCP 端口健康检查 — 通过 TCP 连接检测指定主机端口是否可达
/// </summary>
public sealed class TcpPortHealthCheck : ITransportHealthCheck {
    private readonly string _host;
    private readonly int _port;
    private readonly string _transportType;

    /// <inheritdoc/>
    public string TransportType => _transportType;

    /// <summary>
    /// 构造 TCP 端口健康检查器
    /// </summary>
    /// <param name="host">目标主机</param>
    /// <param name="port">目标端口</param>
    /// <param name="transportType">传输类型名称，默认 "tcp"</param>
    public TcpPortHealthCheck(string host, int port, string transportType = "tcp") {
        _host = host;
        _port = port;
        _transportType = transportType;
    }

    /// <inheritdoc/>
    public async Task<TransportHealthResult> CheckAsync(CancellationToken ct = default) {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
            client.Close();
            return TransportHealthResult.Available(TransportType, sw.Elapsed);
        } catch (System.Net.Sockets.SocketException ex) {
            var category = ex.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse
                ? TransportUnavailabilityCategory.PortConflict
                : TransportUnavailabilityCategory.NetworkUnreachable;

            return TransportHealthResult.Unavailable(
                TransportType, category,
                $"TCP connect failed: {ex.Message} (SocketError={ex.SocketErrorCode})",
                sw.Elapsed);
        } catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException) {
            return TransportHealthResult.Unavailable(
                TransportType, TransportUnavailabilityCategory.NetworkUnreachable,
                "Health check timed out", sw.Elapsed);
        }
    }
}