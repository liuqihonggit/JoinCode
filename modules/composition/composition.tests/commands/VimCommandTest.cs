namespace Composition.Tests.Commands;

/// <summary>
/// /vim 命令单元测试 — 覆盖 on/enable 启用(VimEngine.Enable+SetMode(Vim)+按键提示)/
/// off/disable 禁用/无参 toggle(Normal→Vim / Vim→Normal 互斥切换)/服务缺失降级不抛/
/// 大小写别名归一/命令元数据
/// <para>IVimEngine 与 IEditorModeService 经 DI 注入;继承 ToggleCommandBase 三分支骨架</para>
/// </summary>
[Collection("command-output")]
public sealed class VimCommandTest {
    private static (VimCommand Command, Mock<IVimEngine> Engine, Mock<IEditorModeService> ModeService, ChatCommandContext Context) Arrange(
        string arguments = "",
        bool withEngine = true,
        bool withModeService = true,
        EditorMode currentMode = EditorMode.Normal) {
        var engine = new Mock<IVimEngine>();
        var modeService = new Mock<IEditorModeService>();
        modeService.Setup(m => m.GetCurrentModeAsync()).ReturnsAsync(currentMode);
        var services = CommandTestHarness.CreateServices(
            configureDi: di => {
                if (withEngine) di.AddSingleton(engine.Object);
                if (withModeService) di.AddSingleton(modeService.Object);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new VimCommand(), engine, modeService, context);
    }

    [Fact]
    public async Task Execute_On_EnablesVimEngineAndSetsMode() {
        var (cmd, engine, modeService, context) = Arrange(arguments: "on");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        engine.Verify(e => e.Enable(), Times.Once, "on 应启用 Vim 引擎");
        modeService.Verify(m => m.SetMode(EditorMode.Vim), Times.Once, "on 应切换编辑模式为 Vim");
        output.Should().Contain("Vim 模式: 已启用");
        output.Should().Contain("使用 hjkl 移动，i 进入插入模式，Esc 返回普通模式");
    }

    [Fact]
    public async Task Execute_EnableAlias_SameAsOn() {
        var (cmd, engine, _, context) = Arrange(arguments: "enable");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.Enable(), Times.Once, "enable 别名应映射为开");
        output.Should().Contain("Vim 模式: 已启用");
    }

    [Fact]
    public async Task Execute_Off_DisablesVimEngineAndResetsMode() {
        var (cmd, engine, modeService, context) = Arrange(arguments: "off");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.Disable(), Times.Once, "off 应停用 Vim 引擎");
        modeService.Verify(m => m.SetMode(EditorMode.Normal), Times.Once, "off 应恢复标准编辑模式");
        output.Should().Contain("Vim 模式: 已禁用");
    }

    [Fact]
    public async Task Execute_DisableAlias_SameAsOff() {
        var (cmd, engine, _, context) = Arrange(arguments: "disable");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.Disable(), Times.Once, "disable 别名应映射为关");
        output.Should().Contain("Vim 模式: 已禁用");
    }

    [Fact]
    public async Task Execute_EmptyArgs_FromNormalMode_TogglesToVim() {
        var (cmd, engine, modeService, context) = Arrange(arguments: "", currentMode: EditorMode.Normal);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.Enable(), Times.Once, "当前 Normal 时 toggle 应切到 Vim");
        modeService.Verify(m => m.SetMode(EditorMode.Vim), Times.Once);
        output.Should().Contain("编辑模式: Vim");
    }

    [Fact]
    public async Task Execute_EmptyArgs_FromVimMode_TogglesToNormal() {
        var (cmd, engine, modeService, context) = Arrange(arguments: "", currentMode: EditorMode.Vim);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.Disable(), Times.Once, "当前 Vim 时 toggle 应切回 Normal");
        modeService.Verify(m => m.SetMode(EditorMode.Normal), Times.Once);
        output.Should().Contain("编辑模式: Normal (标准 readline 键绑定)");
    }

    [Fact]
    public async Task Execute_OnArg_IsCaseInsensitive() {
        var (cmd, engine, _, context) = Arrange(arguments: "ON");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        engine.Verify(e => e.Enable(), Times.Once, "参数应 ToLowerInvariant 归一匹配");
        output.Should().Contain("Vim 模式: 已启用");
    }

    [Fact]
    public async Task Execute_WithoutServices_StillReportsEnabledWithoutThrow() {
        var (cmd, engine, _, context) = Arrange(arguments: "on", withEngine: false, withModeService: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("服务缺失应静默降级而非抛异常");
        output.Should().Contain("Vim 模式: 已启用");
    }

    [Fact]
    public async Task Execute_Toggle_WithoutModeService_FallsBackToNormalDefault() {
        var (cmd, engine, _, context) = Arrange(arguments: "", withModeService: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        engine.Verify(e => e.Enable(), Times.Once,
            "编辑模式服务缺失时当前模式按 Normal 默认,toggle 应切到 Vim");
        output.Should().Contain("编辑模式: Vim");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new VimCommand();
        cmd.Name.Should().Be("vim");
        cmd.Usage.Should().Be("/vim [on|off]");
    }
}
