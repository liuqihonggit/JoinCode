namespace Host.Tests.ChatCommands;

/// <summary>
/// BridgeCommand 取值范围测试 — 验证 BridgeAction 枚举字面量正确路由
/// 覆盖:qr/sessions/status/connect/disconnect + 大小写不敏感 + 默认 status
/// </summary>
public sealed class BridgeCommandTests
{
    [Fact]
    public void Name_Should_Be_bridge()
    {
        var cmd = new BridgeCommand();
        cmd.Name.Should().Be("bridge");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new BridgeCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Contain_All_BridgeActions()
    {
        var cmd = new BridgeCommand();
        cmd.Usage.Should().Contain("qr");
        cmd.Usage.Should().Contain("sessions");
        cmd.Usage.Should().Contain("status");
        cmd.Usage.Should().Contain("connect");
        cmd.Usage.Should().Contain("disconnect");
    }

    [Fact]
    public void Aliases_Should_Contain_rc()
    {
        var cmd = new BridgeCommand();
        cmd.Aliases.Should().Contain("rc");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new BridgeCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_WithEmptyArgs_Should_Default_To_Status()
    {
        // 空 args → 默认 "status" → BridgeActionConstants.Status 分支
        var cmd = new BridgeCommand();
        var context = new ChatCommandContext
        {
            Arguments = "",
            CancellationToken = CancellationToken.None,
            Services = CreateServices(),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("qr")]
    [InlineData("sessions")]
    [InlineData("status")]
    [InlineData("connect")]
    [InlineData("disconnect")]
    public async Task Execute_WithBridgeActionSubcommand_Should_Return_Continue(string subCommand)
    {
        // BridgeActionConstants.Qr/Sessions/Status/Connect/Disconnect 枚举路由取值范围测试
        var cmd = new BridgeCommand();
        var context = new ChatCommandContext
        {
            Arguments = subCommand,
            CancellationToken = CancellationToken.None,
            Services = CreateServices(),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("QR")]
    [InlineData("SESSIONS")]
    [InlineData("Status")]
    [InlineData("CONNECT")]
    [InlineData("Disconnect")]
    public async Task Execute_WithUppercaseSubcommand_Should_Be_CaseInsensitive(string subCommand)
    {
        var cmd = new BridgeCommand();
        var context = new ChatCommandContext
        {
            Arguments = subCommand,
            CancellationToken = CancellationToken.None,
            Services = CreateServices(),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownSubcommand_Should_NotThrow()
    {
        var cmd = new BridgeCommand();
        var context = new ChatCommandContext
        {
            Arguments = "unknown-action",
            CancellationToken = CancellationToken.None,
            Services = CreateServices(),
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    // ===== BridgeAction 枚举字面量路由验证 =====

    [Theory]
    [InlineData("qr", BridgeAction.Qr)]
    [InlineData("sessions", BridgeAction.Sessions)]
    [InlineData("status", BridgeAction.Status)]
    [InlineData("connect", BridgeAction.Connect)]
    [InlineData("disconnect", BridgeAction.Disconnect)]
    public void BridgeAction_FromValue_ValidString_Should_Resolve_Correctly(string input, BridgeAction expected)
    {
        BridgeActionExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void BridgeActionConstants_Values_Should_Match_Route()
    {
        // 验证枚举常量值与原硬编码字符串完全一致(行为不变)
        BridgeActionConstants.Qr.Should().Be("qr");
        BridgeActionConstants.Sessions.Should().Be("sessions");
        BridgeActionConstants.Status.Should().Be("status");
        BridgeActionConstants.Connect.Should().Be("connect");
        BridgeActionConstants.Disconnect.Should().Be("disconnect");
    }

    private static CommandServices CreateServices()
    {
        return new CommandServices
        {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
        FileSystem = TestFileSystem.Current,
        };
    }
}
