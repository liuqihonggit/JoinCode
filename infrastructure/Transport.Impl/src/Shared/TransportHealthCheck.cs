namespace JoinCode.Transport;

public interface ITransportHealthCheck
{
    string TransportType { get; }

    Task<TransportHealthResult> CheckAsync(CancellationToken ct = default);
}

public sealed class TransportHealthResult
{
    public required bool IsAvailable { get; init; }
    public required string TransportType { get; init; }
    public string? UnavailableReason { get; init; }
    public TimeSpan CheckDuration { get; init; }
    public TransportUnavailabilityCategory? Category { get; init; }

    public static TransportHealthResult Available(string transportType, TimeSpan duration) => new()
    {
        IsAvailable = true,
        TransportType = transportType,
        CheckDuration = duration,
    };

    public static TransportHealthResult Unavailable(
        string transportType,
        TransportUnavailabilityCategory category,
        string reason,
        TimeSpan duration) => new()
    {
        IsAvailable = false,
        TransportType = transportType,
        Category = category,
        UnavailableReason = reason,
        CheckDuration = duration,
    };
}

public enum TransportUnavailabilityCategory
{
    NetworkUnreachable,
    SandboxBlocked,
    ConfigMissing,
    PortConflict,
    DependencyMissing,
}

public sealed class StdioHealthCheck : ITransportHealthCheck
{
    private readonly string? _command;
    private readonly IFileSystem _fs;

    public string TransportType => "stdio";

    public StdioHealthCheck(string? command, IFileSystem fs)
    {
        _command = command;
        _fs = fs;
    }

    public Task<TransportHealthResult> CheckAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(_command))
        {
            return Task.FromResult(TransportHealthResult.Unavailable(
                TransportType, TransportUnavailabilityCategory.ConfigMissing,
                "No command configured for Stdio transport", sw.Elapsed));
        }

        var commandName = _command!;
        var isPath = commandName.Contains(Path.DirectorySeparatorChar) ||
                     commandName.Contains(Path.AltDirectorySeparatorChar);

        if (isPath && !_fs.FileExists(commandName))
        {
            return Task.FromResult(TransportHealthResult.Unavailable(
                TransportType, TransportUnavailabilityCategory.ConfigMissing,
                $"Command path does not exist: {commandName}", sw.Elapsed));
        }

        return Task.FromResult(TransportHealthResult.Available(TransportType, sw.Elapsed));
    }
}

public sealed class HttpListenerHealthCheck : ITransportHealthCheck
{
    private readonly string _prefix;

    public string TransportType => "http";

    public HttpListenerHealthCheck(string prefix)
    {
        _prefix = prefix;
    }

    public Task<TransportHealthResult> CheckAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var listener = new System.Net.HttpListener();
            listener.Prefixes.Add(_prefix);
            listener.Start();
            listener.Stop();

            return Task.FromResult(TransportHealthResult.Available(TransportType, sw.Elapsed));
        }
        catch (System.Net.HttpListenerException ex)
        {
            var category = IsAccessDeniedError(ex)
                ? TransportUnavailabilityCategory.SandboxBlocked
                : TransportUnavailabilityCategory.PortConflict;

            return Task.FromResult(TransportHealthResult.Unavailable(
                TransportType, category,
                $"HttpListener failed: {ex.Message} (ErrorCode={ex.ErrorCode})",
                sw.Elapsed));
        }
        catch (Exception ex)
        {
            return Task.FromResult(TransportHealthResult.Unavailable(
                TransportType, TransportUnavailabilityCategory.NetworkUnreachable,
                $"Health check failed: {ex.Message}", sw.Elapsed));
        }
    }

    private static bool IsAccessDeniedError(System.Net.HttpListenerException ex) =>
        ex.ErrorCode == 5 ||     // ERROR_ACCESS_DENIED (Windows)
        ex.ErrorCode == 13 ||    // EACCES (Linux)
        ex.ErrorCode == 10013;   // WSAEACCES (Windows socket)
}

public sealed class TcpPortHealthCheck : ITransportHealthCheck
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _transportType;

    public string TransportType => _transportType;

    public TcpPortHealthCheck(string host, int port, string transportType = "tcp")
    {
        _host = host;
        _port = port;
        _transportType = transportType;
    }

    public async Task<TransportHealthResult> CheckAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
            client.Close();
            return TransportHealthResult.Available(TransportType, sw.Elapsed);
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            var category = ex.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse
                ? TransportUnavailabilityCategory.PortConflict
                : TransportUnavailabilityCategory.NetworkUnreachable;

            return TransportHealthResult.Unavailable(
                TransportType, category,
                $"TCP connect failed: {ex.Message} (SocketError={ex.SocketErrorCode})",
                sw.Elapsed);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            return TransportHealthResult.Unavailable(
                TransportType, TransportUnavailabilityCategory.NetworkUnreachable,
                "Health check timed out", sw.Elapsed);
        }
    }
}
