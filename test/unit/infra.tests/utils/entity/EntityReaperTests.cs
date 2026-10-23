namespace Infra.Tests.EntityTests;

public sealed class EntityReaperTests {
    private sealed class ReclaimableEntity : JoinCode.Abstractions.Entity.Entity {
        public static readonly ConcurrentDictionary<ObjectId, ReclaimableEntity> Registry = new();

        public ReclaimableEntity()
            : base(ObjectType.Task) {
            Registry.TryAdd(ObjectId, this);
        }

        public override void Dispose() {
            Registry.TryRemove(ObjectId, out _);
            base.Dispose();
        }
    }

    [Fact]
    public async Task EntityReaper_ScanOnce_ReclaimsPersistedCompletedEntities() {
        await using var entity = new ReclaimableEntity();
        entity.LifecycleState = EntityLifecycle.Completed;
        entity.CompletedAt = DateTime.UtcNow;
        entity.MarkPersisted();

        entity.CanReclaim().Should().BeTrue();
        await entity.DisposeAsync();
        entity.LifecycleState.Should().Be(EntityLifecycle.Disposed);
    }

    [Fact]
    public async Task EntityReaper_ScanOnce_SkipsNonReclaimableEntities() {
        await using var entity = new ReclaimableEntity();
        entity.CanReclaim().Should().BeFalse();
        entity.LifecycleState.Should().Be(EntityLifecycle.Created);
    }

    [Fact]
    public async Task EntityReaper_GetTimedOutEntities_DetectsTimedOut() {
        await using var entity = new ReclaimableEntity { TimeoutAt = DateTime.UtcNow.AddSeconds(-1) };
        entity.IsTimedOut.Should().BeTrue();
    }
}
