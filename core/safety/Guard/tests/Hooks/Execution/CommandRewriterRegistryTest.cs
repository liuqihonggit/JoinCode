namespace Guard.Tests.Hooks.Execution;

/// <summary>
/// CommandRewriterRegistry 单元测试 — 验证 Rewrite 按优先级执行 / Register 注册 / 默认改写器自动注册
/// </summary>
public sealed class CommandRewriterRegistryTest
{
    // === 默认改写器自动注册 ===

    [Fact]
    public void Constructor_RegistersDefaultRewriters()
    {
        var registry = new CommandRewriterRegistry();

        var rewriters = registry.GetRewriters();
        rewriters.Should().Contain(r => r.Name == "GhPrBodyRewriter");
        rewriters.Should().Contain(r => r.Name == "GhTimeoutRewriter");
        rewriters.Should().Contain(r => r.Name == "VpnRouteRewriter");
    }

    [Fact]
    public void Constructor_RegistersExactlyThreeDefaultRewriters()
    {
        var registry = new CommandRewriterRegistry();

        registry.GetRewriters().Should().HaveCount(3);
    }

    // === Register ===

    [Fact]
    public void Register_AddsCustomRewriter()
    {
        var registry = new CommandRewriterRegistry();
        var custom = new TestRewriter("Custom", 200, _ => true, (c, _) => c + " --custom");

        registry.Register(custom);

        registry.GetRewriters().Should().Contain(custom);
        registry.GetRewriters().Should().HaveCount(4);
    }

    [Fact]
    public void Register_NullRewriter_ThrowsArgumentNullException()
    {
        var registry = new CommandRewriterRegistry();

        var act = () => registry.Register(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Register_MultipleRewriters_AllAdded()
    {
        var registry = new CommandRewriterRegistry();
        var r1 = new TestRewriter("R1", 100, _ => true, (c, _) => c);
        var r2 = new TestRewriter("R2", 200, _ => true, (c, _) => c);

        registry.Register(r1);
        registry.Register(r2);

        registry.GetRewriters().Should().HaveCount(5);
    }

    // === Rewrite — 无匹配 ===

    [Fact]
    public void Rewrite_NoMatchingRewriter_ReturnsUnchanged()
    {
        var registry = new CommandRewriterRegistry();

        var result = registry.Rewrite("echo hello");

        result.WasRewritten.Should().BeFalse();
        result.RewrittenCommand.Should().Be("echo hello");
        result.OriginalCommand.Should().Be("echo hello");
    }

    // === Rewrite — 匹配 ===

    [Fact]
    public void Rewrite_MatchingRewriter_ReturnsRewritten()
    {
        var registry = new CommandRewriterRegistry();
        var custom = new TestRewriter("Custom", 200, c => c.StartsWith("test"), (c, _) => c + " --modified");
        registry.Register(custom);

        var result = registry.Rewrite("test command");

        result.WasRewritten.Should().BeTrue();
        result.RewrittenCommand.Should().Be("test command --modified");
        result.RewriterName.Should().Be("Custom");
        result.Reason.Should().Contain("Custom");
    }

    // === Rewrite — 优先级 ===

    [Fact]
    public void Rewrite_MultipleMatching_HigherPriorityWins()
    {
        var registry = new CommandRewriterRegistry();
        var low = new TestRewriter("Low", 10, _ => true, (c, _) => c + " --low");
        var high = new TestRewriter("High", 100, _ => true, (c, _) => c + " --high");
        registry.Register(low);
        registry.Register(high);

        var result = registry.Rewrite("cmd");

        result.WasRewritten.Should().BeTrue();
        result.RewriterName.Should().Be("High");
        result.RewrittenCommand.Should().Be("cmd --high");
    }

    [Fact]
    public void Rewrite_RewriterReturnsSameCommand_SkipsToNextRewriter()
    {
        var registry = new CommandRewriterRegistry();
        var noOp = new TestRewriter("NoOp", 100, _ => true, (c, _) => c);
        var actual = new TestRewriter("Actual", 50, _ => true, (c, _) => c + " --actual");
        registry.Register(noOp);
        registry.Register(actual);

        var result = registry.Rewrite("cmd");

        result.WasRewritten.Should().BeTrue();
        result.RewriterName.Should().Be("Actual");
    }

    // === Rewrite — 参数校验 ===

    [Fact]
    public void Rewrite_EmptyCommand_ThrowsArgumentException()
    {
        var registry = new CommandRewriterRegistry();

        var act = () => registry.Rewrite("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rewrite_WhitespaceCommand_ThrowsArgumentException()
    {
        var registry = new CommandRewriterRegistry();

        var act = () => registry.Rewrite("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rewrite_NullCommand_ThrowsArgumentException()
    {
        var registry = new CommandRewriterRegistry();

        var act = () => registry.Rewrite(null!);

        act.Should().Throw<ArgumentException>();
    }

    // === Rewrite — 默认改写器集成 ===

    [Fact]
    public void Rewrite_GhPrCreate_AutoAddsBody()
    {
        var registry = new CommandRewriterRegistry();

        var result = registry.Rewrite("gh pr create --title foo");

        result.WasRewritten.Should().BeTrue();
        result.RewrittenCommand.Should().Contain("--body");
        result.RewriterName.Should().Be("GhPrBodyRewriter");
    }

    [Fact]
    public void Rewrite_GhPrCreate_WithExistingBody_NotRewrittenByGhPrBody()
    {
        var registry = new CommandRewriterRegistry();

        var result = registry.Rewrite("gh pr create --title foo --body existing");

        result.WasRewritten.Should().BeFalse();
    }

    // === Rewrite — context 传递 ===

    [Fact]
    public void Rewrite_PassesContextToRewriter()
    {
        var registry = new CommandRewriterRegistry();
        IReadOnlyDictionary<string, object>? capturedContext = null;
        var custom = new TestRewriter("Custom", 200, _ => true, (c, ctx) =>
        {
            capturedContext = ctx;
            return c;
        });
        registry.Register(custom);

        var context = new Dictionary<string, object> { ["key"] = "value" };
        registry.Rewrite("cmd", context);

        capturedContext.Should().BeSameAs(context);
    }

    [Fact]
    public void Rewrite_NullContext_UsesEmptyContext()
    {
        var registry = new CommandRewriterRegistry();
        IReadOnlyDictionary<string, object>? capturedContext = null;
        var custom = new TestRewriter("Custom", 200, _ => true, (c, ctx) =>
        {
            capturedContext = ctx;
            return c + " --ok";
        });
        registry.Register(custom);

        registry.Rewrite("cmd", null);

        capturedContext.Should().NotBeNull();
        capturedContext.Should().BeEmpty();
    }

    /// <summary>
    /// 测试用改写器桩 — 可配置 CanRewrite / Rewrite / Priority / Name
    /// </summary>
    private sealed class TestRewriter : ICommandRewriter
    {
        private readonly Func<string, bool> _canRewrite;
        private readonly Func<string, IReadOnlyDictionary<string, object>, string> _rewrite;

        public TestRewriter(
            string name,
            int priority,
            Func<string, bool> canRewrite,
            Func<string, IReadOnlyDictionary<string, object>, string> rewrite)
        {
            Name = name;
            Priority = priority;
            _canRewrite = canRewrite;
            _rewrite = rewrite;
        }

        public string Name { get; }
        public int Priority { get; }
        public bool CanRewrite(string command) => _canRewrite(command);
        public string Rewrite(string command, IReadOnlyDictionary<string, object> context) => _rewrite(command, context);
    }
}
