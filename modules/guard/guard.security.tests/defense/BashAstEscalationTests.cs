namespace Guard.Security.Tests;

/// <summary>
/// AST 升级器的接线测试 — 「分类器判 Safe、但命令被构造过」时把需要确认的信号补上。
/// <para>
/// 这套校验器源自「这条命令能否免确认自动执行」的口径，对**正常创作行为**（写 heredoc 脚本、
/// 写 <c>2&gt;&amp;1</c>、写 <c>if</c>、<c>source</c>、<c>echo a\ b</c>）也会报警。
/// 所以本类锁两个方向：
/// ① 构造/混淆类检查必须升级；
/// ② 实测会误报的检查项**不能**升级 —— 否则用户每次写脚本都要多点一次确认，防护会被当成噪音关掉。
/// </para>
/// </summary>
public class BashAstEscalationTests {
    private readonly ITestOutputHelper _output;

    /// <summary>构造并注入测试输出</summary>
    public BashAstEscalationTests(ITestOutputHelper output) {
        _output = output;
    }

    /// <summary>构造/混淆写法必须升级为「需要确认」。</summary>
    [Theory]
    [InlineData("jq -n 'system(\"id\")'")]
    public async Task ConstructedCommand_IsEscalated(string command) {
        using var walker = new BashAstSecurityWalker();
        await using var escalator = new BashAstRiskEscalator(new BashSecurityValidator(walker));

        escalator.TryEscalate(command, out var result).Should().BeTrue($"'{command}' 被构造过，静态判定不可信");
        result.Should().NotBeNull();
        if (result is null) {
            return;
        }
        result.RequiresIntervention.Should().BeTrue("升级后的结论必须触发拦截");
    }

    /// <summary>
    /// 实测在良性命令上就会触发的检查项 —— 不能升级，否则日常操作全是确认弹窗。
    /// <para>每条都注明它命中的检查项，便于以后有人想收紧时先看代价。</para>
    /// </summary>
    [Theory]
    // Newlines — 写脚本
    [InlineData("cat > /tmp/x.sh <<EOF\nrm -rf ./build\nEOF")]
    // MalformedTokenInjection — 正常 shell 语法
    [InlineData("if [ -f x ]; then echo y; fi")]
    [InlineData("[ -d x ] && echo dir")]
    // OutputRedirection — 合并 stderr
    [InlineData("npm run build 2>&1 | tail -20")]
    [InlineData("exec 3>&1")]
    // DangerousVariables — source 环境脚本
    [InlineData("source ~/.bashrc")]
    // BackslashEscapedWhitespace — 转义空格
    [InlineData("echo a\\ b")]
    // ObfuscatedFlags — 设置 IFS
    [InlineData("IFS=$'\\n'")]
    // BraceExpansion — 花括号展开
    [InlineData("echo {a,b}")]
    public async Task AuthoringPattern_IsNotEscalated(string command) {
        using var walker = new BashAstSecurityWalker();
        await using var escalator = new BashAstRiskEscalator(new BashSecurityValidator(walker));

        var escalated = escalator.TryEscalate(command, out var result);
        _output.WriteLine($"{command.Replace("\n", "\\n", StringComparison.Ordinal),-44} -> escalated={escalated}");

        escalated.Should().BeFalse($"'{command}' 是正常写法，升级会让每次写脚本/写 if 都多一次确认");
        result.Should().BeNull();
    }

    /// <summary>普通良性命令不受影响。</summary>
    [Theory]
    [InlineData("ls -la")]
    [InlineData("git status")]
    [InlineData("dotnet build modules/x.csproj --no-restore")]
    [InlineData("grep -rn \"TODO\" --include=*.cs modules/")]
    [InlineData("jq -r '.items[] | .name' x.json")]
    [InlineData("sh -c 'echo hi'")]
    public async Task BenignCommand_IsNotEscalated(string command) {
        using var walker = new BashAstSecurityWalker();
        await using var escalator = new BashAstRiskEscalator(new BashSecurityValidator(walker));

        escalator.TryEscalate(command, out _).Should().BeFalse($"'{command}' 是良性命令");
    }

    /// <summary>排障开关：设 <c>JCC_BASH_AST_ESCALATE=0</c> 时整层失效。</summary>
    [Fact]
    public async Task DisabledByEnvironmentVariable() {
        var previous = Environment.GetEnvironmentVariable(BashAstRiskEscalator.DisableEnvironmentVariable);
        try {
            Environment.SetEnvironmentVariable(BashAstRiskEscalator.DisableEnvironmentVariable, "0");
            using var walker = new BashAstSecurityWalker();
            await using var escalator = new BashAstRiskEscalator(new BashSecurityValidator(walker));
            escalator.TryEscalate("jq -n 'system(\"id\")'", out var result).Should().BeFalse();
            result.Should().BeNull();
        } finally {
            Environment.SetEnvironmentVariable(BashAstRiskEscalator.DisableEnvironmentVariable, previous);
        }
    }

    /// <summary>
    /// 中间件层：分类器判 Safe、AST 认为被构造过的命令，Auto 模式必须被拒 ——
    /// 这是「接线」真正生效的位置。
    /// </summary>
    [Fact]
    public async Task Middleware_RejectsConstructedCommand_InAutoMode() {
        using var walker = new BashAstSecurityWalker();
        await using var escalator = new BashAstRiskEscalator(new BashSecurityValidator(walker));
        await using var middleware = new DangerousCommandProtectionMiddleware(
            dangerClassifier: new CommandDangerClassifier(),
            astEscalator: escalator);

        var context = CreateContext(PermissionMode.Auto, "jq -n 'system(\"id\")'");
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeFalse("被构造过的命令不得因为字面良性而放行");
        context.Result.Should().NotBeNull();
        if (context.Result is null) {
            return;
        }
        context.Result.IsApproved.Should().BeFalse();
    }

    /// <summary>中间件层：正常写法不受 AST 层影响，Auto 模式照常放行。</summary>
    [Theory]
    [InlineData("ls -la")]
    [InlineData("cat > /tmp/x.sh <<EOF\nrm -rf ./build\nEOF")]
    public async Task Middleware_AllowsAuthoringPatterns_InAutoMode(string command) {
        using var walker = new BashAstSecurityWalker();
        await using var escalator = new BashAstRiskEscalator(new BashSecurityValidator(walker));
        await using var middleware = new DangerousCommandProtectionMiddleware(
            dangerClassifier: new CommandDangerClassifier(),
            astEscalator: escalator);

        var context = CreateContext(PermissionMode.Auto, command);
        var nextCalled = false;

        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue($"正常写法不该被 AST 层拦下：'{command}'");
    }

    private static PermissionCheckContext CreateContext(PermissionMode mode, string command) {
        var args = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement(command, HooksJsonContext.Default.String)
        };
        return new PermissionCheckContext {
            ToolName = "bash",
            Arguments = args,
            CurrentMode = mode,
            Config = PermissionConfig.CreateDefault(),
            AutoApprovedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AutoRejectedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ApprovedLevels = []
        };
    }
}
