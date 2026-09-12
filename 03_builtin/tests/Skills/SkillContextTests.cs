namespace Core.Tests.Skills;

public sealed class SkillContextTests
{
    private static SkillDefinition CreateSkill(string name) => new()
    {
        Name = name,
        Description = "test description",
        Steps = []
    };

    [Fact]
    public void MetricsProperties_ShouldReturnExpectedValues()
    {
        var executionContext = new ExecutionContext(CancellationToken.None);
        var skill = CreateSkill("test-skill");
        var context = new SkillContext
        {
            SkillName = "test-skill",
            Skill = skill,
            ExecutionContext = executionContext
        };

        context.MetricsPrefix.Should().Be("skill.execute");
        context.IsMetricsSuccess.Should().BeFalse();
        context.MetricsDurationMs.Should().BeNull();
        context.BuildMetricsTags().Should().Contain("skill", "test-skill");
    }

    [Fact]
    public void MetricsProperties_WithResult_ShouldReturnSuccessAndDuration()
    {
        var executionContext = new ExecutionContext(CancellationToken.None);
        var skill = CreateSkill("test-skill");
        var stopwatch = Stopwatch.StartNew();
        var context = new SkillContext
        {
            SkillName = "test-skill",
            Skill = skill,
            ExecutionContext = executionContext,
            Result = SkillResult.SuccessResult("test-skill", "ok"),
            Stopwatch = stopwatch
        };

        stopwatch.Stop();

        context.IsMetricsSuccess.Should().BeTrue();
        context.MetricsDurationMs.Should().Be(stopwatch.ElapsedMilliseconds);
    }

    [Fact]
    public void Properties_ShouldBeSettable()
    {
        var executionContext = new ExecutionContext(CancellationToken.None);
        var skill = CreateSkill("test-skill");
        var parameters = new Dictionary<string, JsonElement>();
        var result = SkillResult.SuccessResult("test-skill", "ok");

        var context = new SkillContext
        {
            SkillName = "test-skill",
            Parameters = parameters,
            Skill = skill,
            ExecutionContext = executionContext,
            CancellationToken = CancellationToken.None,
            ValidationError = "validation failed",
            Result = result
        };

        context.SkillName.Should().Be("test-skill");
        context.Parameters.Should().BeSameAs(parameters);
        context.Skill.Should().BeSameAs(skill);
        context.ExecutionContext.Should().BeSameAs(executionContext);
        context.ValidationError.Should().Be("validation failed");
        context.Result.Should().BeSameAs(result);
    }
}
