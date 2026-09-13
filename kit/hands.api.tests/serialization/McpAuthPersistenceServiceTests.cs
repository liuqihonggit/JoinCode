namespace Hands.Tests.Serialization;

public sealed class McpAuthPersistenceServiceTests
{
    private readonly Mock<IConfigurationService> _configMock;
    private readonly McpAuthPersistenceService _service;

    public McpAuthPersistenceServiceTests()
    {
        _configMock = new Mock<IConfigurationService>();
        _service = new McpAuthPersistenceService(_configMock.Object);
    }

    [Fact]
    public async Task SaveAsync_WithoutConfigService_DoesNothing()
    {
        var service = new McpAuthPersistenceService();

        var act = async () => await service.SaveAsync("name", "type", "data").ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SaveAsync_NewEntry_AddsToConfig()
    {
        _configMock.Setup(c => c.GetAsync("mcp.auth_entries", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        await _service.SaveAsync("auth1", "apiKey", "secret").ConfigureAwait(true);

        _configMock.Verify(c => c.SetAsync("mcp.auth_entries", It.Is<string>(s => s.Contains("auth1") && s.Contains("secret")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_ExistingEntry_UpdatesIt()
    {
        var existing = JsonSerializer.Serialize(new List<AuthConfigEntry>
        {
            new() { Name = "auth1", AuthType = "oldType", Data = "oldData", SavedAt = DateTime.UtcNow.AddDays(-1) }
        }, AuthEntryContext.Default.ListAuthConfigEntry);
        _configMock.Setup(c => c.GetAsync("mcp.auth_entries", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        await _service.SaveAsync("auth1", "newType", "newData").ConfigureAwait(true);

        _configMock.Verify(c => c.SetAsync("mcp.auth_entries", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadAsync_ExistingEntry_ReturnsEntry()
    {
        var existing = JsonSerializer.Serialize(new List<AuthConfigEntry>
        {
            new() { Name = "auth1", AuthType = "apiKey", Data = "secret", SavedAt = DateTime.UtcNow }
        }, AuthEntryContext.Default.ListAuthConfigEntry);
        _configMock.Setup(c => c.GetAsync("mcp.auth_entries", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await _service.LoadAsync("auth1").ConfigureAwait(true);

        result.Should().NotBeNull();
        result!.Name.Should().Be("auth1");
        result.Data.Should().Be("secret");
    }

    [Fact]
    public async Task LoadAsync_MissingEntry_ReturnsNull()
    {
        _configMock.Setup(c => c.GetAsync("mcp.auth_entries", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var result = await _service.LoadAsync("missing").ConfigureAwait(true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_WithoutConfigService_ReturnsNull()
    {
        var service = new McpAuthPersistenceService();

        var result = await service.LoadAsync("auth1").ConfigureAwait(true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_ReturnsAllEntries()
    {
        var existing = JsonSerializer.Serialize(new List<AuthConfigEntry>
        {
            new() { Name = "auth1", AuthType = "apiKey", Data = "secret", SavedAt = DateTime.UtcNow },
            new() { Name = "auth2", AuthType = "oauth", Data = "token", SavedAt = DateTime.UtcNow }
        }, AuthEntryContext.Default.ListAuthConfigEntry);
        _configMock.Setup(c => c.GetAsync("mcp.auth_entries", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await _service.ListAsync().ConfigureAwait(true);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAsync_WithoutConfigService_ReturnsEmpty()
    {
        var service = new McpAuthPersistenceService();

        var result = await service.ListAsync().ConfigureAwait(true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveAsync_ExistingEntry_RemovesIt()
    {
        var existing = JsonSerializer.Serialize(new List<AuthConfigEntry>
        {
            new() { Name = "auth1", AuthType = "apiKey", Data = "secret", SavedAt = DateTime.UtcNow }
        }, AuthEntryContext.Default.ListAuthConfigEntry);
        _configMock.Setup(c => c.GetAsync("mcp.auth_entries", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        await _service.RemoveAsync("auth1").ConfigureAwait(true);

        _configMock.Verify(c => c.SetAsync("mcp.auth_entries", It.Is<string>(s => !s.Contains("auth1")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_WithoutConfigService_DoesNothing()
    {
        var service = new McpAuthPersistenceService();

        var act = async () => await service.RemoveAsync("auth1").ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_ReturnsNullAndDoesNotThrow()
    {
        _configMock.Setup(c => c.GetAsync("mcp.auth_entries", It.IsAny<CancellationToken>())).ReturnsAsync("not json");

        var result = await _service.LoadAsync("auth1").ConfigureAwait(true);

        result.Should().BeNull();
    }
}
