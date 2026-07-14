
namespace Core.Tests.Skills;

public sealed class SkillServiceAlignmentTests : IDisposable
{
    private readonly Mock<IFileOperationService> _fileOperationServiceMock;
    private readonly Mock<IQueryEngine> _queryEngineMock;
    private readonly Mock<IToolRegistry> _toolRegistryMock;

    public SkillServiceAlignmentTests()
    {
        _fileOperationServiceMock = new Mock<IFileOperationService>();
        _queryEngineMock = new Mock<IQueryEngine>();
        _toolRegistryMock = new Mock<IToolRegistry>();

        _fileOperationServiceMock.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(true);
        _fileOperationServiceMock.Setup(x => x.GetFiles(It.IsAny<string>(), "*.json", SearchOption.AllDirectories))
            .Returns(Array.Empty<string>());
        _fileOperationServiceMock.Setup(x => x.GetFiles(It.IsAny<string>(), "SKILL.md", SearchOption.AllDirectories))
            .Returns(Array.Empty<string>());
    }

    private SkillService CreateService()
    {
        var options = new SkillOptions { SkillsDirectory = "/test/skills", CacheExpiration = TimeSpan.FromMinutes(5) };

        var middlewares = new IMiddleware<Core.Skills.SkillContext>[]
        {
            new Core.Skills.SkillValidationMiddleware(),
            new Core.Skills.SkillTelemetryMiddleware(),
            new Core.Skills.SkillExecutionMiddleware(_queryEngineMock.Object, _toolRegistryMock.Object, new VariableResolver()),
            new MetricsMiddleware<Core.Skills.SkillContext>()
        };
        var pipeline = new MiddlewarePipeline<Core.Skills.SkillContext>(middlewares);

        return new SkillService(options, _fileOperationServiceMock.Object, pipeline);
    }

    public void Dispose() { }

    [Fact]
    public async Task Constructor_ShouldLoadAllBuiltInSkills()
    {
        var service = CreateService();
        var skills = await service.GetAvailableSkillsAsync();

        skills.Should().HaveCount(9);
        skills.Select(s => s.Name).Should().Contain(
            new[] { "verify", "debug", "batch", "stuck", "hunter", "loop", "remember", "simplify", "skillify" });
    }

    [Fact]
    public async Task SkillExists_AllBuiltInSkills_ShouldReturnTrue()
    {
        var service = CreateService();

        foreach (var name in new[] { "verify", "debug", "batch", "stuck", "hunter", "loop", "remember", "simplify", "skillify" })
        {
            service.SkillExists(name).Should().BeTrue($"built-in skill '{name}' should exist");
        }
    }

    [Fact]
    public async Task GetSkill_EachBuiltInSkill_ShouldHaveNonEmptyDescription()
    {
        var service = CreateService();

        foreach (var name in new[] { "verify", "debug", "batch", "stuck", "hunter", "loop", "remember", "simplify", "skillify" })
        {
            var skill = await service.GetSkillAsync(name);
            skill.Should().NotBeNull($"built-in skill '{name}' should be retrievable");
            skill!.Description.Should().NotBeNullOrEmpty($"built-in skill '{name}' should have a description");
        }
    }

    [Fact]
    public async Task GetSkill_EachBuiltInSkill_ShouldHaveStepsOrTemplate()
    {
        var service = CreateService();

        foreach (var name in new[] { "verify", "debug", "batch", "stuck", "hunter", "loop", "remember", "simplify", "skillify" })
        {
            var skill = await service.GetSkillAsync(name);
            skill.Should().NotBeNull();
            var hasSteps = skill!.Steps.Count > 0;
            var hasTemplate = !string.IsNullOrEmpty(skill.ContentTemplate);
            (hasSteps || hasTemplate).Should().BeTrue($"built-in skill '{name}' should have steps or content template");
        }
    }

    [Fact]
    public async Task SearchSkills_ByKeyword_ShouldReturnMatching()
    {
        var service = CreateService();
        var skills = await service.GetAvailableSkillsAsync();

        var matching = skills.Where(s => s.Name.Contains("debug") || s.Description.Contains("debug", StringComparison.OrdinalIgnoreCase)).ToList();
        matching.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SearchSkills_ByTag_ShouldReturnMatching()
    {
        var service = CreateService();
        var skills = await service.GetAvailableSkillsAsync();

        var withTags = skills.Where(s => s.Tags.Count > 0).ToList();
        if (withTags.Count > 0)
        {
            var tag = withTags[0].Tags[0];
            var matching = skills.Where(s => s.Tags.Contains(tag)).ToList();
            matching.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task RegisterCustomSkill_ShouldBeAvailable()
    {
        var service = CreateService();
        var customSkill = new SkillDefinition
        {
            Name = "custom_test",
            Description = "Custom test skill",
            Steps = new List<SkillStep>
            {
                new() { Id = "step1", Type = SkillStepType.Prompt, Prompt = "Test prompt" }
            }
        };

        service.RegisterSkill(customSkill);

        service.SkillExists("custom_test").Should().BeTrue();
        var retrieved = await service.GetSkillAsync("custom_test");
        retrieved.Should().NotBeNull();
        retrieved!.Description.Should().Be("Custom test skill");
    }

    [Fact]
    public async Task UnregisterSkill_BuiltIn_ShouldRemove()
    {
        var service = CreateService();

        service.SkillExists("debug").Should().BeTrue();
        service.UnregisterSkill("debug");
        service.SkillExists("debug").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_NonExistingSkill_ShouldReturnFailure()
    {
        var service = CreateService();
        var ctx = new ExecutionContext();

        var result = await service.ExecuteAsync("nonexistent_skill", null, ctx);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetAvailableSkills_AfterRegisterAndUnregister_ShouldReflectChanges()
    {
        var service = CreateService();
        var initialCount = (await service.GetAvailableSkillsAsync()).Count;

        service.RegisterSkill(new SkillDefinition
        {
            Name = "temp_skill",
            Description = "Temporary",
            Steps = new List<SkillStep> { new() { Id = "s1", Type = SkillStepType.Prompt, Prompt = "p" } }
        });

        (await service.GetAvailableSkillsAsync()).Should().HaveCount(initialCount + 1);

        service.UnregisterSkill("temp_skill");
        (await service.GetAvailableSkillsAsync()).Should().HaveCount(initialCount);
    }
}
