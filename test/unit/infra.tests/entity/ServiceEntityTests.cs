namespace Infra.Tests.EntityTests;

public sealed class ServiceEntityTests {
    private sealed class TestService : ServiceEntity {
        public TestService(string? displayName = null) : base(displayName) { }
    }

    private sealed class TestServiceWithDispose : ServiceEntity {
        public bool OnDisposeCalled { get; private set; }

        public override void Dispose() {
            OnDisposeCalled = true;
            base.Dispose();
        }
    }

    private sealed class TestServiceNoBaseCall : ServiceEntity {
        public TestServiceNoBaseCall(int _) { }
    }

    [Fact]
    public async Task ServiceEntity_ObjectIdType_ShouldBeService() {
        await using var service = new TestService();
        service.ObjectId.Type.Should().Be(ObjectType.Service);
    }

    [Fact]
    public async Task ServiceEntity_UniqueId_ShouldStartWithServicePrefix() {
        await using var service = new TestService();
        service.UniqueId.Should().StartWith("service-");
    }

    [Fact]
    public async Task ServiceEntity_DisplayName_Custom() {
        await using var service = new TestService("my-service");
        service.DisplayName.Should().Be("my-service");
    }

    [Fact]
    public void ServiceEntity_Dispose_DefaultOnDisposeDoesNotThrow() {
        var service = new TestService();
        var act = () => service.Dispose();
        act.Should().NotThrow();
        service.LifecycleState.Should().Be(EntityLifecycle.Disposed);
    }

    [Fact]
    public async Task ServiceEntity_Dispose_InvokesOverriddenOnDispose() {
        await using var service = new TestServiceWithDispose();
        service.OnDisposeCalled.Should().BeFalse();
        await service.DisposeAsync();
        service.OnDisposeCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ServiceEntity_NoExplicitBaseCall_StillRegistersObjectId() {
        await using var service = new TestServiceNoBaseCall(42);
        service.ObjectId.Type.Should().Be(ObjectType.Service);
        service.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ServiceEntity_Created_HasCorrectDefaults() {
        await using var service = new TestService("test");
        service.LifecycleState.Should().Be(EntityLifecycle.Created);
        service.IsPersisted.Should().BeFalse();
        service.LastActivityAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        service.TraceId.Should().BeNull();
    }
}