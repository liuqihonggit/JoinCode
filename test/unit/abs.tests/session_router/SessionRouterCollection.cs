namespace Abs.Tests.SessionRouterTests;

[CollectionDefinition(nameof(SessionRouterCollection))]
public sealed class SessionRouterCollection : ICollectionFixture<SessionRouterCollectionFixture>;

public sealed class SessionRouterCollectionFixture : IAsyncDisposable {
    public SessionRouterCollectionFixture() {
        _ = SessionRouter.ClearAsync();
    }

    public async ValueTask DisposeAsync() {
        await SessionRouter.ClearAsync();
    }
}