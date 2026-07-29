
namespace JoinCode.Abstractions.Interfaces;

public interface ISshSessionManager : IAsyncDisposable
{
    Task<ISshSession> CreateSessionAsync(
        SshSessionConfig config,
        CancellationToken ct = default);

    ISshSession? GetSession(string sessionId);

    IReadOnlyList<ISshSession> GetActiveSessions();

    Task DestroySessionAsync(
        string sessionId,
        CancellationToken ct = default);

    event EventHandler<SshSessionStateChangedEventArgs>? SessionStateChanged;
}

public interface ISshSession : IAsyncDisposable
{
    string SessionId { get; }

    SshSessionConfig Config { get; }

    SshConnectionState ConnectionState { get; }

    Task ConnectAsync(CancellationToken ct = default);

    Task DisconnectAsync(CancellationToken ct = default);

    Task ReconnectAsync(CancellationToken ct = default);

    Task<bool> KeepAliveAsync(CancellationToken ct = default);

    Task<SshCommandResult> ExecuteCommandAsync(
        string command,
        CancellationToken ct = default);

    Task<ISshForwardedPort> ForwardLocalPortAsync(
        int localPort,
        string remoteHost,
        int remotePort,
        CancellationToken ct = default);

    Task<ISshForwardedPort> ForwardRemotePortAsync(
        int remotePort,
        string localHost,
        int localPort,
        CancellationToken ct = default);

    IReadOnlyList<ISshForwardedPort> GetActiveForwards();

    event EventHandler<SshConnectionStateChangedEventArgs>? ConnectionStateChanged;
}

public interface ISshForwardedPort : IAsyncDisposable
{
    string ForwardId { get; }

    SshForwardType ForwardType { get; }

    string LocalEndpoint { get; }

    string RemoteEndpoint { get; }

    bool IsForwarding { get; }

    Task StartAsync(CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);
}
