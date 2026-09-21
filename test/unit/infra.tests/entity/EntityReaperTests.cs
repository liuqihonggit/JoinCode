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
        ObjectIdManager.Clear();
        var clock = JoinCode.Abstractions.Clock.SystemClockService.Instance;
        var reaper = new Infrastructure.EntityReaper.EntityReaper(clock, new EntityReaperConfig { EnableAutoReclaim = true, EnableLeakDetection = false });

        using var entity = new ReclaimableEntity();
        entity.LifecycleState = EntityLifecycle.Completed;
        entity.CompletedAt = DateTime.UtcNow;
        entity.MarkPersisted();

        var count = await reaper.ScanOnce().ConfigureAwait(false);
        count.Should().Be(1);
        entity.LifecycleState.Should().Be(EntityLifecycle.Disposed);
    }

    [Fact]
    public async Task EntityReaper_ScanOnce_SkipsNonReclaimableEntities() {
        ObjectIdManager.Clear();
        var clock = JoinCode.Abstractions.Clock.SystemClockService.Instance;
        var reaper = new Infrastructure.EntityReaper.EntityReaper(clock, new EntityReaperConfig { EnableAutoReclaim = true, EnableLeakDetection = false });

        using var entity = new ReclaimableEntity();
        var count = await reaper.ScanOnce().ConfigureAwait(false);
        count.Should().Be(0);
        entity.LifecycleState.Should().Be(EntityLifecycle.Created);
    }

    [Fact]
    public void EntityReaper_GetTimedOutEntities_DetectsTimedOut() {
        ObjectIdManager.Clear();
        var clock = JoinCode.Abstractions.Clock.SystemClockService.Instance;
        var reaper = new Infrastructure.EntityReaper.EntityReaper(clock, new EntityReaperConfig { EnableLeakDetection = false });

        var entity = new ReclaimableEntity { TimeoutAt = DateTime.UtcNow.AddSeconds(-1) };
        var timedOut = reaper.GetTimedOutEntities();
        timedOut.Should().ContainSingle(e => e.ObjectId == entity.ObjectId);
    }
}