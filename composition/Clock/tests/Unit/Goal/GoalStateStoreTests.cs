
namespace Core.Goal.Tests;

using Testing.Common.Services;

public sealed class GoalStateStoreTests
{
    private static GoalStateStore CreateStore(InMemoryFileSystem? fs = null, string? baseDir = null)
    {
        fs ??= new InMemoryFileSystem();
        baseDir ??= "/test-goal-state";
        return new GoalStateStore(fs, baseDir);
    }

    [Fact]
    public async Task SaveLoad_Should_RoundTrip()
    {
        var store = CreateStore();
        var state = new GoalState
        {
            GoalId = "g1",
            Objective = "test objective",
            Status = GoalStatus.Pursuing,
            TokensUsed = 100,
            TurnsCompleted = 5,
        };

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync("g1");

        Assert.NotNull(loaded);
        Assert.Equal("g1", loaded!.GoalId);
        Assert.Equal("test objective", loaded.Objective);
        Assert.Equal(GoalStatus.Pursuing, loaded.Status);
        Assert.Equal(100, loaded.TokensUsed);
        Assert.Equal(5, loaded.TurnsCompleted);
    }

    [Fact]
    public async Task Load_NonExistent_Should_ReturnNull()
    {
        var store = CreateStore();
        var loaded = await store.LoadAsync("non-existent");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task Delete_Should_RemoveState()
    {
        var store = CreateStore();
        var state = new GoalState { GoalId = "g2", Objective = "to delete", Status = GoalStatus.Pursuing };

        await store.SaveAsync(state);
        Assert.NotNull(await store.LoadAsync("g2"));

        await store.DeleteAsync("g2");
        Assert.Null(await store.LoadAsync("g2"));
    }

    [Fact]
    public async Task GetActiveGoals_Should_FilterPursuingAndPaused()
    {
        var store = CreateStore();
        await store.SaveAsync(new GoalState { GoalId = "pursuing", Objective = "a", Status = GoalStatus.Pursuing });
        await store.SaveAsync(new GoalState { GoalId = "paused", Objective = "b", Status = GoalStatus.Paused });
        await store.SaveAsync(new GoalState { GoalId = "achieved", Objective = "c", Status = GoalStatus.Achieved });
        await store.SaveAsync(new GoalState { GoalId = "unmet", Objective = "d", Status = GoalStatus.Unmet });

        var active = await store.GetActiveGoalsAsync();

        Assert.Equal(2, active.Count);
        Assert.Contains(active, s => s.GoalId == "pursuing");
        Assert.Contains(active, s => s.GoalId == "paused");
    }

    [Fact]
    public async Task Save_Overwrite_Should_UpdateExisting()
    {
        var store = CreateStore();

        await store.SaveAsync(new GoalState { GoalId = "g3", Objective = "v1", Status = GoalStatus.Pursuing, TokensUsed = 10 });
        await store.SaveAsync(new GoalState { GoalId = "g3", Objective = "v2", Status = GoalStatus.Achieved, TokensUsed = 20 });

        var loaded = await store.LoadAsync("g3");
        Assert.NotNull(loaded);
        Assert.Equal("v2", loaded!.Objective);
        Assert.Equal(GoalStatus.Achieved, loaded.Status);
        Assert.Equal(20, loaded.TokensUsed);
    }
}
