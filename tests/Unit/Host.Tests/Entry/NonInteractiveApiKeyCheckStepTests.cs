namespace JoinCode.Entry.Tests;

using Microsoft.Extensions.Hosting;

/// <summary>
/// NonInteractiveApiKeyCheckStep 单元测试
/// 验证 R-P2-002 修复:无 API Key 时非交互模式应直接退出而非警告后继续
/// </summary>
public class NonInteractiveApiKeyCheckStepTests
{
    private static StartupContext CreateContext(string apiKey)
    {
        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                ApiKey = apiKey,
                Provider = "openai",
                ModelId = "gpt-4o"
            }
        };

        var host = new Mock<IHost>().Object;
        var fs = new InMemoryFileSystem();

        return new StartupContext
        {
            Config = config,
            Options = new CommandLineOptions(),
            Host = host,
            FileSystem = fs
        };
    }

    [Fact]
    public async Task EmptyApiKey_ShouldSetNonZeroExitCodeAndNotCallNext()
    {
        // Arrange — 无 API Key 是 R-P2-002 修复目标场景
        var step = new NonInteractiveApiKeyCheckStep();
        var context = CreateContext(apiKey: string.Empty);
        var nextCalled = false;

        // Act
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 无 API Key 时不应继续执行管道(否则后续 LLM 调用必失败)
        nextCalled.Should().BeFalse("无 API Key 时不应继续执行管道(避免无意义的 401 调用)");
        context.ExitCode.Should().NotBe(0, "无 API Key 时应设置非 0 退出码,提示用户配置 API Key");
    }

    [Fact]
    public async Task ValidApiKey_ShouldCallNextAndKeepExitCodeZero()
    {
        // Arrange — 有 API Key 是正常场景
        var step = new NonInteractiveApiKeyCheckStep();
        var context = CreateContext(apiKey: "sk-test-key-12345");
        var nextCalled = false;

        // Act
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 有 API Key 时应继续执行管道
        nextCalled.Should().BeTrue("有 API Key 时应继续执行管道");
        context.ExitCode.Should().Be(0, "有 API Key 时 ExitCode 应保持 0");
    }
}
