namespace Composition.Tests.Commands;

/// <summary>
/// /tag 命令单元测试 — 覆盖空参+list 列表(空标签/多标签渲染)/add(成功/重复/缺参用法)/
/// remove(成功/不存在/缺参用法)/未知操作/服务缺失降级(list 提示/add+remove 静默)
/// <para>ISessionTagService 用 Moq 桩经本地构造 CommandServices 注入(SessionTagService 为 CommandServices 直接字段)</para>
/// </summary>
[Collection("command-output")]
public sealed class TagCommandTest {
    private static (TagCommand Command, Mock<ISessionTagService> TagService, ChatCommandContext Context) Arrange(
        string arguments = "",
        bool withService = true,
        IReadOnlyList<string>? tags = null,
        bool addResult = true,
        bool removeResult = true) {
        var tagService = new Mock<ISessionTagService>();
        tagService.Setup(t => t.GetTags(It.IsAny<string>())).Returns(tags ?? []);
        tagService.Setup(t => t.AddTag(It.IsAny<string>(), It.IsAny<string>())).Returns(addResult);
        tagService.Setup(t => t.RemoveTag(It.IsAny<string>(), It.IsAny<string>())).Returns(removeResult);

        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            SessionTagService = withService ? tagService.Object : null,
        };
        var context = CommandTestHarness.CreateContext(commandServices, arguments: arguments);
        return (new TagCommand(), tagService, context);
    }

    [Fact]
    public async Task Execute_EmptyArgs_EmptyTags_ShowsEmptyListWithUsage() {
        var (cmd, _, context) = Arrange(arguments: "", tags: []);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("会话标签:");
        output.Should().Contain("(暂无标签)");
        output.Should().Contain("使用 /tag add <name> 添加标签");
        output.Should().Contain("使用 /tag remove <name> 移除标签");
    }

    [Fact]
    public async Task Execute_ListArg_RendersAllTagsWithHashPrefix() {
        var (cmd, tagService, context) = Arrange(arguments: "list", tags: ["alpha", "beta"]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        tagService.Verify(t => t.GetTags("test-session"), Times.Once, "列表应查询当前会话标签");
        output.Should().Contain("#alpha");
        output.Should().Contain("#beta");
        output.Should().NotContain("(暂无标签)");
    }

    [Fact]
    public async Task Execute_ListArg_IsCaseInsensitive() {
        var (cmd, tagService, context) = Arrange(arguments: "LIST", tags: ["x"]);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("#x", "list 匹配应 OrdinalIgnoreCase");
    }

    [Fact]
    public async Task Execute_Add_NewTag_ShowsAdded() {
        var (cmd, tagService, context) = Arrange(arguments: "add alpha");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        tagService.Verify(t => t.AddTag("test-session", "alpha"), Times.Once, "add 应为当前会话添加标签");
        output.Should().Contain("已添加标签: alpha");
    }

    [Fact]
    public async Task Execute_Add_DuplicateTag_ShowsExists() {
        var (cmd, tagService, context) = Arrange(arguments: "add alpha", addResult: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("标签已存在: alpha");
    }

    [Fact]
    public async Task Execute_Add_WithoutName_ShowsUsage() {
        var (cmd, tagService, context) = Arrange(arguments: "add");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /tag add <tag_name>");
        tagService.Verify(t => t.AddTag(It.IsAny<string>(), It.IsAny<string>()), Times.Never,
            "缺标签名时不应发起添加");
    }

    [Fact]
    public async Task Execute_Remove_ExistingTag_ShowsRemoved() {
        var (cmd, tagService, context) = Arrange(arguments: "remove alpha");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        tagService.Verify(t => t.RemoveTag("test-session", "alpha"), Times.Once, "remove 应为当前会话移除标签");
        output.Should().Contain("已移除标签: alpha");
    }

    [Fact]
    public async Task Execute_Remove_MissingTag_ShowsNotFound() {
        var (cmd, _, context) = Arrange(arguments: "remove alpha", removeResult: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("标签不存在: alpha");
    }

    [Fact]
    public async Task Execute_Remove_WithoutName_ShowsUsage() {
        var (cmd, tagService, context) = Arrange(arguments: "remove");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /tag remove <tag_name>");
        tagService.Verify(t => t.RemoveTag(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Execute_AddTagWithSpaces_PreservesWholeName() {
        var (cmd, tagService, context) = Arrange(arguments: "add multi word tag");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        tagService.Verify(t => t.AddTag("test-session", "multi word tag"), Times.Once,
            "标签名含空格时应整段保留(add 之后的所有参数)");
        output.Should().Contain("已添加标签: multi word tag");
    }

    [Fact]
    public async Task Execute_UnknownAction_ShowsSupportedActions() {
        var (cmd, tagService, context) = Arrange(arguments: "bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知操作: bogus");
        output.Should().Contain("支持: add, remove, list");
        tagService.Verify(t => t.AddTag(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Execute_List_WithoutService_ShowsUninitializedHint() {
        var (cmd, _, context) = Arrange(arguments: "", withService: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("会话标签服务未初始化", "list 分支的服务缺失提示无条件输出");
    }

    [Fact]
    public async Task Execute_Add_WithoutService_NonInteractiveSilent() {
        var (cmd, _, context) = Arrange(arguments: "add alpha", withService: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("非交互环境服务缺失应静默降级而非抛异常");
        output.Should().NotContain("已添加标签");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new TagCommand();
        cmd.Name.Should().Be("tag");
        cmd.Usage.Should().Be("/tag [add|remove|list] [tag_name]");
    }
}
