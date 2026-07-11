
namespace Core.Tests.Skills;

public class SkillServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IFileOperationService> _fileOperationServiceMock;
    private readonly Mock<IQueryEngine> _queryEngineMock;
    private readonly Mock<IToolRegistry> _toolRegistryMock;

    public SkillServiceTests()
    {
        _tempDir = "/test/skills";

        _fileOperationServiceMock = new Mock<IFileOperationService>();
        _queryEngineMock = new Mock<IQueryEngine>();
        _toolRegistryMock = new Mock<IToolRegistry>();

        SetupFileOperationService();
    }

    private void SetupFileOperationService()
    {
        _fileOperationServiceMock.Setup(x => x.DirectoryExists(_tempDir)).Returns(true);
        _fileOperationServiceMock.Setup(x => x.GetFiles(_tempDir, "*.json", SearchOption.AllDirectories))
            .Returns(Array.Empty<string>());
        _fileOperationServiceMock.Setup(x => x.GetFiles(_tempDir, "SKILL.md", SearchOption.AllDirectories))
            .Returns(Array.Empty<string>());
    }

    private SkillService CreateService()
    {
        var options = new SkillOptions
        {
            SkillsDirectory = _tempDir,
            CacheExpiration = TimeSpan.FromMinutes(5)
        };

        var middlewares = new IMiddleware<Core.Skills.SkillContext>[]
        {
            new Core.Skills.SkillValidationMiddleware(),
            new Core.Skills.SkillTelemetryMiddleware(),
            new Core.Skills.SkillExecutionMiddleware(_queryEngineMock.Object, _toolRegistryMock.Object, new VariableResolver()),
            new MetricsMiddleware<Core.Skills.SkillContext>()
        };
        var pipeline = new MiddlewarePipeline<Core.Skills.SkillContext>(middlewares);

        return new SkillService(
            options,
            _fileOperationServiceMock.Object,
            pipeline);
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task Constructor_ShouldLoadBuiltInSkills()
    {
        var service = CreateService();
        var skills = await service.GetAvailableSkillsAsync().ConfigureAwait(true);

        skills.Should().NotBeEmpty();
        skills.Should().Contain(s => s.Name == "batch");
        skills.Should().Contain(s => s.Name == "debug");
        skills.Should().Contain(s => s.Name == "verify");
    }

    [Fact]
    public async Task GetSkill_ExistingSkill_ShouldReturnSkill()
    {
        var service = CreateService();
        var skill = await service.GetSkillAsync("batch").ConfigureAwait(true);

        skill.Should().NotBeNull();
        skill!.Name.Should().Be("batch");
    }

    [Fact]
    public async Task GetSkill_NonExistingSkill_ShouldReturnNull()
    {
        var service = CreateService();
        var skill = await service.GetSkillAsync("nonexistent").ConfigureAwait(true);

        skill.Should().BeNull();
    }

    [Fact]
    public void SkillExists_ExistingSkill_ShouldReturnTrue()
    {
        var service = CreateService();

        service.SkillExists("batch").Should().BeTrue();
    }

    [Fact]
    public void SkillExists_NonExistingSkill_ShouldReturnFalse()
    {
        var service = CreateService();

        service.SkillExists("nonexistent").Should().BeFalse();
    }

    [Fact]
    public async Task RegisterSkill_ShouldAddSkill()
    {
        var service = CreateService();
        var skill = new SkillDefinition
        {
            Name = "test_skill",
            Description = "Test skill",
            Steps = new List<SkillStep>
            {
                new() { Id = "step1", Type = SkillStepType.Prompt, Prompt = "Test prompt" }
            }
        };

        service.RegisterSkill(skill);

        service.SkillExists("test_skill").Should().BeTrue();
        (await service.GetSkillAsync("test_skill").ConfigureAwait(true))!.Description.Should().Be("Test skill");
    }

    [Fact]
    public void UnregisterSkill_ExistingSkill_ShouldRemoveSkill()
    {
        var service = CreateService();

        service.UnregisterSkill("batch");

        service.SkillExists("batch").Should().BeFalse();
    }

    [Fact]
    public void UnregisterSkill_NonExistingSkill_ShouldReturnFalse()
    {
        var service = CreateService();

        var result = service.UnregisterSkill("nonexistent");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_NonExistingSkill_ShouldReturnFailure()
    {
        var service = CreateService();
        var ctx = new SkillExecutionContext();

        var result = await service.ExecuteAsync("nonexistent", null, ctx).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("技能不存在");
    }

    [Fact]
    public async Task ExecuteAsync_WithNullSkillName_ShouldThrow()
    {
        var service = CreateService();
        var ctx = new SkillExecutionContext();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ExecuteAsync(null!, null, ctx)).ConfigureAwait(true);
    }

    [Fact]
    public async Task GetAvailableSkills_ShouldReturnAllSkills()
    {
        var service = CreateService();

        var skills = await service.GetAvailableSkillsAsync().ConfigureAwait(true);

        skills.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ReloadAsync_WithNullSkillName_ShouldReloadAll()
    {
        var service = CreateService();
        var ctx = new SkillExecutionContext();

        var result = await service.ReloadAsync(null, ctx).ConfigureAwait(true);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ReloadAsync_WithNonExistingSkill_ShouldReturnFalse()
    {
        var service = CreateService();
        var ctx = new SkillExecutionContext();

        var result = await service.ReloadAsync("nonexistent", ctx).ConfigureAwait(true);

        result.Should().BeFalse();
    }
}
