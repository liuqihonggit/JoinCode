namespace Abs.Tests.SessionRouterTests;

[Collection(nameof(SessionRouterCollection))]
public sealed class SessionRouterTests
{
    public SessionRouterTests()
    {
        SessionRouter.Clear();
    }

    [Fact]
    public void GetOrCreateScope_相同SessionId_返回同一实例()
    {
        var sessionId = new ObjectId(ObjectType.Session);

        var scope1 = SessionRouter.GetOrCreateScope(sessionId);
        var scope2 = SessionRouter.GetOrCreateScope(sessionId);

        scope1.Should().BeSameAs(scope2);
        SessionRouter.ScopeCount.Should().Be(1);
    }

    [Fact]
    public void GetOrCreateScope_不同SessionId_返回不同实例()
    {
        var sessionId1 = new ObjectId(ObjectType.Session);
        var sessionId2 = new ObjectId(ObjectType.Session);

        var scope1 = SessionRouter.GetOrCreateScope(sessionId1);
        var scope2 = SessionRouter.GetOrCreateScope(sessionId2);

        scope1.Should().NotBeSameAs(scope2);
        SessionRouter.ScopeCount.Should().Be(2);
    }

    [Fact]
    public void Resolve_跨会话跳转_可获取()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        var scope = SessionRouter.GetOrCreateScope(sessionId);
        var goal = new Goal("测试目标");
        scope.Register(goal);

        var resolved = SessionRouter.Resolve<Goal>(sessionId, goal.ObjectId);
        resolved.Should().BeSameAs(goal);

        SessionRouter.Clear();
        goal.Dispose();
    }

    [Fact]
    public void Resolve_跨会话隔离_不可见()
    {
        var sessionIdA = new ObjectId(ObjectType.Session);
        var sessionIdB = new ObjectId(ObjectType.Session);
        var scopeA = SessionRouter.GetOrCreateScope(sessionIdA);
        var scopeB = SessionRouter.GetOrCreateScope(sessionIdB);

        var goalA = new Goal("会话A的目标");
        scopeA.Register(goalA);

        // 会话B 无法通过 goalA 的 ObjectId 获取到它
        SessionRouter.Resolve<Goal>(sessionIdB, goalA.ObjectId).Should().BeNull();

        // 会话A 可以获取
        SessionRouter.Resolve<Goal>(sessionIdA, goalA.ObjectId).Should().BeSameAs(goalA);

        SessionRouter.Clear();
        goalA.Dispose();
    }

    [Fact]
    public void Resolve_会话不存在_返回null()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        var entityId = new ObjectId(ObjectType.Goal);

        SessionRouter.Resolve<Goal>(sessionId, entityId).Should().BeNull();
    }

    [Fact]
    public void RemoveScope_清理其所有Entity()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        var scope = SessionRouter.GetOrCreateScope(sessionId);
        var goal1 = new Goal("目标1");
        var goal2 = new Goal("目标2");
        scope.Register(goal1);
        scope.Register(goal2);

        SessionRouter.RemoveScope(sessionId).Should().BeTrue();
        SessionRouter.ScopeCount.Should().Be(0);
        goal1.LifecycleState.Should().Be(EntityLifecycle.Disposed);
        goal2.LifecycleState.Should().Be(EntityLifecycle.Disposed);
    }

    [Fact]
    public void RemoveScope_不存在_返回False()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        SessionRouter.RemoveScope(sessionId).Should().BeFalse();
    }

    [Fact]
    public void GetAllScopes_遍历所有会话()
    {
        var sessionId1 = new ObjectId(ObjectType.Session);
        var sessionId2 = new ObjectId(ObjectType.Session);
        SessionRouter.GetOrCreateScope(sessionId1);
        SessionRouter.GetOrCreateScope(sessionId2);

        SessionRouter.GetAllScopes().Should().HaveCount(2);
    }

    [Fact]
    public void TryGetScope_存在_返回True()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        SessionRouter.GetOrCreateScope(sessionId);

        SessionRouter.TryGetScope(sessionId, out var scope).Should().BeTrue();
        scope.Should().NotBeNull();
    }

    [Fact]
    public void TryGetScope_不存在_返回False()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        SessionRouter.TryGetScope(sessionId, out var scope).Should().BeFalse();
        scope.Should().BeNull();
    }

    [Fact]
    public void GetOrCreateScope_空SessionId_抛ArgumentException()
    {
        var act = () => SessionRouter.GetOrCreateScope(ObjectId.Empty);
        act.Should().Throw<ArgumentException>();
    }
}
