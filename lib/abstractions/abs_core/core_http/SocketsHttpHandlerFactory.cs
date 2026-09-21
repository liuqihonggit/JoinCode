namespace JoinCode.Abstractions.Http;

public static class SocketsHttpHandlerFactory {
    /// <summary>创建带 DNS 刷新的 SocketsHttpHandler。</summary>
    public static SocketsHttpHandler CreateWithDnsRefresh(TimeSpan? pooledConnectionLifetime = null) {
        return new SocketsHttpHandler {
            PooledConnectionLifetime = pooledConnectionLifetime ?? TimeSpan.FromMinutes(1),
        };
    }
}