
namespace Core.Goal.Tests;

public sealed class LeadMergeOrchestratorTests
{
    [Fact]
    public void ComputeMergeOrder_NoDeps_ShouldReturnAll()
    {
        var plan = CreatePlan([
            new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", OwnedFiles = ["a.cs"] },
            new SubTaskDefinition { Id = "sub_2", Title = "B", Description = "DB", OwnedFiles = ["b.cs"] }
        ]);
        var available = new HashSet<string> { "sub_1", "sub_2" };

        var order = LeadMergeOrchestrator.ComputeMergeOrder(plan, available);

        Assert.NotNull(order);
        Assert.Equal(2, order.Count);
    }

    [Fact]
    public void ComputeMergeOrder_WithDeps_ShouldReturnTopologicalOrder()
    {
        var plan = CreatePlan([
            new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", OwnedFiles = ["a.cs"] },
            new SubTaskDefinition { Id = "sub_2", Title = "B", Description = "DB", DependsOn = ["sub_1"], OwnedFiles = ["b.cs"] }
        ]);
        var available = new HashSet<string> { "sub_1", "sub_2" };

        var order = LeadMergeOrchestrator.ComputeMergeOrder(plan, available);

        Assert.NotNull(order);
        Assert.Equal(0, order.IndexOf("sub_1"));
        Assert.Equal(1, order.IndexOf("sub_2"));
    }

    [Fact]
    public void ComputeMergeOrder_PartialAvailable_ShouldOnlyIncludeAvailable()
    {
        var plan = CreatePlan([
            new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", OwnedFiles = ["a.cs"] },
            new SubTaskDefinition { Id = "sub_2", Title = "B", Description = "DB", DependsOn = ["sub_1"], OwnedFiles = ["b.cs"] },
            new SubTaskDefinition { Id = "sub_3", Title = "C", Description = "DC", OwnedFiles = ["c.cs"] }
        ]);
        var available = new HashSet<string> { "sub_1", "sub_3" };

        var order = LeadMergeOrchestrator.ComputeMergeOrder(plan, available);

        Assert.NotNull(order);
        Assert.Equal(2, order.Count);
        Assert.Contains("sub_1", order);
        Assert.Contains("sub_3", order);
    }

    [Fact]
    public void ComputeMergeOrder_CyclicDeps_ShouldReturnNull()
    {
        var plan = CreatePlan([
            new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", DependsOn = ["sub_2"], OwnedFiles = ["a.cs"] },
            new SubTaskDefinition { Id = "sub_2", Title = "B", Description = "DB", DependsOn = ["sub_1"], OwnedFiles = ["b.cs"] }
        ]);
        var available = new HashSet<string> { "sub_1", "sub_2" };

        var order = LeadMergeOrchestrator.ComputeMergeOrder(plan, available);

        Assert.Null(order);
    }

    [Fact]
    public void ComputeMergeOrder_DiamondDeps_ShouldReturnValidOrder()
    {
        var plan = CreatePlan([
            new SubTaskDefinition { Id = "sub_1", Title = "Base", Description = "D", OwnedFiles = ["base.cs"] },
            new SubTaskDefinition { Id = "sub_2", Title = "Left", Description = "D", DependsOn = ["sub_1"], OwnedFiles = ["left.cs"] },
            new SubTaskDefinition { Id = "sub_3", Title = "Right", Description = "D", DependsOn = ["sub_1"], OwnedFiles = ["right.cs"] },
            new SubTaskDefinition { Id = "sub_4", Title = "Top", Description = "D", DependsOn = ["sub_2", "sub_3"], OwnedFiles = ["top.cs"] }
        ]);
        var available = new HashSet<string> { "sub_1", "sub_2", "sub_3", "sub_4" };

        var order = LeadMergeOrchestrator.ComputeMergeOrder(plan, available);

        Assert.NotNull(order);
        Assert.Equal(4, order.Count);
        Assert.True(order.IndexOf("sub_1") < order.IndexOf("sub_2"));
        Assert.True(order.IndexOf("sub_1") < order.IndexOf("sub_3"));
        Assert.True(order.IndexOf("sub_2") < order.IndexOf("sub_4"));
        Assert.True(order.IndexOf("sub_3") < order.IndexOf("sub_4"));
    }

    [Fact]
    public void ComputeMergeOrder_EmptyAvailable_ShouldReturnEmpty()
    {
        var plan = CreatePlan([new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", OwnedFiles = ["a.cs"] }]);
        var available = new HashSet<string>();

        var order = LeadMergeOrchestrator.ComputeMergeOrder(plan, available);

        Assert.NotNull(order);
        Assert.Empty(order);
    }

    private static ClusterPlan CreatePlan(List<SubTaskDefinition> subTasks)
    {
        return new ClusterPlan
        {
            Objective = "test",
            Decomposition = DecompositionResult.Decomposable("test", subTasks),
            ExecutionOptions = new ClusterExecutionOptions()
        };
    }
}
