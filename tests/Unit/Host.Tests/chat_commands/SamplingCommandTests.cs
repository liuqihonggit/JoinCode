namespace Host.Tests.ChatCommands;

/// <summary>
/// /sampling 采样参数命令测试 — 温度/MaxTokens 写回 ExecutionSettingsProvider。
/// 回归背景（T4）：温度与 MaxTokens 只有 GUI 设置面板能改（session 方法写 provider），
/// CLI/TUI 无任何入口；新增共享 ChatCommand 让三端同享（对齐 /effort 的写回模式）。
/// </summary>
public sealed class SamplingCommandTests
{
    private sealed class StubSettingsProvider : IExecutionSettingsProvider
    {
        public EffortLevel EffortLevel { get; set; } = EffortLevel.Auto;
        public bool ThinkingEnabled { get; set; }
        public bool FastMode => false;
        public string? FastModelId => null;
        public float? Temperature { get; set; }
        public int? MaxTokens { get; set; }
    }

    private static (SamplingCommand Cmd, StubSettingsProvider Provider, ChatCommandContext Ctx) Create(string arguments)
    {
        var provider = new StubSettingsProvider();
        var services = new CommandServices
        {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ExecutionSettingsProvider = provider,
        };
        var ctx = new ChatCommandContext
        {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(services),
        };
        return (new SamplingCommand(), provider, ctx);
    }

    [Fact]
    public void Name_Is_Sampling()
    {
        var cmd = new SamplingCommand();
        cmd.Name.Should().Be(ChatCommandNameConstants.Sampling);
    }

    [Fact]
    public async Task Execute_WithTemperatureAndMaxTokens_WritesBoth()
    {
        var (cmd, provider, ctx) = Create("0.7 4096");

        var result = await cmd.ExecuteAsync(ctx);

        result.ShouldContinue.Should().BeTrue();
        provider.Temperature.Should().Be(0.7f);
        provider.MaxTokens.Should().Be(4096);
    }

    [Fact]
    public async Task Execute_WithTemperatureOnly_LeavesMaxTokensUntouched()
    {
        var (cmd, provider, ctx) = Create("0.3");
        provider.MaxTokens = 1024;

        await cmd.ExecuteAsync(ctx);

        provider.Temperature.Should().Be(0.3f);
        provider.MaxTokens.Should().Be(1024, "只给温度时不应动 MaxTokens");
    }

    [Fact]
    public async Task Execute_WithoutArgs_ShowsCurrent_AndWritesNothing()
    {
        var (cmd, provider, ctx) = Create("");
        provider.Temperature = 0.5f;

        var result = await cmd.ExecuteAsync(ctx);

        result.ShouldContinue.Should().BeTrue();
        provider.Temperature.Should().Be(0.5f, "无参数=查询模式");
    }

    [Fact]
    public async Task Execute_Unset_ClearsBoth()
    {
        var (cmd, provider, ctx) = Create("unset");
        provider.Temperature = 0.9f;
        provider.MaxTokens = 8192;

        await cmd.ExecuteAsync(ctx);

        provider.Temperature.Should().BeNull();
        provider.MaxTokens.Should().BeNull();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("2.5")]
    public async Task Execute_WithInvalidTemperature_DoesNotWrite(string args)
    {
        var (cmd, provider, ctx) = Create(args);

        var result = await cmd.ExecuteAsync(ctx);

        result.ShouldContinue.Should().BeTrue();
        provider.Temperature.Should().BeNull("无效温度不得写入");
        provider.MaxTokens.Should().BeNull();
    }
}
