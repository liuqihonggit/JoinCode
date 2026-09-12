namespace JoinCode.Abstractions.Http;

public static class SocketsHttpHandlerFactory
{
    public static SocketsHttpHandler CreateWithDnsRefresh(TimeSpan? pooledConnectionLifetime = null)
    {
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = pooledConnectionLifetime ?? TimeSpan.FromMinutes(1),
        };
    }
}
