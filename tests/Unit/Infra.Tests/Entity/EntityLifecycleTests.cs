namespace Infra.Tests.EntityTests;

public sealed class EntityLifecycleTests
{
    private sealed class TestEntity : JoinCode.Abstractions.Entity.Entity
    {
        public static readonly ConcurrentDictionary<ObjectId, TestEntity> Registry = new();

        public TestEntity(string? displayName = null)
            : base(ObjectType.Agent, displayName)
        {
            Registry.TryAdd(ObjectId, this);
        }

        protected override void OnDispose()
        {
            Registry.TryRemove(ObjectId, out _);
        }
    }

    [Fact]
    public void Entity_Created_HasCorrectDefaults()
    {
        using var entity = new TestEntity("test");
        entity.LifecycleState.Should().Be(EntityLifecycle.Created);
        entity.IsPersisted.Should().BeFalse();
        entity.LastActivityAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        entity.Id.Should().BeGreaterThan(0);
        entity.UniqueId.Should().StartWith("agent-");
        entity.DisplayName.Should().Be("test");
    }

    [Fact]
    public void Entity_MarkPersisted_TransitionsToPersisted()
    {
        using var entity = new TestEntity();
        entity.LifecycleState = EntityLifecycle.Completed;
        entity.MarkPersisted();
        entity.IsPersisted.Should().BeTrue();
        entity.LifecycleState.Should().Be(EntityLifecycle.Persisted);
    }

    [Fact]
    public void Entity_CanReclaim_DefaultRequiresPersistedAndCompleted()
    {
        using var entity = new TestEntity();
        entity.CanReclaim().Should().BeFalse();

        entity.CompletedAt = DateTime.UtcNow;
        entity.CanReclaim().Should().BeFalse();

        entity.LifecycleState = EntityLifecycle.Completed;
        entity.MarkPersisted();
        entity.CanReclaim().Should().BeTrue();
    }

    [Fact]
    public async Task Entity_Touch_RefreshesLastActivityAt()
    {
        using var entity = new TestEntity();
        var before = entity.LastActivityAt;
        await Task.Delay(10);
        entity.Touch();
        entity.LastActivityAt.Should().BeAfter(before);
    }

    [Fact]
    public void Entity_IsTimedOut_WhenExceeded()
    {
        using var entity = new TestEntity();
        entity.IsTimedOut.Should().BeFalse();

        var timedOutEntity = new TestEntity { TimeoutAt = DateTime.UtcNow.AddSeconds(-1) };
        timedOutEntity.IsTimedOut.Should().BeTrue();
    }

    [Fact]
    public void Entity_Dispose_SetsLifecycleToDisposed()
    {
        var entity = new TestEntity();
        entity.LifecycleState.Should().Be(EntityLifecycle.Created);
        entity.Dispose();
        entity.LifecycleState.Should().Be(EntityLifecycle.Disposed);
    }
}
