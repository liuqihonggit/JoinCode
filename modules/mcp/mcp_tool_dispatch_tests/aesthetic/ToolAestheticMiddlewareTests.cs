namespace McpToolRegistry.Tests;

/// <summary>
/// ToolAestheticMiddleware 单元测试 — 工具执行后分析结果注入情绪反馈
/// </summary>
public class ToolAestheticMiddlewareTests {
    private readonly ITestOutputHelper _output;
    private sealed class FixedLocalTimeProvider(int localHour) : TimeProvider {
        private readonly DateTimeOffset _utcNow = new DateTime(2026, 1, 15, localHour, 0, 0).ToUniversalTime();
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long TimestampFrequency => Stopwatch.Frequency;
    }

    public ToolAestheticMiddlewareTests(ITestOutputHelper output) => _output = output;

    private static ToolExecutionContext CreateContext() {
        return new ToolExecutionContext {
            ToolName = "read",
            Arguments = []
        };
    }

    private static ToolResult CreateTextResult(string text) {
        return new ToolResult {
            Content = [new() { Type = ToolContentType.Text, Text = text }]
        };
    }

    /// <summary>总是返回 Sadness 反馈的分析器 — 用于多分析器测试</summary>
    private sealed class AlwaysSadnessAnalyzer : IAestheticAnalyzer {
        public bool TryAnalyze(ToolExecutionContext context, [MaybeNullWhen(false)] out AestheticFeedback feedback) {
            feedback = new AestheticFeedback {
                Emotion = AestheticEmotion.Sadness,
                Message = "此处令人难过，字段过多",
                Suggestion = "建议拆分"
            };
            return true;
        }
    }

    /// <summary>总是抛异常的分析器 — 用于异常隔离测试</summary>
    private sealed class ThrowingAnalyzer : IAestheticAnalyzer {
        public bool TryAnalyze(ToolExecutionContext context, [MaybeNullWhen(false)] out AestheticFeedback feedback) {
            feedback = default;
            throw new InvalidOperationException("boom");
        }
    }

    [Fact]
    public async Task InvokeAsync_AfterExecution_BugKeywordResult_InjectsEmotionMessage() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        await using var middleware = new ToolAestheticMiddleware([analyzer], NullLogger<ToolAestheticMiddleware>.Instance);
        var context = CreateContext();

        await middleware.InvokeAsync(context, (ctx, _) => {
            ctx.Result = CreateTextResult("// BUG: broken");
            return Task.CompletedTask;
        }, CancellationToken.None);

        var result = context.Result;
        result.Should().NotBeNull();
        if (result is null) return;

        var messages = result.InjectedMessages;
        messages.Should().NotBeNull();
        messages.Should().HaveCount(1);
        if (messages is not null) {
            messages[0].Content.Should().Contain("情绪反馈");
            messages[0].Role.Should().Be(MessageRole.User);
        }
    }

    [Fact]
    public async Task InvokeAsync_AfterExecution_CleanResult_NoInjection() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        await using var middleware = new ToolAestheticMiddleware([analyzer], NullLogger<ToolAestheticMiddleware>.Instance);
        var context = CreateContext();

        await middleware.InvokeAsync(context, (ctx, _) => {
            ctx.Result = CreateTextResult("var x = 42;");
            return Task.CompletedTask;
        }, CancellationToken.None);

        var result = context.Result;
        result.Should().NotBeNull();
        if (result is null) return;
        result.InjectedMessages.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_NullResult_NoInjection() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        await using var middleware = new ToolAestheticMiddleware([analyzer], NullLogger<ToolAestheticMiddleware>.Instance);
        var context = CreateContext();

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_NoAnalyzers_JustCallsNext() {
        await using var middleware = new ToolAestheticMiddleware([], NullLogger<ToolAestheticMiddleware>.Instance);
        var context = CreateContext();

        var nextCalled = false;
        await middleware.InvokeAsync(context, (ctx, _) => {
            nextCalled = true;
            ctx.Result = CreateTextResult("ok");
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_AlwaysCallsNext_First() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        await using var middleware = new ToolAestheticMiddleware([analyzer], NullLogger<ToolAestheticMiddleware>.Instance);
        var context = CreateContext();

        var orderTracker = new List<string>();

        await middleware.InvokeAsync(context, (ctx, _) => {
            orderTracker.Add("next");
            ctx.Result = CreateTextResult("// BUG: broken");
            return Task.CompletedTask;
        }, CancellationToken.None);

        orderTracker.Should().ContainSingle().Which.Should().Be("next");
    }

    [Fact]
    public async Task InvokeAsync_MultipleAnalyzers_InjectsAllFeedbacks() {
        var analyzer1 = new BugKeywordAnalyzer(new CooldownService());
        var analyzer2 = new AlwaysSadnessAnalyzer();

        await using var middleware = new ToolAestheticMiddleware([analyzer1, analyzer2], NullLogger<ToolAestheticMiddleware>.Instance);
        var context = CreateContext();

        await middleware.InvokeAsync(context, (ctx, _) => {
            ctx.Result = CreateTextResult("// BUG: broken");
            return Task.CompletedTask;
        }, CancellationToken.None);

        var result = context.Result;
        result.Should().NotBeNull();
        if (result is null) return;

        var messages = result.InjectedMessages;
        messages.Should().NotBeNull();
        messages.Should().HaveCount(2);
        if (messages is not null) {
            messages[0].Content.Should().Contain("情绪反馈");
            messages[1].Content.Should().Contain("情绪反馈");
        }
    }

    [Fact]
    public async Task InvokeAsync_AnalyzerThrows_SkipsAndContinues() {
        var throwingAnalyzer = new ThrowingAnalyzer();
        var normalAnalyzer = new BugKeywordAnalyzer(new CooldownService());

        await using var middleware = new ToolAestheticMiddleware([throwingAnalyzer, normalAnalyzer], NullLogger<ToolAestheticMiddleware>.Instance);
        var context = CreateContext();

        await middleware.InvokeAsync(context, (ctx, _) => {
            ctx.Result = CreateTextResult("// BUG: broken");
            return Task.CompletedTask;
        }, CancellationToken.None);

        var result = context.Result;
        result.Should().NotBeNull();
        if (result is null) return;

        var messages = result.InjectedMessages;
        messages.Should().NotBeNull();
        messages.Should().HaveCount(1);
        if (messages is not null) {
            messages[0].Content.Should().Contain("BUG");
        }
    }

    [Fact]
    public async Task InvokeAsync_PreservesExistingInjectedMessages() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        await using var middleware = new ToolAestheticMiddleware([analyzer], NullLogger<ToolAestheticMiddleware>.Instance);
        var context = CreateContext();

        var existingMessage = new ApiMessage(MessageRole.User, "existing message");

        await middleware.InvokeAsync(context, (ctx, _) => {
            ctx.Result = CreateTextResult("// BUG: broken") with {
                InjectedMessages = [existingMessage]
            };
            return Task.CompletedTask;
        }, CancellationToken.None);

        var result = context.Result;
        result.Should().NotBeNull();
        if (result is null) return;

        var messages = result.InjectedMessages;
        messages.Should().NotBeNull();
        messages.Should().HaveCount(2);
        if (messages is not null) {
            messages[0].Content.Should().Be("existing message");
            messages[1].Content.Should().Contain("情绪反馈");
        }
    }

    [Fact]
    public async Task InvokeAsync_ErrorResult_StillAnalyzes() {
        var analyzer = new BugKeywordAnalyzer(new CooldownService());
        await using var middleware = new ToolAestheticMiddleware([analyzer], NullLogger<ToolAestheticMiddleware>.Instance);
        var context = CreateContext();

        await middleware.InvokeAsync(context, (ctx, _) => {
            ctx.Result = new ToolResult {
                Content = [new() { Type = ToolContentType.Text, Text = "Error: // FIXME: broken handler" }],
                IsError = true
            };
            return Task.CompletedTask;
        }, CancellationToken.None);

        var result = context.Result;
        result.Should().NotBeNull();
        if (result is null) return;

        var messages = result.InjectedMessages;
        messages.Should().NotBeNull();
        messages.Should().HaveCount(1);
        if (messages is not null) {
            messages[0].Content.Should().Contain("FIXME");
        }
    }

    [Fact]
    public async Task InvokeAsync_AllRealAnalyzers_MultipleEmotionsInjected() {
        var provider = new FixedLocalTimeProvider(2);
        var cooldown = new CooldownService();
        var analyzers = new IAestheticAnalyzer[] {
            new BugKeywordAnalyzer(cooldown),
            new MultiFieldAnalyzer(cooldown),
            new DeadCodeHintAnalyzer(cooldown),
            new SympathyAnalyzer(cooldown),
            new UrgencyAnalyzer(cooldown),
            new LateNightAnalyzer(provider, cooldown)
        };

        await using var middleware = new ToolAestheticMiddleware(analyzers, NullLogger<ToolAestheticMiddleware>.Instance);
        var context = new ToolExecutionContext {
            ToolName = "read",
            Arguments = []
        };

        await middleware.InvokeAsync(context, (ctx, _) => {
            ctx.Result = CreateTextResult("// BUG: FIXME broken — 用户说没有弄好，妈妈患癌住院");
            return Task.CompletedTask;
        }, CancellationToken.None);

        var result = context.Result;
        result.Should().NotBeNull();
        if (result is null) return;

        var messages = result.InjectedMessages;
        messages.Should().NotBeNull();
        messages.Should().HaveCount(4);
        if (messages is null) return;

        foreach (var msg in messages) {
            msg.Content.Should().Contain("情绪反馈");
            _output.WriteLine($"════════════════════════════════════════");
            _output.WriteLine(msg.Content);
            _output.WriteLine($"════════════════════════════════════════");
        }
    }

    [Fact]
    public async Task EdgeCase_NormalCode_NoEmotionInjected() {
        var cooldown = new CooldownService();
        var analyzers = new IAestheticAnalyzer[] {
            new BugKeywordAnalyzer(cooldown),
            new MultiFieldAnalyzer(cooldown),
            new DeadCodeHintAnalyzer(cooldown),
            new SympathyAnalyzer(cooldown),
            new UrgencyAnalyzer(cooldown),
            new LateNightAnalyzer(new FixedLocalTimeProvider(10), cooldown)
        };

        await using var middleware = new ToolAestheticMiddleware(analyzers, NullLogger<ToolAestheticMiddleware>.Instance);
        var context = new ToolExecutionContext { ToolName = "read", Arguments = [] };

        await middleware.InvokeAsync(context, (ctx, _) => {
            ctx.Result = CreateTextResult("var x = 42;\nConsole.WriteLine(x);");
            return Task.CompletedTask;
        }, CancellationToken.None);

        var messages = context.Result?.InjectedMessages;
        messages.Should().BeNullOrEmpty("正常代码不应触发任何情绪");
        _output.WriteLine($"正常代码: 注入 {messages?.Count ?? 0} 条情绪反馈 ✓");
    }

    [Fact]
    public async Task EdgeCase_EmptyResult_NoEmotionInjected() {
        var cooldown = new CooldownService();
        var analyzers = new IAestheticAnalyzer[] { new BugKeywordAnalyzer(cooldown) };

        await using var middleware = new ToolAestheticMiddleware(analyzers, NullLogger<ToolAestheticMiddleware>.Instance);
        var context = new ToolExecutionContext { ToolName = "read", Arguments = [] };

        await middleware.InvokeAsync(context, (ctx, _) => {
            ctx.Result = CreateTextResult("");
            return Task.CompletedTask;
        }, CancellationToken.None);

        var messages = context.Result?.InjectedMessages;
        messages.Should().BeNullOrEmpty("空结果不应触发情绪");
        _output.WriteLine($"空结果: 注入 {messages?.Count ?? 0} 条情绪反馈 ✓");
    }

    [Fact]
    public async Task EdgeCase_NonCodeTool_LateNightNotTriggered() {
        var cooldown = new CooldownService();
        var analyzers = new IAestheticAnalyzer[] { new LateNightAnalyzer(new FixedLocalTimeProvider(2), cooldown) };

        await using var middleware = new ToolAestheticMiddleware(analyzers, NullLogger<ToolAestheticMiddleware>.Instance);
        var context = new ToolExecutionContext { ToolName = "weather", Arguments = [] };

        await middleware.InvokeAsync(context, (ctx, _) => {
            ctx.Result = CreateTextResult("晴 25°C");
            return Task.CompletedTask;
        }, CancellationToken.None);

        var messages = context.Result?.InjectedMessages;
        messages.Should().BeNullOrEmpty("非代码工具深夜不应触发同情");
        _output.WriteLine($"非代码工具(weather)深夜2点: 注入 {messages?.Count ?? 0} 条情绪反馈 ✓");
    }

    [Fact]
    public async Task EdgeCase_Daytime_LateNightNotTriggered() {
        var cooldown = new CooldownService();
        var analyzers = new IAestheticAnalyzer[] { new LateNightAnalyzer(new FixedLocalTimeProvider(14), cooldown) };

        await using var middleware = new ToolAestheticMiddleware(analyzers, NullLogger<ToolAestheticMiddleware>.Instance);
        var context = new ToolExecutionContext { ToolName = "read", Arguments = [] };

        await middleware.InvokeAsync(context, (ctx, _) => {
            ctx.Result = CreateTextResult("var x = 42;");
            return Task.CompletedTask;
        }, CancellationToken.None);

        var messages = context.Result?.InjectedMessages;
        messages.Should().BeNullOrEmpty("白天不应触发深夜同情");
        _output.WriteLine($"白天14点代码工具: 注入 {messages?.Count ?? 0} 条情绪反馈 ✓");
    }

    [Fact]
    public async Task EdgeCase_Cooldown_SecondCallNotTriggered() {
        var cooldown = new CooldownService();
        var analyzers = new IAestheticAnalyzer[] { new BugKeywordAnalyzer(cooldown) };

        await using var middleware = new ToolAestheticMiddleware(analyzers, NullLogger<ToolAestheticMiddleware>.Instance);

        for (var i = 1; i <= 2; i++) {
            var context = new ToolExecutionContext { ToolName = "read", Arguments = [] };
            await middleware.InvokeAsync(context, (ctx, _) => {
                ctx.Result = CreateTextResult("// BUG: broken");
                return Task.CompletedTask;
            }, CancellationToken.None);
            var count = context.Result?.InjectedMessages?.Count ?? 0;
            _output.WriteLine($"第{i}次调用(BUG): 注入 {count} 条情绪反馈");
        }
    }

    [Fact]
    public async Task EdgeCase_LongText_AnalyzersHandleGracefully() {
        var cooldown = new CooldownService();
        var analyzers = new IAestheticAnalyzer[] {
            new BugKeywordAnalyzer(cooldown),
            new SympathyAnalyzer(cooldown),
            new UrgencyAnalyzer(cooldown)
        };

        await using var middleware = new ToolAestheticMiddleware(analyzers, NullLogger<ToolAestheticMiddleware>.Instance);
        var context = new ToolExecutionContext { ToolName = "read", Arguments = [] };

        var longText = new string('a', 10000) + " BUG " + new string('b', 10000);
        await middleware.InvokeAsync(context, (ctx, _) => {
            ctx.Result = CreateTextResult(longText);
            return Task.CompletedTask;
        }, CancellationToken.None);

        var messages = context.Result?.InjectedMessages;
        messages.Should().HaveCount(1, "超长文本中只有BUG应触发");
        _output.WriteLine($"超长文本(20000+字符): 注入 {messages?.Count ?? 0} 条情绪反馈 ✓");
    }
}
