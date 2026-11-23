namespace Composition.Tests.Commands;

/// <summary>
/// /feedback 命令单元测试 — 覆盖空参输入引导/有参脱敏保存(feedback 目录创建+markdown 落盘+
/// 成功渲染)/保存路径提示
/// <para>写入经 InMemoryFileSystem(0磁盘IO),反馈目录为 AppDataConstants.Paths.JccDirectory 派生路径</para>
/// </summary>
[Collection("command-output")]
public sealed class FeedbackCommandTest {
    private static (FeedbackCommand Command, ChatCommandContext Context, IFileSystem Fs) Arrange(string arguments = "") {
        var fs = TestFileSystem.Current;
        var services = CommandTestHarness.CreateServices(fileSystem: fs);
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new FeedbackCommand(), context, fs);
    }

    [Fact]
    public async Task Execute_EmptyArgs_ShowsInputGuide() {
        var (cmd, context, _) = Arrange(arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("反馈");
        output.Should().Contain("请输入您的反馈内容:");
        output.Should().NotContain("已保存到:");
    }

    [Fact]
    public async Task Execute_WithFeedbackText_SavesMarkdownFile() {
        var (cmd, context, fs) = Arrange(arguments: "界面加载有点慢");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        var feedbackDir = Path.Combine(AppDataConstants.Paths.JccDirectory, "feedback");
        fs.DirectoryExists(feedbackDir).Should().BeTrue("反馈目录应被创建");
        var files = fs.GetFiles(feedbackDir, "feedback_*.md", System.IO.SearchOption.TopDirectoryOnly);
        files.Should().HaveCount(1, "应恰好保存一个反馈文件");
        var content = await fs.ReadAllTextAsync(files[0], CancellationToken.None);
        content.Should().Contain("# 反馈");
        content.Should().Contain("**时间**:");
        content.Should().Contain("界面加载有点慢");
    }

    [Fact]
    public async Task Execute_WithFeedbackText_ShowsSuccessAndSavedPath() {
        var (cmd, context, _) = Arrange(arguments: "功能建议：增加主题切换");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("反馈已提交");
        output.Should().Contain("功能建议：增加主题切换");
        output.Should().Contain("已保存到:");
        output.Should().Contain("feedback");
    }

    [Fact]
    public async Task Execute_TrimsSurroundingWhitespace() {
        var (cmd, context, fs) = Arrange(arguments: "   带空格的反馈   ");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("反馈已提交", "Trim 后非空应走保存主路径");
        output.Should().Contain("带空格的反馈");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new FeedbackCommand();
        cmd.Name.Should().Be("feedback");
        cmd.Usage.Should().Be("/feedback [反馈内容]");
        cmd.Aliases.Should().Contain("bug");
        cmd.IsHidden.Should().BeTrue();
    }
}
