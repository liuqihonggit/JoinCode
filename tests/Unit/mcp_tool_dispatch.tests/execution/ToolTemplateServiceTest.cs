namespace McpToolDispatch.Tests.Execution;

/// <summary>
/// ToolTemplateService 单元测试 — 验证模板加载、Schema构建、占位符替换
/// </summary>
public sealed class ToolTemplateServiceTest : IAsyncLifetime
{
    private InMemoryFileSystem _fs = null!;
    private ToolTemplateService _service = null!;

    public Task InitializeAsync()
    {
        _fs = new InMemoryFileSystem();
        _service = new ToolTemplateService(_fs);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task LoadTemplatesAsync_NoTemplatesDir_ReturnsEmptyList()
    {
        var templates = await _service.LoadTemplatesAsync();
        templates.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveTemplateAsync_And_LoadTemplatesAsync_RoundTrip()
    {
        var template = new ToolTemplate
        {
            Id = "test_tool",
            ToolName = "test_tool",
            Description = "A test tool",
            Kind = ToolKind.Mcp,
            GroupName = "test_group",
            Parameters =
            [
                new ToolTemplateParameter
                {
                    Name = "input",
                    Description = "Input parameter",
                    Type = "string",
                    Required = true
                }
            ],
            Execution = new ToolTemplateExecution
            {
                Type = "shell",
                Command = "echo",
                Args = ["{{input}}"],
                TimeoutSeconds = 10
            }
        };

        await _service.SaveTemplateAsync(template);
        var loaded = await _service.LoadTemplatesAsync();

        loaded.Should().HaveCount(1);
        loaded[0].ToolName.Should().Be("test_tool");
        loaded[0].Description.Should().Be("A test tool");
        loaded[0].Parameters.Should().HaveCount(1);
        loaded[0].Execution.Type.Should().Be("shell");
    }

    [Fact]
    public async Task ListTemplatesAsync_ReturnsCachedTemplates()
    {
        var template = new ToolTemplate
        {
            Id = "cached_tool",
            ToolName = "cached_tool",
            Description = "Cached tool",
            Parameters = [],
            Execution = new ToolTemplateExecution { Type = "shell", Command = "ls" }
        };

        await _service.SaveTemplateAsync(template);
        await _service.LoadTemplatesAsync();

        var listed = await _service.ListTemplatesAsync();
        listed.Should().HaveCount(1);
    }

    [Fact]
    public async Task LoadTemplatesAsync_InvalidJson_SkipsFile()
    {
        var templatesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".jcc", "tool-templates");
        _fs.CreateDirectory(templatesDir!);
        _fs.WriteAllText(Path.Combine(templatesDir, "bad.json"), "not valid json {{{");

        var templates = await _service.LoadTemplatesAsync();
        templates.Should().BeEmpty();
    }
}
