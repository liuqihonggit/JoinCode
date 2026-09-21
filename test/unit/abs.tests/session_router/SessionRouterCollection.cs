namespace Abs.Tests.SessionRouterTests;

[CollectionDefinition(nameof(SessionRouterCollection))]
public sealed class SessionRouterCollection : ICollectionFixture<SessionRouterCollectionFixture>;

public sealed class SessionRouterCollectionFixture : IDisposable {
    public SessionRouterCollectionFixture() {
        _ = SessionRouter.ClearAsync();
    }

    public async Task Dispose() {
        await SessionRouter.ClearAsync();
    }
}