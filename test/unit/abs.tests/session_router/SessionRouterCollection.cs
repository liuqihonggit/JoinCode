namespace Abs.Tests.SessionRouterTests;

[CollectionDefinition(nameof(SessionRouterCollection))]
public sealed class SessionRouterCollection : ICollectionFixture<SessionRouterCollectionFixture>;

public sealed class SessionRouterCollectionFixture : IDisposable
{
    public SessionRouterCollectionFixture()
    {
        SessionRouter.Clear();
    }

    public void Dispose()
    {
        SessionRouter.Clear();
    }
}
