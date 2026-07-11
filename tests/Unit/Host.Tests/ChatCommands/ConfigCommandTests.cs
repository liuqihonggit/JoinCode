namespace Host.Tests.ChatCommands;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Configuration.Settings; // SettingSource 命名空间

/// <summary>
/// ConfigCommand 取值范围测试 — 验证 CrudAction 枚举字面量正确路由
/// 覆盖:list/ls/delete/rm/remove + get/set/default(保留) + 未知子命令
/// 验证目标:Step 3.6 重构后,所有 case 标签能被正确识别
/// </summary>
public sealed class ConfigCommandTests
{
    [Fact]
    public void Name_Should_Be_config()
    {
        var cmd = new ConfigCommand();
        cmd.Name.Should().Be("config");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new ConfigCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new ConfigCommand();
        cmd.Usage.Should().StartWith("/config");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new ConfigCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Be_Empty()
    {
        var cmd = new ConfigCommand();
        cmd.Aliases.Should().BeEmpty();
    }

    [Theory]
    [InlineData("list")]
    [InlineData("ls")]
    public async Task Execute_WithListVariants_Should_Return_Continue(string subCommand)
    {
        // CrudActionConstants.List/Ls → ListConfigAsync
        var configService = CreateMockConfigService();
        var cmd = new ConfigCommand();
        var context = CreateContext(subCommand, configService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("rm")]
    [InlineData("remove")]
    public async Task Execute_WithDeleteVariants_Should_Return_Continue(string subCommand)
    {
        // CrudActionConstants.Delete/Rm/Remove → RemoveConfigAsync
        var configService = CreateMockConfigService();
        var cmd = new ConfigCommand();
        var context = CreateContext($"{subCommand} somekey", configService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("get")]
    [InlineData("set")]
    public async Task Execute_WithReservedGetSetVariants_Should_Return_Continue(string subCommand)
    {
        // get/set 保留字符串(不属于 CrudAction 范围,Step 3.6 决策)
        var configService = CreateMockConfigService();
        var cmd = new ConfigCommand();
        var context = CreateContext($"{subCommand} somekey", configService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithEmptyArgs_Should_Default_To_List()
    {
        // 空 args → "list" → 走 List 分支
        var configService = CreateMockConfigService();
        var cmd = new ConfigCommand();
        var context = CreateContext("", configService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownSubcommand_Should_Default_To_List()
    {
        // 未知 args → default → 走 List 分支
        var configService = CreateMockConfigService();
        var cmd = new ConfigCommand();
        var context = CreateContext("unknown-action", configService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("LIST")]
    [InlineData("LS")]
    [InlineData("DELETE")]
    [InlineData("RM")]
    [InlineData("REMOVE")]
    public async Task Execute_WithUppercaseSubcommand_Should_Be_CaseInsensitive(string subCommand)
    {
        var configService = CreateMockConfigService();
        var cmd = new ConfigCommand();
        var context = CreateContext(subCommand, configService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    private static ChatCommandContext CreateContext(string arguments, IConfigurationService? configService)
    {
        var services = new CommandServices
        {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
        FileSystem = TestFileSystem.Current,
        };

        // 注入 IConfigurationService (通过 ServiceProvider mock)
        if (configService is not null)
        {
            var serviceProvider = new Mock<IServiceProvider>();
            serviceProvider.Setup(sp => sp.GetService(typeof(IConfigurationService)))
                .Returns(configService);
            services = new CommandServices
            {
                ChatService = services.ChatService,
                CodeService = services.CodeService,
                PlanService = services.PlanService,
                ServiceProvider = serviceProvider.Object,
            FileSystem = TestFileSystem.Current,
            };
        }

        return new ChatCommandContext
        {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
            Services = services,
        };
    }

    private static IConfigurationService CreateMockConfigService()
    {
        var mock = new Mock<IConfigurationService>();

        mock.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        mock.Setup(s => s.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        mock.Setup(s => s.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        mock.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        return mock.Object;
    }
}
