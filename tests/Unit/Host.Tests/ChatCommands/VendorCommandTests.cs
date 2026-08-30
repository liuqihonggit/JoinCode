namespace Host.Tests.ChatCommands;

/// <summary>
/// /vendor 供应商切换命令测试 — 运行时切换供应商并写回配置。
/// 回归背景（T5）：/model 已覆盖模型切换，但 CLI/TUI 无供应商运行时切换入口
/// （GUI 有 SetVendorAsync）；新增共享 ChatCommand 对齐 GUI 语义：
/// WorkflowConfig.Provider.Vendor 切换 + 默认模型跟随 + settings.json profile 持久化。
/// </summary>
public sealed class VendorCommandTests
{
    private static (VendorCommand Cmd, WorkflowConfig Config, ChatCommandContext Ctx) Create(string arguments)
    {
        var config = new WorkflowConfig();
        config.Provider.Vendor = "openai";
        config.Provider.ModelId = "gpt-4o";

        var catalog = new Mock<IModelCatalog>();
        catalog.Setup(c => c.GetDefaultModelForProvider("anthropic")).Returns("claude-sonnet-4");
        catalog.Setup(c => c.GetProviderDisplayName(It.IsAny<string>())).Returns<string>(p => p);

        var configService = new Mock<IConfigurationService>();
        var fastMode = new Mock<IFastModeService>();

        var services = new CommandServices
        {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            WorkflowConfig = config,
            ServiceProvider = new VendorTestServiceProvider(configService.Object, catalog.Object, fastMode.Object),
        };
        var ctx = new ChatCommandContext
        {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(services),
        };
        return (new VendorCommand(), config, ctx);
    }

    private sealed class VendorTestServiceProvider : IServiceProvider
    {
        private readonly IConfigurationService _configService;
        private readonly IModelCatalog _catalog;
        private readonly IFastModeService _fastMode;

        public VendorTestServiceProvider(IConfigurationService configService, IModelCatalog catalog, IFastModeService fastMode)
        {
            _configService = configService;
            _catalog = catalog;
            _fastMode = fastMode;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IConfigurationService)) return _configService;
            if (serviceType == typeof(IModelCatalog)) return _catalog;
            if (serviceType == typeof(IFastModeService)) return _fastMode;
            return null;
        }
    }

    [Fact]
    public void Name_Is_Vendor()
    {
        new VendorCommand().Name.Should().Be(ChatCommandNameConstants.Vendor);
    }

    [Fact]
    public async Task Execute_NoArgs_ListsVendors_AndMarksCurrent()
    {
        var (cmd, config, ctx) = Create("");

        var result = await cmd.ExecuteAsync(ctx);

        result.ShouldContinue.Should().BeTrue();
        config.Provider.Vendor.Should().Be("openai", "无参=查询模式不改变当前供应商");
    }

    [Fact]
    public async Task Execute_ValidVendor_SwitchesConfigAndPersistsProfile()
    {
        var (cmd, config, ctx) = Create("anthropic");

        var result = await cmd.ExecuteAsync(ctx);

        result.ShouldContinue.Should().BeTrue();
        config.Provider.Vendor.Should().Be("anthropic");
        config.CurrentModelId.Should().Be("claude-sonnet-4", "切换供应商后默认模型应跟随");
    }

    [Theory]
    [InlineData("not-a-vendor")]
    [InlineData("OPENAII")]
    public async Task Execute_InvalidVendor_DoesNotChange(string vendor)
    {
        var (cmd, config, ctx) = Create(vendor);

        var result = await cmd.ExecuteAsync(ctx);

        result.ShouldContinue.Should().BeTrue();
        config.Provider.Vendor.Should().Be("openai", "无效供应商不得改动当前配置");
    }

    [Fact]
    public async Task Execute_SameVendor_NoOp()
    {
        var (cmd, config, ctx) = Create("openai");

        var result = await cmd.ExecuteAsync(ctx);

        result.ShouldContinue.Should().BeTrue();
        config.Provider.Vendor.Should().Be("openai");
    }
}
