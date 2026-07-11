
namespace MockServer.E2E.Tests.Hooks;

/// <summary>
/// 钩子事件捕获测试
/// 验证 Hooks 是否能在运行时捕获 LLM 输出和控制台输出
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(PipeTestCollection))]
public sealed class HookEventCaptureTests : OpenAIMockTestBase
{
    private readonly List<HookExecutionEvent> _capturedEvents = new();
    private readonly IHookEventBroadcaster _broadcaster;

    public HookEventCaptureTests(PipeMockServerFixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        _broadcaster = new HookEventBroadcaster();
        _broadcaster.SetAllEventsEnabled(true);
    }

    /// <summary>
    /// 测试捕获 LLM 输出中的关键字
    /// </summary>
    [Fact]
    public async Task Should_Capture_LlmOutput_With_Keywords()
    {
        // Arrange
        var keywords = new[] { "error", "exception", "failed" };
        var capturedOutputs = new List<string>();

        _broadcaster.RegisterHandler(evt =>
        {
            if (evt is HookResponseEvent responseEvent)
            {
                var output = responseEvent.Output ?? responseEvent.Stdout ?? "";
                if (keywords.Any(k => output.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    capturedOutputs.Add(output);
                }
            }
        });

        // Act - 模拟 LLM 输出包含关键字
        _broadcaster.BroadcastResponse(new BroadcastContext
        {
            HookId = "test-hook-1",
            HookName = "LLMResponse",
            HookEvent = HookEvent.PostToolUse,
            Output = "An error occurred while processing your request.",
            Stdout = "An error occurred while processing your request.",
            Stderr = null,
            ExitCode = 0,
            Outcome = HookExecutionOutcome.Success,
            Duration = TimeSpan.FromSeconds(1)
        });

        // Assert
        capturedOutputs.Should().ContainSingle();
        capturedOutputs[0].Should().Contain("error");
    }

    /// <summary>
    /// 测试捕获控制台输出
    /// </summary>
    [Fact]
    public async Task Should_Capture_ConsoleOutput()
    {
        // Arrange
        var consoleOutputs = new List<string>();

        _broadcaster.RegisterHandler(evt =>
        {
            if (evt is HookProgressEvent progressEvent)
            {
                var output = progressEvent.Stdout ?? "";
                consoleOutputs.Add(output);
            }
        });

        // Act - 模拟控制台输出
        _broadcaster.BroadcastProgress(
            hookId: "test-hook-2",
            hookName: "ConsoleOutput",
            hookEvent: HookEvent.SessionStart,
            stdout: "Processing command...",
            stderr: null);

        // Assert
        consoleOutputs.Should().ContainSingle();
        consoleOutputs[0].Should().Be("Processing command...");
    }

    /// <summary>
    /// 测试使用正则表达式捕获特定模式
    /// </summary>
    [Fact]
    public async Task Should_Capture_Output_Using_Regex_Pattern()
    {
        // Arrange
        var pattern = new Regex(@"\[ERROR\]\s*(.+)", RegexOptions.IgnoreCase);
        var errors = new List<string>();

        _broadcaster.RegisterHandler(evt =>
        {
            if (evt is HookResponseEvent responseEvent)
            {
                var output = responseEvent.Output ?? "";
                var match = pattern.Match(output);
                if (match.Success)
                {
                    errors.Add(match.Groups[1].Value);
                }
            }
        });

        // Act
        _broadcaster.BroadcastResponse(new BroadcastContext
        {
            HookId = "test-hook-3",
            HookName = "ErrorLogger",
            HookEvent = HookEvent.PostToolUseFailure,
            Output = "[ERROR] Connection timeout occurred",
            Stdout = "[ERROR] Connection timeout occurred",
            Stderr = null,
            ExitCode = 1,
            Outcome = HookExecutionOutcome.Error,
            Duration = TimeSpan.FromSeconds(2)
        });

        // Assert
        errors.Should().ContainSingle();
        errors[0].Should().Be("Connection timeout occurred");
    }

    /// <summary>
    /// 测试捕获系统提示词相关事件
    /// </summary>
    [Fact]
    public async Task Should_Capture_SystemPrompt_Events()
    {
        // Arrange
        var systemPromptEvents = new List<HookExecutionEvent>();

        _broadcaster.RegisterHandler(evt =>
        {
            if (evt.HookEvent == HookEvent.Setup)
            {
                systemPromptEvents.Add(evt);
            }
        });

        // Act
        _broadcaster.BroadcastStarted(
            hookId: "test-hook-4",
            hookName: "SystemPromptLoader",
            hookEvent: HookEvent.Setup);

        // Assert
        systemPromptEvents.Should().ContainSingle();
        systemPromptEvents[0].Should().BeOfType<HookStartedEvent>();
    }

    /// <summary>
    /// 测试捕获用户提示词提交事件
    /// </summary>
    [Fact]
    public async Task Should_Capture_UserPromptSubmit_Events()
    {
        // Arrange
        var userPrompts = new List<string>();

        _broadcaster.RegisterHandler(evt =>
        {
            if (evt is HookResponseEvent responseEvent &&
                responseEvent.HookEvent == HookEvent.UserPromptSubmit)
            {
                userPrompts.Add(responseEvent.Output ?? "");
            }
        });

        // Act
        _broadcaster.BroadcastResponse(new BroadcastContext
        {
            HookId = "test-hook-5",
            HookName = "UserPromptHandler",
            HookEvent = HookEvent.UserPromptSubmit,
            Output = "Hello, how are you?",
            Stdout = "Hello, how are you?",
            Stderr = null,
            ExitCode = 0,
            Outcome = HookExecutionOutcome.Success,
            Duration = TimeSpan.FromMilliseconds(100)
        });

        // Assert
        userPrompts.Should().ContainSingle();
        userPrompts[0].Should().Be("Hello, how are you?");
    }

    /// <summary>
    /// 测试多个处理器同时接收事件
    /// </summary>
    [Fact]
    public async Task Should_Support_Multiple_Handlers()
    {
        // Arrange
        var handler1Calls = 0;
        var handler2Calls = 0;

        _broadcaster.RegisterHandler(_ => handler1Calls++);
        _broadcaster.RegisterHandler(_ => handler2Calls++);

        // Act
        _broadcaster.BroadcastStarted(
            hookId: "test-hook-6",
            hookName: "MultiHandlerTest",
            hookEvent: HookEvent.SessionStart);

        // Assert
        handler1Calls.Should().Be(1);
        handler2Calls.Should().Be(1);
    }

    /// <summary>
    /// 测试事件处理器异常不会中断其他处理器
    /// </summary>
    [Fact]
    public async Task Should_Continue_When_Handler_Throws()
    {
        // Arrange
        var successfulHandlerCalls = 0;

        _broadcaster.RegisterHandler(_ => throw new Exception("Test exception"));
        _broadcaster.RegisterHandler(_ => successfulHandlerCalls++);

        // Act
        _broadcaster.BroadcastStarted(
            hookId: "test-hook-7",
            hookName: "ExceptionTest",
            hookEvent: HookEvent.SessionStart);

        // Assert - 第二个处理器应该仍然被调用
        successfulHandlerCalls.Should().Be(1);
    }

    /// <summary>
    /// 测试进度报告功能
    /// </summary>
    [Fact]
    public async Task Should_Report_Progress_Periodically()
    {
        // Arrange
        var progressEvents = new List<HookProgressEvent>();

        _broadcaster.RegisterHandler(evt =>
        {
            if (evt is HookProgressEvent progressEvent)
            {
                progressEvents.Add(progressEvent);
            }
        });

        // Act - 模拟多次进度更新
        for (int i = 0; i < 3; i++)
        {
            _broadcaster.BroadcastProgress(
                hookId: "test-hook-8",
                hookName: "ProgressTest",
                hookEvent: HookEvent.PostToolUse,
                stdout: $"Progress: {i + 1}/3",
                stderr: null);
        }

        // Assert
        progressEvents.Should().HaveCount(3);
        progressEvents[0].Stdout.Should().Be("Progress: 1/3");
        progressEvents[2].Stdout.Should().Be("Progress: 3/3");
    }
}
