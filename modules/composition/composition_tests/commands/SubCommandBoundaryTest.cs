namespace Composition.Tests.Commands;

/// <summary>
/// 子命令词边界匹配测试（TASK130-A3）——整串取参时按前缀分派会误命中：
/// <c>/tag address</c> 命中 <c>add</c> 后把 <c>ress</c> 当标签名、<c>/workflows runner</c>
/// 命中 <c>run</c> 后把 <c>ner</c> 当工作流名，症状是「静默操作了另一个名字」而非报错。
/// </summary>
[Collection("command-output")]
public sealed class SubCommandBoundaryTest {
    [Theory]
    [InlineData("add", "add", true, "")]
    [InlineData("add foo", "add", true, "foo")]
    [InlineData("add  foo  ", "add", true, "foo")]
    [InlineData("ADD Foo", "add", true, "Foo")]
    [InlineData("add\tfoo", "add", true, "foo")]
    [InlineData("address", "add", false, "")]
    [InlineData("addx", "add", false, "")]
    [InlineData("installer.exe", "install", false, "")]
    [InlineData("runner", "run", false, "")]
    [InlineData("", "add", false, "")]
    [InlineData("ad", "add", false, "")]
    public void TryTakeSubCommand_RequiresWordBoundary(string args, string subCommand, bool expected, string expectedRest) {
        var matched = ChatCommandBase.TryTakeSubCommand(args, subCommand, out var rest);

        matched.Should().Be(expected);
        rest.Should().Be(expectedRest);
    }

    /// <summary>`/tag address` 不许被当成 add "ress"。</summary>
    [Fact]
    public async Task TagCommand_AddressArgument_DoesNotAddTag() {
        var tags = new Mock<ISessionTagService>();
        var services = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            SessionTagService = tags.Object,
        };
        var context = CommandTestHarness.CreateContext(services, arguments: "address");

        await CommandTestHarness.CaptureOutputAsync(() => new TagCommand().ExecuteAsync(context));

        tags.Verify(t => t.AddTag(It.IsAny<string>(), It.IsAny<string>()), Times.Never,
            "address 命中的是未知操作，不能把 ress 当标签名写进去");
    }

    /// <summary>`/tag add foo` 仍要正常添加。</summary>
    [Fact]
    public async Task TagCommand_AddWithName_AddsTag() {
        var tags = new Mock<ISessionTagService>();
        tags.Setup(t => t.AddTag(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        var services = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            SessionTagService = tags.Object,
        };
        var context = CommandTestHarness.CreateContext(services, arguments: "add foo");

        await CommandTestHarness.CaptureOutputAsync(() => new TagCommand().ExecuteAsync(context));

        tags.Verify(t => t.AddTag(It.IsAny<string>(), "foo"), Times.Once);
    }
}
