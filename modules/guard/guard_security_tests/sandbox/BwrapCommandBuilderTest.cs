namespace Guard.Security.Tests.Sandbox;

/// <summary>
/// BwrapCommandBuilder 测试 — 逐命令包装必须把 filesystem/network 清单翻成 bwrap 参数，
/// 并保持「deny 后仍允许 allowRead 覆盖」的 schema 语义。
/// </summary>
public class BwrapCommandBuilderTest {
    private static SandboxWrapRequest Request(
        string command = "echo hi",
        string workingDirectory = "/tmp/work",
        List<string>? allowWrite = null,
        List<string>? allowRead = null,
        List<string>? denyWrite = null,
        List<string>? denyRead = null,
        bool restrictNetwork = true,
        List<string>? allowedDomains = null,
        bool allowLocalBinding = false,
        List<string>? allowUnixSockets = null,
        bool allowAllUnixSockets = false,
        int? httpProxyPort = null,
        int? socksProxyPort = null,
        bool weakerNested = false,
        bool weakerNetwork = false,
        Dictionary<string, List<string>>? ignoreViolations = null,
        string? ripgrepCommand = null)
        => new() {
            Command = command,
            WorkingDirectory = workingDirectory,
            AllowWritePaths = allowWrite ?? [],
            AllowReadPaths = allowRead ?? [],
            DenyWritePaths = denyWrite ?? [],
            DenyReadPaths = denyRead ?? [],
            RestrictNetwork = restrictNetwork,
            AllowedDomains = allowedDomains ?? [],
            AllowLocalBinding = allowLocalBinding,
            AllowUnixSockets = allowUnixSockets ?? [],
            AllowAllUnixSockets = allowAllUnixSockets,
            HttpProxyPort = httpProxyPort,
            SocksProxyPort = socksProxyPort,
            EnableWeakerNestedSandbox = weakerNested,
            EnableWeakerNetworkIsolation = weakerNetwork,
            IgnoreViolations = ignoreViolations ?? [],
            RipgrepCommand = ripgrepCommand,
        };

    private static SandboxWrappedCommand Build(SandboxWrapRequest request, params string[] directories)
        => BwrapCommandBuilder.Build(request, _ => true, path => directories.Contains(path, StringComparer.Ordinal));

    [Fact]
    public void Given_普通命令_When_Build_Then_一次性bwrap包装并保留工作目录() {
        var wrapped = Build(Request("echo 'hi'"));

        wrapped.ProviderType.Should().Be(SandboxType.Bubblewrap);
        wrapped.Command.Should().StartWith("'bwrap' ");
        wrapped.Command.Should().Contain("'--unshare-all'");
        wrapped.Command.Should().Contain("'--die-with-parent'");
        wrapped.Command.Should().Contain("'--chdir' '/tmp/work'");
        wrapped.Command.Should().Contain("'--bind' '/tmp/work' '/tmp/work'");
        wrapped.Command.Should().Contain("'--' '/bin/sh' '-c'");
        wrapped.Command.Should().EndWith("'echo '\\''hi'\\'''");
        wrapped.Warning.Should().BeNull();
    }

    [Fact]
    public void Given_允许写入与禁止读取目录_When_Build_Then_bind在前tmpfs掩码在后() {
        var wrapped = Build(
            Request(allowWrite: ["/tmp/out"], denyRead: ["/tmp/secret"]),
            "/tmp/secret");

        wrapped.Command.Should().Contain("'--bind' '/tmp/out' '/tmp/out'");
        wrapped.Command.Should().Contain("'--tmpfs' '/tmp/secret'");
        wrapped.Command.IndexOf("'--bind' '/tmp/out'", StringComparison.Ordinal)
            .Should().BeLessThan(wrapped.Command.IndexOf("'--tmpfs' '/tmp/secret'", StringComparison.Ordinal));
    }

    [Fact]
    public void Given_禁止读取普通文件_When_Build_Then_用devnull覆盖() {
        var wrapped = Build(Request(denyRead: ["/tmp/secret.txt"]));

        wrapped.Command.Should().Contain("'--ro-bind' '/dev/null' '/tmp/secret.txt'");
    }

    [Fact]
    public void Given_allowRead落在denyRead内_When_Build_Then_allowRead最后覆盖() {
        var wrapped = Build(
            Request(allowRead: ["/tmp/secret/ok"], denyRead: ["/tmp/secret"]),
            "/tmp/secret");

        var denyIndex = wrapped.Command.IndexOf("'--tmpfs' '/tmp/secret'", StringComparison.Ordinal);
        var allowIndex = wrapped.Command.LastIndexOf("'--ro-bind' '/tmp/secret/ok' '/tmp/secret/ok'", StringComparison.Ordinal);

        denyIndex.Should().BeGreaterThanOrEqualTo(0);
        allowIndex.Should().BeGreaterThan(denyIndex, "allowRead 语义优先于 denyRead");
    }

    [Fact]
    public void Given_allowWrite路径不存在_When_Build_Then_跳过并告警() {
        var wrapped = BwrapCommandBuilder.Build(Request(allowWrite: ["/tmp/missing"]), _ => false, _ => true);

        wrapped.Command.Should().NotContain("/tmp/missing");
        wrapped.Warning.Should().Contain("allowWrite 路径不存在");
    }

    [Fact]
    public void Given_allowedDomains_When_Build_Then_failclosed断网并告警() {
        var wrapped = Build(Request(allowedDomains: ["example.com"]));

        wrapped.Command.Should().Contain("'--unshare-all'");
        wrapped.Command.Should().NotContain("'--share-net'");
        wrapped.Warning.Should().Contain("allowedDomains 已 fail-closed");
    }

    [Fact]
    public void Given_显式放开网络_When_Build_Then_share_net() {
        var wrapped = Build(Request(restrictNetwork: false));

        wrapped.Command.Should().Contain("'--share-net'");
        wrapped.Warning.Should().BeNull();
    }

    [Fact]
    public void Given_禁止写入目录_When_Build_Then_用tmpfs覆盖() {
        var wrapped = Build(Request(denyWrite: ["/repo/.git"]), "/repo/.git");

        wrapped.Command.Should().Contain("'--tmpfs' '/repo/.git'");
    }

    [Fact]
    public void Given_允许读取目录_When_Build_Then_ro_bind() {
        var wrapped = Build(Request(allowRead: ["/data/input"]));

        wrapped.Command.Should().Contain("'--ro-bind' '/data/input' '/data/input'");
    }

    [Fact]
    public void Given_bwrap不支持的网络安全选项_When_Build_Then_全部显式告警() {
        var wrapped = Build(Request(
            allowLocalBinding: true,
            allowUnixSockets: ["/tmp/agent.sock"],
            allowAllUnixSockets: true,
            httpProxyPort: 8080,
            socksProxyPort: 1080));

        wrapped.Warning.Should().Contain("allowLocalBinding");
        wrapped.Warning.Should().Contain("allowUnixSockets");
        wrapped.Warning.Should().Contain("allowAllUnixSockets");
        wrapped.Warning.Should().Contain("httpProxyPort/socksProxyPort");
    }

    [Fact]
    public void Given_更弱隔离与违规忽略开关_When_Build_Then_显式告警不支持() {
        var wrapped = Build(Request(
            weakerNested: true,
            weakerNetwork: true,
            ignoreViolations: new Dictionary<string, List<string>> { ["/tmp"] = ["read"] },
            ripgrepCommand: "/usr/local/bin/rg"));

        wrapped.Warning.Should().Contain("enableWeakerNestedSandbox");
        wrapped.Warning.Should().Contain("enableWeakerNetworkIsolation");
        wrapped.Warning.Should().Contain("ignoreViolations");
        wrapped.Warning.Should().Contain("sandbox.ripgrep");
    }

    [Fact]
    public void Given_相对allowWrite路径_When_Build_Then_按工作目录展开() {
        var wrapped = Build(Request(workingDirectory: "/tmp/work", allowWrite: ["out"]));

        wrapped.Command.Should().Contain($"'--bind' '{Path.Combine("/tmp/work", "out")}' '{Path.Combine("/tmp/work", "out")}'");
    }
}
