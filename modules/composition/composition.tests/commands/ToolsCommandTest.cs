namespace Composition.Tests.Commands;

/// <summary>
/// /tools 命令单元测试 — 覆盖注册表缺失降级/空工具列表/多工具渲染(名称+描述+
/// 必需/可选参数标记)/无参数工具省略参数段
/// <para>IToolRegistry 用 Moq 桩经本地构造 CommandServices 注入(ToolRegistry 为 CommandServices 直接字段)</para>
/// </summary>
[Collection("command-output")]
public sealed class ToolsCommandTest {
    private static ToolInfo CreateTool(
        string name,
        string description = "做某事",
        Dictionary<string, ToolSchemaProperty>? properties = null,
        List<string>? required = null) => new() {
        Name = name,
        Description = description,
        InputSchema = new ToolSchema {
            Properties = properties ?? new Dictionary<string, ToolSchemaProperty>(),
            Required = required ?? [],
        },
    };

    private static (ToolsCommand Command, Mock<IToolRegistry> Registry, ChatCommandContext Context) Arrange(
        IReadOnlyList<ToolInfo>? tools = null,
        bool withRegistry = true) {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tools ?? []);

        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            ToolRegistry = withRegistry ? registry.Object : null,
        };
        var context = CommandTestHarness.CreateContext(commandServices);
        return (new ToolsCommand(), registry, context);
    }

    [Fact]
    public async Task Execute_WithoutRegistry_ShowsUnavailable() {
        var (cmd, registry, context) = Arrange(withRegistry: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("工具注册表不可用。");
        registry.Verify(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_EmptyTools_ShowsNoToolsMessage() {
        var (cmd, registry, context) = Arrange(tools: []);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("没有注册的工具。");
        registry.Verify(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_WithTools_RendersToolNamesAndDescriptions() {
        var tools = new List<ToolInfo> {
            CreateTool("read_file", "读取文件"),
            CreateTool("write_file", "写入文件"),
        };
        var (cmd, _, context) = Arrange(tools: tools);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 可用工具 (2) ===");
        output.Should().Contain("read_file");
        output.Should().Contain("描述: 读取文件");
        output.Should().Contain("write_file");
        output.Should().Contain("描述: 写入文件");
    }

    [Fact]
    public async Task Execute_ToolWithParameters_MarksRequiredAndOptional() {
        var tools = new List<ToolInfo> {
            CreateTool("search", properties: new Dictionary<string, ToolSchemaProperty> {
                ["pattern"] = new() { Type = "string" },
                ["limit"] = new() { Type = "number" },
            }, required: ["pattern"]),
        };
        var (cmd, _, context) = Arrange(tools: tools);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("参数:");
        output.Should().Contain("- pattern: string (必需)");
        output.Should().Contain("- limit: number (可选)");
    }

    [Fact]
    public async Task Execute_ToolWithoutParameters_OmitsParameterSection() {
        var tools = new List<ToolInfo> { CreateTool("clock", "取时间") };
        var (cmd, _, context) = Arrange(tools: tools);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("clock");
        output.Should().NotContain("参数:", "无参数工具不应渲染参数段");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ToolsCommand();
        cmd.Name.Should().Be("tools");
        cmd.Usage.Should().Be("/tools");
        cmd.ExposeToMcp.Should().BeTrue();
    }
}
