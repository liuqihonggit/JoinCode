namespace Infra.Tests.EntityTests;

public sealed class ServiceEntityTests
{
    private sealed class TestService : ServiceEntity
    {
        public TestService(string? displayName = null) : base(displayName) { }
    }

    private sealed class TestServiceWithDispose : ServiceEntity
    {
        public bool OnDisposeCalled { get; private set; }

        protected override void OnDispose()
        {
            OnDisposeCalled = true;
        }
    }

    private sealed class TestServiceNoBaseCall : ServiceEntity
    {
        public TestServiceNoBaseCall(int _) { }
    }

    [Fact]
    public void ServiceEntity_ObjectIdType_ShouldBeService()
    {
        using var service = new TestService();
        service.ObjectId.Type.Should().Be(ObjectType.Service);
    }

    [Fact]
    public void ServiceEntity_UniqueId_ShouldStartWithServicePrefix()
    {
        using var service = new TestService();
        service.UniqueId.Should().StartWith("service-");
    }

    [Fact]
    public void ServiceEntity_DisplayName_Custom()
    {
        using var service = new TestService("my-service");
        service.DisplayName.Should().Be("my-service");
    }

    [Fact]
    public void ServiceEntity_Dispose_DefaultOnDisposeDoesNotThrow()
    {
        var service = new TestService();
        var act = () => service.Dispose();
        act.Should().NotThrow();
        service.LifecycleState.Should().Be(EntityLifecycle.Disposed);
    }

    [Fact]
    public void ServiceEntity_Dispose_InvokesOverriddenOnDispose()
    {
        var service = new TestServiceWithDispose();
        service.OnDisposeCalled.Should().BeFalse();
        service.Dispose();
        service.OnDisposeCalled.Should().BeTrue();
    }

    [Fact]
    public void ServiceEntity_NoExplicitBaseCall_StillRegistersObjectId()
    {
        using var service = new TestServiceNoBaseCall(42);
        service.ObjectId.Type.Should().Be(ObjectType.Service);
        service.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ServiceEntity_Created_HasCorrectDefaults()
    {
        using var service = new TestService("test");
        service.LifecycleState.Should().Be(EntityLifecycle.Created);
        service.IsPersisted.Should().BeFalse();
        service.LastActivityAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        service.TraceId.Should().BeNull();
    }
}
