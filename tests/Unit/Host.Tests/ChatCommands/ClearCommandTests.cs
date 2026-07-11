namespace Host.Tests.ChatCommands;

public sealed class ClearCommandTests
{
    [Fact]
    public void Name_Should_Be_clear()
    {
        var cmd = new ClearCommand();
        cmd.Name.Should().Be("clear");
    }

    [Fact]
    public void Description_Should_Contain_清空()
    {
        var cmd = new ClearCommand();
        cmd.Description.Should().Contain("清空");
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new ClearCommand();
        cmd.Usage.Should().StartWith("/clear");
    }

    [Fact]
    public void Aliases_Should_Contain_reset_new_cls()
    {
        var cmd = new ClearCommand();
        cmd.Aliases.Should().Contain("reset");
        cmd.Aliases.Should().Contain("new");
        cmd.Aliases.Should().Contain("cls");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new ClearCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_Should_Clear_Directly()
    {
        // 对齐 TS: /clear 直接清除，无需 --force
        var chatService = new Mock<IChatService>();
        chatService.Setup(cs => cs.ClearHistoryAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var cmd = new ClearCommand();
        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = chatService.Object,
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        chatService.Verify(cs => cs.ClearHistoryAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}