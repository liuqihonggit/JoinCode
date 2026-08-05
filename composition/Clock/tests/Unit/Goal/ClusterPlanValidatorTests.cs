
namespace Core.Goal.Tests;

public sealed class ClusterPlanValidatorTests
{
    private readonly ClusterPlanValidator _sut = new();

    private static ClusterPlan CreatePlan(Action<List<SubTaskDefinition>> configureSubTasks)
    {
        var subTasks = new List<SubTaskDefinition>();
        configureSubTasks(subTasks);

        return new ClusterPlan
        {
            Objective = "test",
            Decomposition = DecompositionResult.Decomposable("test", subTasks),
            ExecutionOptions = new ClusterExecutionOptions()
        };
    }

    private static ClusterPlan CreatePlanWithComplexity(ComplexityLevel complexity, Action<List<SubTaskDefinition>> configureSubTasks)
    {
        var subTasks = new List<SubTaskDefinition>();
        configureSubTasks(subTasks);

        return new ClusterPlan
        {
            Objective = "test",
            Decomposition = DecompositionResult.Decomposable("test", subTasks, complexity),
            ExecutionOptions = new ClusterExecutionOptions()
        };
    }

    private static void AddNSubTasks(List<SubTaskDefinition> tasks, int n)
    {
        for (int i = 0; i < n; i++)
        {
            tasks.Add(new SubTaskDefinition { Id = $"sub_{i}", Title = $"T{i}", Description = $"D{i}", OwnedFiles = [$"file{i}.cs"] });
        }
    }

    [Fact]
    public void Validate_NotDecomposable_Should_Return_Invalid()
    {
        var plan = new ClusterPlan
        {
            Objective = "test",
            Decomposition = DecompositionResult.NotDecomposable("不可分解"),
            ExecutionOptions = new ClusterExecutionOptions()
        };

        var result = _sut.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("不可分解"));
    }

    [Fact]
    public void Validate_NoSubTasks_Should_Return_Invalid()
    {
        var plan = CreatePlan(_ => { });

        var result = _sut.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("没有子任务"));
    }

    [Fact]
    public void Validate_TooManySubTasks_Should_Return_Invalid()
    {
        var plan = CreatePlan(tasks =>
        {
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(new SubTaskDefinition { Id = $"sub_{i}", Title = $"T{i}", Description = $"D{i}", OwnedFiles = [$"file{i}.cs"] });
            }
        });

        var result = _sut.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("超过最大限制"));
    }

    [Fact]
    public void Validate_DuplicateIds_Should_Return_Invalid()
    {
        var plan = CreatePlan(tasks =>
        {
            tasks.Add(new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", OwnedFiles = ["a.cs"] });
            tasks.Add(new SubTaskDefinition { Id = "sub_1", Title = "B", Description = "DB", OwnedFiles = ["b.cs"] });
        });

        var result = _sut.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("重复"));
    }

    [Fact]
    public void Validate_EmptyId_Should_Return_Invalid()
    {
        var plan = CreatePlan(tasks =>
        {
            tasks.Add(new SubTaskDefinition { Id = "", Title = "A", Description = "DA", OwnedFiles = ["a.cs"] });
        });

        var result = _sut.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ID 不能为空"));
    }

    [Fact]
    public void Validate_SelfDependency_Should_Return_Invalid()
    {
        var plan = CreatePlan(tasks =>
        {
            tasks.Add(new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", DependsOn = ["sub_1"], OwnedFiles = ["a.cs"] });
        });

        var result = _sut.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("依赖自身"));
    }

    [Fact]
    public void Validate_MissingDependency_Should_Return_Invalid()
    {
        var plan = CreatePlan(tasks =>
        {
            tasks.Add(new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", DependsOn = ["sub_999"], OwnedFiles = ["a.cs"] });
        });

        var result = _sut.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("sub_999"));
    }

    [Fact]
    public void Validate_CyclicDependency_Should_Return_Invalid()
    {
        var plan = CreatePlan(tasks =>
        {
            tasks.Add(new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", DependsOn = ["sub_2"], OwnedFiles = ["a.cs"] });
            tasks.Add(new SubTaskDefinition { Id = "sub_2", Title = "B", Description = "DB", DependsOn = ["sub_1"], OwnedFiles = ["b.cs"] });
        });

        var result = _sut.Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("环"));
    }

    [Fact]
    public void Validate_FileOverlap_Should_Add_Warning()
    {
        var plan = CreatePlan(tasks =>
        {
            tasks.Add(new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", OwnedFiles = ["shared.cs"] });
            tasks.Add(new SubTaskDefinition { Id = "sub_2", Title = "B", Description = "DB", OwnedFiles = ["shared.cs"] });
        });

        var result = _sut.Validate(plan);

        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
        Assert.NotEmpty(result.FileConflicts);
        Assert.Equal("shared.cs", result.FileConflicts[0].FilePath);
    }

    [Fact]
    public void Validate_InvalidPriority_Should_Add_Warning()
    {
        var plan = CreatePlan(tasks =>
        {
            tasks.Add(new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", Priority = (SubTaskPriority)999, OwnedFiles = ["a.cs"] });
        });

        var result = _sut.Validate(plan);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("999"));
    }

    [Fact]
    public void Validate_ValidPlan_Should_Return_Valid()
    {
        var plan = CreatePlan(tasks =>
        {
            tasks.Add(new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", Priority = SubTaskPriority.High, OwnedFiles = ["a.cs"] });
            tasks.Add(new SubTaskDefinition { Id = "sub_2", Title = "B", Description = "DB", DependsOn = ["sub_1"], Priority = SubTaskPriority.Medium, OwnedFiles = ["b.cs"] });
        });

        var result = _sut.Validate(plan);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_NullPlan_Should_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.Validate(null!));
    }

    [Fact]
    public void Validate_MaxSubTasks_Boundary_Should_Pass()
    {
        var plan = CreatePlan(tasks =>
        {
            for (int i = 0; i < 8; i++)
            {
                tasks.Add(new SubTaskDefinition { Id = $"sub_{i}", Title = $"T{i}", Description = $"D{i}", OwnedFiles = [$"file{i}.cs"] });
            }
        });

        var result = _sut.Validate(plan);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_DiamondDependency_Should_Pass()
    {
        var plan = CreatePlan(tasks =>
        {
            tasks.Add(new SubTaskDefinition { Id = "sub_1", Title = "Base", Description = "D", OwnedFiles = ["base.cs"] });
            tasks.Add(new SubTaskDefinition { Id = "sub_2", Title = "Left", Description = "D", DependsOn = ["sub_1"], OwnedFiles = ["left.cs"] });
            tasks.Add(new SubTaskDefinition { Id = "sub_3", Title = "Right", Description = "D", DependsOn = ["sub_1"], OwnedFiles = ["right.cs"] });
            tasks.Add(new SubTaskDefinition { Id = "sub_4", Title = "Top", Description = "D", DependsOn = ["sub_2", "sub_3"], OwnedFiles = ["top.cs"] });
        });

        var result = _sut.Validate(plan);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_LowComplexity_TooManySubTasks_Should_Add_ComplexityMismatchWarning()
    {
        var plan = CreatePlanWithComplexity(ComplexityLevel.Low, tasks => AddNSubTasks(tasks, 6));

        var result = _sut.Validate(plan);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("complexity_mismatch") && w.Contains("Low"));
    }

    [Fact]
    public void Validate_MediumComplexity_TooFewSubTasks_Should_Add_ComplexityMismatchWarning()
    {
        var plan = CreatePlanWithComplexity(ComplexityLevel.Medium, tasks => AddNSubTasks(tasks, 3));

        var result = _sut.Validate(plan);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("complexity_mismatch") && w.Contains("Medium") && w.Contains("Low"));
    }

    [Fact]
    public void Validate_MediumComplexity_FiveSubTasks_Should_SuggestDowngradeToLow()
    {
        var plan = CreatePlanWithComplexity(ComplexityLevel.Medium, tasks => AddNSubTasks(tasks, 5));

        var result = _sut.Validate(plan);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("complexity_mismatch") && w.Contains("Low"));
    }

    [Fact]
    public void Validate_HighComplexity_TooFewSubTasks_Should_Add_ComplexityMismatchWarning()
    {
        var plan = CreatePlanWithComplexity(ComplexityLevel.High, tasks => AddNSubTasks(tasks, 8));

        var result = _sut.Validate(plan);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("complexity_mismatch") && w.Contains("High") && w.Contains("Medium"));
    }

    [Fact]
    public void Validate_LowComplexity_Consistent_Should_NoComplexityWarning()
    {
        var plan = CreatePlanWithComplexity(ComplexityLevel.Low, tasks => AddNSubTasks(tasks, 4));

        var result = _sut.Validate(plan);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("complexity_mismatch"));
    }

    [Fact]
    public void Validate_MediumComplexity_Consistent_Should_NoComplexityWarning()
    {
        var plan = CreatePlanWithComplexity(ComplexityLevel.Medium, tasks => AddNSubTasks(tasks, 7));

        var result = _sut.Validate(plan);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("complexity_mismatch"));
    }
}
