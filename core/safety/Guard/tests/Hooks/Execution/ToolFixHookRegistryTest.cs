namespace Guard.Tests.Hooks.Execution;

/// <summary>
/// ToolFixHookRegistry 单元测试 — 验证 TryFixAsync 阈值触发 / Register 注册 / 优先级执行
/// </summary>
public sealed class ToolFixHookRegistryTest
{
    private readonly Mock<IToolHealthMonitor> _healthMonitor = new();

    // === 构造 ===

    [Fact]
    public void Constructor_NullHealthMonitor_ThrowsArgumentNullException()
    {
        var act = () => new ToolFixHookRegistry(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Constructor_DefaultThreshold_Is3()
    {
        _healthMonitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 3 });

        var registry = new ToolFixHookRegistry(_healthMonitor.Object);
        var hook = new TestFixHook("Test", 100, (_, _) => true, (_, _, _) => Task.FromResult(new ToolFixResult { Success = true, Description = "fixed" }));
        registry.Register(hook);

        // 阈值默认3，ConsecutiveFailures=3 应触发
        var result = await registry.TryFixAsync("tool1", new Exception("err"));

        result.Success.Should().BeTrue();
    }

    // === Register ===

    [Fact]
    public void Register_AddsHook()
    {
        var registry = new ToolFixHookRegistry(_healthMonitor.Object);
        var hook = new TestFixHook("Test", 100, (_, _) => true, (_, _, _) => Task.FromResult(new ToolFixResult { Success = true }));

        registry.Register(hook);

        registry.GetHooks().Should().Contain(hook);
    }

    [Fact]
    public void Register_NullHook_ThrowsArgumentNullException()
    {
        var registry = new ToolFixHookRegistry(_healthMonitor.Object);

        var act = () => registry.Register(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Register_MultipleHooks_AllAdded()
    {
        var registry = new ToolFixHookRegistry(_healthMonitor.Object);
        var h1 = new TestFixHook("H1", 100, (_, _) => true, (_, _, _) => Task.FromResult(new ToolFixResult { Success = true }));
        var h2 = new TestFixHook("H2", 200, (_, _) => true, (_, _, _) => Task.FromResult(new ToolFixResult { Success = true }));

        registry.Register(h1);
        registry.Register(h2);

        registry.GetHooks().Should().HaveCount(2);
    }

    // === TryFixAsync — 阈值 ===

    [Fact]
    public async Task TryFixAsync_BelowThreshold_ReturnsFailureWithoutCallingHooks()
    {
        _healthMonitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ToolHealthRecord?)null);
        var registry = new ToolFixHookRegistry(_healthMonitor.Object, threshold: 3);
        var hook = new TestFixHook("Test", 100, (_, _) => true, (_, _, _) => Task.FromResult(new ToolFixResult { Success = true }));
        registry.Register(hook);

        var result = await registry.TryFixAsync("tool1", new Exception("err"));

        result.Success.Should().BeFalse();
        result.Description.Should().Be("错误次数未达阈值");
        hook.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task TryFixAsync_AtThreshold_TriggersHook()
    {
        _healthMonitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 3 });
        var registry = new ToolFixHookRegistry(_healthMonitor.Object, threshold: 3);
        var hook = new TestFixHook("Test", 100, (_, _) => true, (_, _, _) => Task.FromResult(new ToolFixResult { Success = true, Description = "fixed" }));
        registry.Register(hook);

        var result = await registry.TryFixAsync("tool1", new Exception("err"));

        result.Success.Should().BeTrue();
        result.Description.Should().Be("fixed");
        hook.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryFixAsync_AboveThreshold_TriggersHook()
    {
        _healthMonitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 10 });
        var registry = new ToolFixHookRegistry(_healthMonitor.Object, threshold: 3);
        var hook = new TestFixHook("Test", 100, (_, _) => true, (_, _, _) => Task.FromResult(new ToolFixResult { Success = true }));
        registry.Register(hook);

        var result = await registry.TryFixAsync("tool1", new Exception("err"));

        result.Success.Should().BeTrue();
    }

    // === TryFixAsync — 无匹配 ===

    [Fact]
    public async Task TryFixAsync_NoMatchingHook_ReturnsFailure()
    {
        _healthMonitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 5 });
        var registry = new ToolFixHookRegistry(_healthMonitor.Object, threshold: 3);
        var hook = new TestFixHook("Test", 100, (_, _) => false, (_, _, _) => Task.FromResult(new ToolFixResult { Success = true }));
        registry.Register(hook);

        var result = await registry.TryFixAsync("tool1", new Exception("err"));

        result.Success.Should().BeFalse();
        result.Description.Should().Be("无匹配的修正器");
    }

    [Fact]
    public async Task TryFixAsync_NoHooksRegistered_ReturnsNoMatcherFailure()
    {
        _healthMonitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 5 });
        var registry = new ToolFixHookRegistry(_healthMonitor.Object, threshold: 3);

        var result = await registry.TryFixAsync("tool1", new Exception("err"));

        result.Success.Should().BeFalse();
        result.Description.Should().Be("无匹配的修正器");
    }

    // === TryFixAsync — 优先级 ===

    [Fact]
    public async Task TryFixAsync_HigherPriorityHookTriedFirst()
    {
        _healthMonitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 5 });
        var registry = new ToolFixHookRegistry(_healthMonitor.Object, threshold: 3);
        var low = new TestFixHook("Low", 10, (_, _) => true, (_, _, _) => Task.FromResult(new ToolFixResult { Success = true, Description = "low" }));
        var high = new TestFixHook("High", 100, (_, _) => true, (_, _, _) => Task.FromResult(new ToolFixResult { Success = true, Description = "high" }));
        registry.Register(low);
        registry.Register(high);

        var result = await registry.TryFixAsync("tool1", new Exception("err"));

        result.Description.Should().Be("high");
        low.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task TryFixAsync_FirstHookFails_TriesNextHook()
    {
        _healthMonitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 5 });
        var registry = new ToolFixHookRegistry(_healthMonitor.Object, threshold: 3);
        var high = new TestFixHook("High", 100, (_, _) => true, (_, _, _) => Task.FromResult(new ToolFixResult { Success = false, Description = "high failed" }));
        var low = new TestFixHook("Low", 50, (_, _) => true, (_, _, _) => Task.FromResult(new ToolFixResult { Success = true, Description = "low fixed" }));
        registry.Register(high);
        registry.Register(low);

        var result = await registry.TryFixAsync("tool1", new Exception("err"));

        result.Success.Should().BeTrue();
        result.Description.Should().Be("low fixed");
    }

    [Fact]
    public async Task TryFixAsync_HookThrowsException_SkipsToNextHook()
    {
        _healthMonitor.Setup(x => x.GetRecordAsync("tool1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolHealthRecord { ToolName = "tool1", ConsecutiveFailures = 5 });
        var registry = new ToolFixHookRegistry(_healthMonitor.Object, threshold: 3);
        var throwing = new TestFixHook("Throwing", 100, (_, _) => true, (_, _, _) => throw new InvalidOperationException("boom"));
        var fallback = new TestFixHook("Fallback", 50, (_, _) => true, (_, _, _) => Task.FromResult(new ToolFixResult { Success = true, Description = "fallback fixed" }));
        registry.Register(throwing);
        registry.Register(fallback);

        var result = await registry.TryFixAsync("tool1", new Exception("err"));

        result.Success.Should().BeTrue();
        result.Description.Should().Be("fallback fixed");
    }

    // === TryFixAsync — 参数校验 ===

    [Fact]
    public async Task TryFixAsync_EmptyToolName_ThrowsArgumentException()
    {
        var registry = new ToolFixHookRegistry(_healthMonitor.Object);

        var act = async () => await registry.TryFixAsync("", new Exception("err"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TryFixAsync_WhitespaceToolName_ThrowsArgumentException()
    {
        var registry = new ToolFixHookRegistry(_healthMonitor.Object);

        var act = async () => await registry.TryFixAsync("  ", new Exception("err"));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TryFixAsync_NullError_ThrowsArgumentNullException()
    {
        var registry = new ToolFixHookRegistry(_healthMonitor.Object);

        var act = async () => await registry.TryFixAsync("tool1", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// 测试用修正器桩 — 可配置 CanFix / FixAsync / Priority / Name，并记录调用次数
    /// </summary>
    private sealed class TestFixHook : IToolFixHook
    {
        private readonly Func<string, Exception, bool> _canFix;
        private readonly Func<string, Exception, CancellationToken, Task<ToolFixResult>> _fix;

        public TestFixHook(
            string name,
            int priority,
            Func<string, Exception, bool> canFix,
            Func<string, Exception, CancellationToken, Task<ToolFixResult>> fix)
        {
            Name = name;
            Priority = priority;
            _canFix = canFix;
            _fix = fix;
            CallCount = 0;
        }

        public string Name { get; }
        public int Priority { get; }
        public int CallCount { get; private set; }

        public bool CanFix(string toolName, Exception error) => _canFix(toolName, error);

        public Task<ToolFixResult> FixAsync(string toolName, Exception error, CancellationToken ct = default)
        {
            CallCount++;
            return _fix(toolName, error, ct);
        }
    }
}
