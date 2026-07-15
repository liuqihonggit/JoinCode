namespace JoinCode.Entry.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// SystemPromptApplyStep 单元测试 — 验证视角1 #5 的 --system-prompt/--append-system-prompt 行为
/// </summary>
public class SystemPromptApplyStepTests
{
    private static StartupContext CreateContext(CommandLineOptions options, IChatService? chatService = null, IChatContextManager? contextManager = null)
    {
        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                ApiKey = "sk-test",
                Provider = "openai",
                ModelId = "gpt-4o"
            }
        };

        var services = new ServiceCollection();
        if (chatService is not null)
            services.AddSingleton(chatService);
        if (contextManager is not null)
            services.AddSingleton(contextManager);
        var provider = services.BuildServiceProvider();

        var hostMock = new Mock<IHost>();
        hostMock.SetupGet(h => h.Services).Returns(provider);
        var host = hostMock.Object;

        return new StartupContext
        {
            Config = config,
            Options = options,
            Host = host,
            FileSystem = new InMemoryFileSystem()
        };
    }

    [Fact]
    public async Task NoSystemPromptAndNoAppend_ShouldCallNextWithoutApplying()
    {
        // Arrange — 默认 CommandLineOptions 无 --system-prompt 也无 --append-system-prompt
        var step = new SystemPromptApplyStep();
        var chatMock = new Mock<IChatService>();
        var contextMgrMock = new Mock<IChatContextManager>();
        var context = CreateContext(new CommandLineOptions(), chatMock.Object, contextMgrMock.Object);
        var nextCalled = false;

        // Act
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 不应用时应直接放行，且不调用任何提示词修改方法
        nextCalled.Should().BeTrue("无参数时应直接调用 next");
        chatMock.Verify(s => s.SetSystemPromptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "无 --system-prompt 时不应调用 SetSystemPromptAsync");
        contextMgrMock.Verify(m => m.AddDynamicSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "无 --append-system-prompt 时不应调用 AddDynamicSystemMessageAsync");
    }

    [Fact]
    public async Task SystemPrompt_ShouldCallSetSystemPromptAsyncAndContinue()
    {
        // Arrange — 指定 --system-prompt
        var step = new SystemPromptApplyStep();
        var chatMock = new Mock<IChatService>();
        chatMock.Setup(s => s.SetSystemPromptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var contextMgrMock = new Mock<IChatContextManager>();

        var options = new CommandLineOptions { SystemPrompt = "你是一个测试助手" };
        var context = CreateContext(options, chatMock.Object, contextMgrMock.Object);

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 应通过 IChatService.SetSystemPromptAsync 替换静态系统提示词
        nextCalled.Should().BeTrue("--system-prompt 应用后应继续调用 next");
        chatMock.Verify(s => s.SetSystemPromptAsync("你是一个测试助手", It.IsAny<CancellationToken>()), Times.Once,
            "--system-prompt 应调用 SetSystemPromptAsync 并传入完整文本");
        contextMgrMock.Verify(m => m.AddDynamicSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "仅 --system-prompt 时不应调用 AddDynamicSystemMessageAsync");
    }

    [Fact]
    public async Task AppendSystemPrompt_ShouldCallAddDynamicSystemMessageAsyncAndContinue()
    {
        // Arrange — 指定 --append-system-prompt
        var step = new SystemPromptApplyStep();
        var chatMock = new Mock<IChatService>();
        var contextMgrMock = new Mock<IChatContextManager>();
        contextMgrMock.Setup(m => m.AddDynamicSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = new CommandLineOptions { AppendSystemPrompt = "额外要求: 使用简洁回复" };
        var context = CreateContext(options, chatMock.Object, contextMgrMock.Object);

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 应通过 IChatContextManager.AddDynamicSystemMessageAsync 追加动态系统消息
        nextCalled.Should().BeTrue("--append-system-prompt 应用后应继续调用 next");
        contextMgrMock.Verify(m => m.AddDynamicSystemMessageAsync("额外要求: 使用简洁回复", It.IsAny<CancellationToken>()), Times.Once,
            "--append-system-prompt 应调用 AddDynamicSystemMessageAsync 并传入完整文本");
        chatMock.Verify(s => s.SetSystemPromptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "仅 --append-system-prompt 时不应调用 SetSystemPromptAsync（不覆盖）");
    }

    [Fact]
    public async Task BothSystemPromptAndAppend_ShouldApplyBothInOrder()
    {
        // Arrange — 同时指定 --system-prompt 和 --append-system-prompt
        // 语义: 先覆盖静态，再追加动态 — 最终前缀 = newStatic + dynamicAppend
        var step = new SystemPromptApplyStep();
        var chatMock = new Mock<IChatService>();
        chatMock.Setup(s => s.SetSystemPromptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var contextMgrMock = new Mock<IChatContextManager>();
        contextMgrMock.Setup(m => m.AddDynamicSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = new CommandLineOptions
        {
            SystemPrompt = "你是一个测试助手",
            AppendSystemPrompt = "额外要求: 使用简洁回复"
        };
        var context = CreateContext(options, chatMock.Object, contextMgrMock.Object);

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 两者都应被调用
        nextCalled.Should().BeTrue("同时指定两个参数时应继续调用 next");
        chatMock.Verify(s => s.SetSystemPromptAsync("你是一个测试助手", It.IsAny<CancellationToken>()), Times.Once,
            "--system-prompt 应被应用");
        contextMgrMock.Verify(m => m.AddDynamicSystemMessageAsync("额外要求: 使用简洁回复", It.IsAny<CancellationToken>()), Times.Once,
            "--append-system-prompt 应被应用");
    }

    [Fact]
    public async Task EmptySystemPrompt_ShouldBeSkipped()
    {
        // Arrange — 空字符串 SystemPrompt 应视为未设置（不应用）
        var step = new SystemPromptApplyStep();
        var chatMock = new Mock<IChatService>();
        var contextMgrMock = new Mock<IChatContextManager>();

        var options = new CommandLineOptions { SystemPrompt = "", AppendSystemPrompt = "" };
        var context = CreateContext(options, chatMock.Object, contextMgrMock.Object);

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 空字符串应跳过应用，直接放行
        nextCalled.Should().BeTrue("空字符串应视为未设置，直接调用 next");
        chatMock.Verify(s => s.SetSystemPromptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "空 SystemPrompt 不应调用 SetSystemPromptAsync");
        contextMgrMock.Verify(m => m.AddDynamicSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "空 AppendSystemPrompt 不应调用 AddDynamicSystemMessageAsync");
    }
}
