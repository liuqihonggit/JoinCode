namespace Abs.Tests.SessionRouterTests;

[Collection(nameof(SessionRouterCollection))]
public sealed class SessionScopeTests
{
    public SessionScopeTests()
    {
        SessionRouter.Clear();
    }
    [Fact]
    public void Register_ThenResolve_可获取()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        var scope = SessionRouter.GetOrCreateScope(sessionId);
        var goal = new Goal("测试目标");

        scope.Register(goal);
        scope.Count.Should().Be(1);
        scope.Resolve<Goal>(goal.ObjectId).Should().BeSameAs(goal);

        SessionRouter.Clear();
        goal.Dispose();
    }

    [Fact]
    public void Unregister_移除后不可获取()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        var scope = SessionRouter.GetOrCreateScope(sessionId);
        var goal = new Goal("测试目标");

        scope.Register(goal);
        scope.Unregister(goal.ObjectId).Should().BeTrue();
        scope.Count.Should().Be(0);
        scope.Resolve<Goal>(goal.ObjectId).Should().BeNull();

        SessionRouter.Clear();
        goal.Dispose();
    }

    [Fact]
    public void Resolve_类型不匹配_返回null()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        var scope = SessionRouter.GetOrCreateScope(sessionId);
        var goal = new Goal("测试目标");

        scope.Register(goal);
        scope.Resolve<Session>(goal.ObjectId).Should().BeNull();

        SessionRouter.Clear();
        goal.Dispose();
    }

    [Fact]
    public void GetAll_ByObjectType_按类型分桶()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        var scope = SessionRouter.GetOrCreateScope(sessionId);
        var goal1 = new Goal("目标1");
        var goal2 = new Goal("目标2");
        var session = new Session();

        scope.Register(goal1);
        scope.Register(goal2);
        scope.Register(session);

        scope.GetAll(ObjectType.Goal).Should().HaveCount(2);
        scope.GetAll(ObjectType.Session).Should().HaveCount(1);
        scope.GetAll(ObjectType.Agent).Should().BeEmpty();

        SessionRouter.Clear();
        goal1.Dispose();
        goal2.Dispose();
        session.Dispose();
    }

    [Fact]
    public void GetAll_Generic_按CLR类型过滤()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        var scope = SessionRouter.GetOrCreateScope(sessionId);
        var goal1 = new Goal("目标1");
        var goal2 = new Goal("目标2");

        scope.Register(goal1);
        scope.Register(goal2);

        var goals = scope.GetAll<Goal>();
        goals.Should().HaveCount(2);
        goals.Should().Contain(goal1);
        goals.Should().Contain(goal2);

        SessionRouter.Clear();
        goal1.Dispose();
        goal2.Dispose();
    }

    [Fact]
    public void Contains_判断是否存在()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        var scope = SessionRouter.GetOrCreateScope(sessionId);
        var goal = new Goal("测试目标");

        scope.Register(goal);
        scope.Contains(goal.ObjectId).Should().BeTrue();
        scope.Unregister(goal.ObjectId);
        scope.Contains(goal.ObjectId).Should().BeFalse();

        SessionRouter.Clear();
        goal.Dispose();
    }

    [Fact]
    public void Dispose_清理所有Entity()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        var scope = SessionRouter.GetOrCreateScope(sessionId);
        var goal1 = new Goal("目标1");
        var goal2 = new Goal("目标2");

        scope.Register(goal1);
        scope.Register(goal2);
        scope.Count.Should().Be(2);

        scope.Dispose();

        scope.IsDisposed.Should().BeTrue();
        scope.Count.Should().Be(0);
        goal1.LifecycleState.Should().Be(EntityLifecycle.Disposed);
        goal2.LifecycleState.Should().Be(EntityLifecycle.Disposed);

        SessionRouter.Clear();
    }

    [Fact]
    public void Register_已释放_抛ObjectDisposedException()
    {
        var sessionId = new ObjectId(ObjectType.Session);
        var scope = SessionRouter.GetOrCreateScope(sessionId);
        scope.Dispose();

        var act = () => scope.Register(new Goal("测试"));
        act.Should().Throw<ObjectDisposedException>();

        SessionRouter.Clear();
    }

    [Fact]
    public void GetOrCreateScope_空SessionId_抛ArgumentException()
    {
        var act = () => SessionRouter.GetOrCreateScope(ObjectId.Empty);
        act.Should().Throw<ArgumentException>();

        SessionRouter.Clear();
    }
}
