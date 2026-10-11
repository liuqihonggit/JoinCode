namespace JoinCode.Abstractions.Interfaces;

public interface IMobileConnectService {
    /// <summary>生成移动端连接 URL。</summary>
    string GenerateConnectUrl(int port);
    /// <summary>异步启动连接服务器。</summary>
    Task<int> StartConnectServerAsync(CancellationToken ct = default);
    /// <summary>停止连接服务器。</summary>
    void StopConnectServer();
    /// <summary>获取服务器是否运行中。</summary>
    bool IsServerRunning { get; }
}