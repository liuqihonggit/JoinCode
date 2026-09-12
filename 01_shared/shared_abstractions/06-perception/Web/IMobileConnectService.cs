namespace JoinCode.Abstractions.Interfaces;

public interface IMobileConnectService
{
    string GenerateConnectUrl(int port);
    Task<int> StartConnectServerAsync(CancellationToken ct = default);
    void StopConnectServer();
    bool IsServerRunning { get; }
}
