
namespace JoinCode.Abstractions.Interfaces;

public interface ISshSessionManager : IAsyncDisposable {
    /// <summary>创建新的 SSH 会话。</summary>
    Task<ISshSession> CreateSessionAsync(
        SshSessionConfig config,
        CancellationToken ct = default);

    /// <summary>根据会话标识获取会话。</summary>
    ISshSession? GetSession(string sessionId);

    /// <summary>获取所有活动会话。</summary>
    IEnumerable<ISshSession> GetActiveSessions();

    /// <summary>销毁指定会话。</summary>
    Task DestroySessionAsync(
        string sessionId,
        CancellationToken ct = default);

    event EventHandler<SshSessionStateChangedEventArgs>? SessionStateChanged;
}

public interface ISshSession : IAsyncDisposable {
    /// <summary>获取会话标识。</summary>
    string SessionId { get; }

    /// <summary>获取会话配置。</summary>
    SshSessionConfig Config { get; }

    /// <summary>获取连接状态。</summary>
    SshConnectionState ConnectionState { get; }

    /// <summary>建立连接。</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>断开连接。</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>重新连接。</summary>
    Task ReconnectAsync(CancellationToken ct = default);

    /// <summary>发送保活心跳。</summary>
    Task<bool> KeepAliveAsync(CancellationToken ct = default);

    /// <summary>执行远程命令。</summary>
    Task<SshCommandResult> ExecuteCommandAsync(
        string command,
        CancellationToken ct = default);

    /// <summary>转发本地端口到远程主机。</summary>
    Task<ISshForwardedPort> ForwardLocalPortAsync(
        int localPort,
        string remoteHost,
        int remotePort,
        CancellationToken ct = default);

    /// <summary>转发远程端口到本地主机。</summary>
    Task<ISshForwardedPort> ForwardRemotePortAsync(
        int remotePort,
        string localHost,
        int localPort,
        CancellationToken ct = default);

    /// <summary>获取所有活动端口转发。</summary>
    IEnumerable<ISshForwardedPort> GetActiveForwards();

    event EventHandler<SshConnectionStateChangedEventArgs>? ConnectionStateChanged;
}

public interface ISshForwardedPort : IAsyncDisposable {
    /// <summary>获取转发标识。</summary>
    string ForwardId { get; }

    /// <summary>获取转发类型。</summary>
    SshForwardType ForwardType { get; }

    /// <summary>获取本地端点。</summary>
    string LocalEndpoint { get; }

    /// <summary>获取远程端点。</summary>
    string RemoteEndpoint { get; }

    /// <summary>获取是否正在转发。</summary>
    bool IsForwarding { get; }

    /// <summary>启动端口转发。</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>停止端口转发。</summary>
    Task StopAsync(CancellationToken ct = default);
}