namespace Composition.Tests.Commands;

/// <summary>
/// /skills 命令单元测试 — 覆盖空参列表(空技能引导创建/多技能按来源分组渲染)/
/// info 详情(按全名/短名匹配)/info 未知名/info 缺参用法/注册表缺失降级
/// <para>ChatCommandRegistry 真实例 + CustomChatCommand 包装 CustomCommand 经 harness 注入;
/// 测试环境非交互走纯文本列表回退分支</para>
/// </summary>
[Collection("command-output")]
public sealed class SkillsCommandTest {
    private static CustomCommand CreateSkill(
        string name,
        string content = "技能内容 $ARGUMENTS",
        string description = "示例技能",
        string? ns = null,
        string sourcePath = @"C:\proj\.trae\commands\demo.md") => new() {
        Name = name,
        Content = content,
        Description = description,
        SourcePath = sourcePath,
        Namespace = ns,
    };

    private static (SkillsCommand Command, ChatCommandContext Context) Arrange(
        string arguments = "",
        params CustomCommand[] skills) {
        var registry = new ChatCommandRegistry();
        foreach (var skill in skills) {
            registry.Register(new CustomChatCommand(skill));
        }
        var services = CommandTestHarness.CreateServices(commandRegistry: registry);
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new SkillsCommand(), context);
    }

    [Fact]
    public async Task Execute_EmptyArgs_NoSkills_ShowsCreationGuide() {
        var (cmd, context) = Arrange(arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("当前无自定义技能");
        output.Should().Contain("项目级: .trae/commands/<skill-name>.md");
        output.Should().Contain("用户级: ~/.jcc/commands/<skill-name>.md");
    }

    [Fact]
    public async Task Execute_EmptyArgs_WithSkills_ListsGroupedCommands() {
        var (cmd, context) = Arrange(
            arguments: "",
            CreateSkill("demo", description: "演示技能"),
            CreateSkill("deploy", description: "部署技能", ns: "ops", sourcePath: @"C:\proj\.trae\commands\ops\deploy.md"));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 可用技能 ===");
        output.Should().Contain("/demo - 演示技能");
        output.Should().Contain("/ops:deploy - 部署技能");
        output.Should().Contain("使用 /skills info <skill-name> 查看详细信息");
    }

    [Fact]
    public async Task Execute_Info_WithoutName_ShowsUsage() {
        var (cmd, context) = Arrange(arguments: "info");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /skills info <skill-name>");
    }

    [Fact]
    public async Task Execute_Info_WithFullName_ShowsSkillDetail() {
        var (cmd, context) = Arrange(
            arguments: "info ops:deploy",
            CreateSkill("deploy", content: "部署步骤内容", ns: "ops"));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("名称: ops:deploy");
        output.Should().Contain("来源: ");
        output.Should().Contain("内容预览:");
        output.Should().Contain("部署步骤内容");
    }

    [Fact]
    public async Task Execute_Info_WithShortName_MatchesByNameToo() {
        var (cmd, context) = Arrange(
            arguments: "info deploy",
            CreateSkill("deploy", ns: "ops"));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("名称: ops:deploy", "短名也应能命中带命名空间的技能");
    }

    [Fact]
    public async Task Execute_Info_NameIsCaseInsensitive() {
        var (cmd, context) = Arrange(
            arguments: "info DEMO",
            CreateSkill("demo"));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("名称: demo");
    }

    [Fact]
    public async Task Execute_Info_UnknownSkill_ShowsUnknownHint() {
        var (cmd, context) = Arrange(
            arguments: "info nope",
            CreateSkill("demo"));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知技能: nope");
    }

    [Fact]
    public async Task Execute_LongContent_IsTruncatedInPreview() {
        var longContent = new string('x', 300);
        var (cmd, context) = Arrange(
            arguments: "info demo",
            CreateSkill("demo", content: longContent));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain(new string('x', 200) + "...", "超过 200 字符的内容预览应截断加省略号");
    }

    [Fact]
    public async Task Execute_WithoutRegistry_ShowsEmptyGuide() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => new SkillsCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前无自定义技能", "注册表缺失视为无技能,输出创建引导");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new SkillsCommand();
        cmd.Name.Should().Be("skills");
        cmd.Usage.Should().Be("/skills [info <skill-name>]");
        cmd.Aliases.Should().Contain("skill");
    }
}
