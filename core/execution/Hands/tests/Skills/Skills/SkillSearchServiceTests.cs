namespace Core.Tests.Services.Skills;

public sealed class SkillSearchServiceTests
{
    private readonly Mock<ISkillService> _skillServiceMock;
    private readonly SkillSearchService _service;

    public SkillSearchServiceTests()
    {
        _skillServiceMock = new Mock<ISkillService>();
        _service = new SkillSearchService(_skillServiceMock.Object);
    }

    private static SkillDefinition CreateSkill(string name, string description, List<string>? tags = null, string? ns = null)
    {
        return new SkillDefinition
        {
            Name = name,
            Description = description,
            Tags = tags ?? new List<string>(),
            Namespace = ns,
            Steps = new List<SkillStep>()
        };
    }

    private void SetupSkills(params SkillDefinition[] skills)
    {
        _skillServiceMock.Setup(s => s.GetAvailableSkillsAsync(default)).ReturnsAsync(skills.ToList().AsReadOnly());
    }

    [Fact]
    public async Task SearchAsync_WithKeyword_ReturnsMatchingSkills()
    {
        SetupSkills(
            CreateSkill("batch", "Batch processing skill", new List<string> { "automation" }, "core"),
            CreateSkill("debug", "Debug and diagnose issues", new List<string> { "debugging" }, "core"),
            CreateSkill("verify", "Verify code quality", new List<string> { "quality" }, "core")
        );

        var query = new SkillSearchQuery { Keyword = "batch" };
        var results = await _service.SearchAsync(query).ConfigureAwait(true);

        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.SkillName == "batch");
    }

    [Fact]
    public async Task SearchAsync_WithNoMatch_ReturnsEmpty()
    {
        SetupSkills(
            CreateSkill("batch", "Batch processing skill", new List<string> { "automation" }, "core")
        );

        var query = new SkillSearchQuery { Keyword = "nonexistent" };
        var results = await _service.SearchAsync(query).ConfigureAwait(true);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task RecommendAsync_WithContext_ReturnsRelevantSkills()
    {
        SetupSkills(
            CreateSkill("debug", "Debug and diagnose issues in code", new List<string> { "debugging", "diagnosis" }, "core"),
            CreateSkill("verify", "Verify code quality and standards", new List<string> { "quality", "testing" }, "core")
        );

        var results = await _service.RecommendAsync("debug code issues", maxResults: 5).ConfigureAwait(true);

        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RecommendAsync_WithEmptyContext_ThrowsArgumentException()
    {
        var act = () => _service.RecommendAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task SearchAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        var act = () => _service.SearchAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task SearchAsync_ByTag_ReturnsMatchingSkills()
    {
        SetupSkills(
            CreateSkill("batch", "Batch processing", new List<string> { "automation" }, "core"),
            CreateSkill("debug", "Debug issues", new List<string> { "debugging" }, "core")
        );

        var query = new SkillSearchQuery { Tags = new List<string> { "automation" } };
        var results = await _service.SearchAsync(query).ConfigureAwait(true);

        results.Should().Contain(r => r.SkillName == "batch");
    }

    [Fact]
    public void Constructor_WithNullSkillService_ThrowsArgumentNullException()
    {
        var act = () => new SkillSearchService(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
