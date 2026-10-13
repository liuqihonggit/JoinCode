namespace McpToolRegistry.Tests;

/// <summary>
/// ToolAestheticMiddleware 单元测试 — 工具执行后分析结果注入情绪反馈
/// </summary>
public class ToolAestheticMiddlewareTests {
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
        var analyzer = new BugKeywordAnalyzer();
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
        var analyzer = new BugKeywordAnalyzer();
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
        var analyzer = new BugKeywordAnalyzer();
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
        var analyzer = new BugKeywordAnalyzer();
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
        var analyzer1 = new BugKeywordAnalyzer();
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
        var normalAnalyzer = new BugKeywordAnalyzer();

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
        var analyzer = new BugKeywordAnalyzer();
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
        var analyzer = new BugKeywordAnalyzer();
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
}
