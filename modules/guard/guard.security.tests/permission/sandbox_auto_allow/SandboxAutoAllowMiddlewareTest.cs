namespace Guard.Tests.Security.Permission;

/// <summary>
/// SandboxAutoAllowMiddleware 行为测试 — 对齐基准 autoAllowBashIfSandboxed：
/// 只有「真的会进沙箱」的 Shell 调用才免确认；逃生阀 / 排除清单 / 无真实隔离能力一律回落 ask。
/// </summary>
public class SandboxAutoAllowMiddlewareTest {
    private static SandboxConfig EnabledConfig(bool autoAllow = true, bool allowUnsandboxed = true, IReadOnlyList<string>? excluded = null)
        => new() {
            Enabled = true,
            AutoAllowBashIfSandboxed = autoAllow,
            AllowUnsandboxedCommands = allowUnsandboxed,
            ExcludedCommands = excluded is null ? [] : [.. excluded],
        };

    private static PermissionCheckContext CreateContext(
        string toolName = "bash",
        string command = "ls -la",
        bool? dangerouslyDisable = null,
        Dictionary<string, JsonElement>? extraArguments = null) {
        var arguments = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement(command),
        };
        if (dangerouslyDisable.HasValue) {
            arguments["dangerously_disable_sandbox"] = JsonSerializer.SerializeToElement(dangerouslyDisable.Value);
        }
        if (extraArguments is not null) {
            foreach (var (key, value) in extraArguments) {
                arguments[key] = value;
            }
        }

        return new PermissionCheckContext {
            ToolName = toolName,
            Arguments = arguments,
            CurrentMode = PermissionMode.Auto,
            Config = PermissionConfig.CreateDefault(),
            AutoApprovedTools = [],
            AutoRejectedTools = [],
        };
    }

    private static readonly ISandboxProvider RealIsolationProvider = Mock.Of<ISandboxProvider>(
        p => p.Capabilities == (SandboxCapabilities.FileSystemIsolation | SandboxCapabilities.NetworkIsolation));

    private static readonly ISandboxManager RealIsolationManager = Mock.Of<ISandboxManager>(
        m => m.IsInSandbox && m.ActiveProvider == RealIsolationProvider);

    private static readonly ISandboxManager NotInSandboxManager = Mock.Of<ISandboxManager>(
        m => !m.IsInSandbox && m.ActiveProvider == RealIsolationProvider);

    private static readonly ISandboxProvider PathOnlyProvider = Mock.Of<ISandboxProvider>(
        p => p.Capabilities == (SandboxCapabilities.FileSystemIsolation | SandboxCapabilities.ProcessIsolation));

    private static readonly ISandboxManager PathOnlyManager = Mock.Of<ISandboxManager>(
        m => m.IsInSandbox && m.ActiveProvider == PathOnlyProvider);

    private static async Task<(SandboxAutoAllowMiddleware Middleware, PermissionCheckContext Context, bool NextCalled)> InvokeAsync(
        SandboxConfig config, PermissionCheckContext context, ISandboxManager? manager) {
        var middleware = new SandboxAutoAllowMiddleware(new WorkflowConfig { Sandbox = config }, manager);
        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        return (middleware, context, nextCalled);
    }

    [Fact]
    public async Task Given_总开关关闭_When_Invoke_Then_走后续判定() {
        var context = CreateContext();
        var (_, ctx, nextCalled) = await InvokeAsync(new SandboxConfig { Enabled = false, AutoAllowBashIfSandboxed = true }, context, RealIsolationManager);

        nextCalled.Should().BeTrue();
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public async Task Given_autoAllow关闭_When_Invoke_Then_走后续判定() {
        var context = CreateContext();
        var (_, ctx, nextCalled) = await InvokeAsync(EnabledConfig(autoAllow: false), context, RealIsolationManager);

        nextCalled.Should().BeTrue();
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public async Task Given_未进入沙箱_When_Invoke_Then_走后续判定() {
        var context = CreateContext();
        var (_, ctx, nextCalled) = await InvokeAsync(EnabledConfig(), context, NotInSandboxManager);

        nextCalled.Should().BeTrue();
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public async Task Given_提供器无网络隔离_When_Invoke_Then_不免确认() {
        var context = CreateContext();
        var (_, ctx, nextCalled) = await InvokeAsync(EnabledConfig(), context, PathOnlyManager);

        nextCalled.Should().BeTrue("只有路径/文件隔离、没有网络隔离的提供器不构成基准意义上的沙箱");
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public async Task Given_真隔离且命令会进沙箱_When_Invoke_Then_直接批准() {
        var context = CreateContext();
        var (_, ctx, nextCalled) = await InvokeAsync(EnabledConfig(), context, RealIsolationManager);

        nextCalled.Should().BeFalse();
        ctx.Result.Should().NotBeNull();
        var result = ctx.Result ?? throw new InvalidOperationException("expected approved result");
        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Given_排除清单命中_When_Invoke_Then_不免确认() {
        var context = CreateContext(command: "docker ps");
        var (_, ctx, nextCalled) = await InvokeAsync(EnabledConfig(excluded: ["docker:*"]), context, RealIsolationManager);

        nextCalled.Should().BeTrue("排除清单命令不进沙箱，不能获得免确认收益");
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public async Task Given_逃生阀且策略允许_When_Invoke_Then_不免确认() {
        var context = CreateContext(dangerouslyDisable: true);
        var (_, ctx, nextCalled) = await InvokeAsync(EnabledConfig(allowUnsandboxed: true), context, RealIsolationManager);

        nextCalled.Should().BeTrue("会跳出沙箱的命令仍必须走 ask 规则");
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public async Task Given_逃生阀但策略禁止_When_Invoke_Then_仍批准() {
        var context = CreateContext(dangerouslyDisable: true);
        var (_, ctx, nextCalled) = await InvokeAsync(EnabledConfig(allowUnsandboxed: false), context, RealIsolationManager);

        nextCalled.Should().BeFalse();
        var result = ctx.Result ?? throw new InvalidOperationException("expected approved result");
        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task Given_非Shell工具_When_Invoke_Then_走后续判定() {
        var context = CreateContext(toolName: "file_read");
        var (_, ctx, nextCalled) = await InvokeAsync(EnabledConfig(), context, RealIsolationManager);

        nextCalled.Should().BeTrue();
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public async Task Given_无沙箱管理器_When_Invoke_Then_走后续判定() {
        var context = CreateContext();
        var (_, ctx, nextCalled) = await InvokeAsync(EnabledConfig(), context, manager: null);

        nextCalled.Should().BeTrue();
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public void Given_布尔参数_When_ReadBoolArgument_Then_仅True命中() {
        var arguments = new Dictionary<string, JsonElement> {
            ["flag"] = JsonSerializer.SerializeToElement(true),
            ["off"] = JsonSerializer.SerializeToElement(false),
            ["text"] = JsonSerializer.SerializeToElement("true"),
        };

        SandboxAutoAllowMiddleware.ReadBoolArgument(arguments, "flag").Should().BeTrue();
        SandboxAutoAllowMiddleware.ReadBoolArgument(arguments, "off").Should().BeFalse();
        SandboxAutoAllowMiddleware.ReadBoolArgument(arguments, "text").Should().BeFalse("字符串 \"true\" 不是布尔真");
        SandboxAutoAllowMiddleware.ReadBoolArgument(arguments, "missing").Should().BeFalse();
    }
}
