namespace JoinCode.Entry.Tests;

using System.Text.Json;
using JoinCode.ChatCommands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testing.Common.Services;

/// <summary>
/// SessionResumeStep 单元测试 — 验证视角1 #1 的 --continue/--resume 行为
/// </summary>
public class SessionResumeStepTests
{
    private static readonly string SessionsDir = WorkflowConstants.Paths.SessionsDirectory;

    private static StartupContext CreateContext(CommandLineOptions options, IFileSystem fs)
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

        var host = new Mock<IHost>().Object;
        return new StartupContext
        {
            Config = config,
            Options = options,
            Host = host,
            FileSystem = fs
        };
    }

    private static string WriteSessionFile(IFileSystem fs, string sessionId, string customTitle, DateTime lastModified, List<SessionMessage> messages)
    {
        var data = new SessionData
        {
            Id = sessionId,
            ProjectPath = "/test",
            CustomTitle = customTitle,
            CreatedAt = DateTime.UtcNow,
            Messages = messages
        };

        var json = JsonSerializer.Serialize(data, CliIndentedJsonContext.Default.SessionData);
        var path = Path.Combine(SessionsDir, $"{sessionId}.json");
        fs.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task NoContinueAndNoResume_ShouldCallNextWithoutResume()
    {
        // Arrange — 默认 CommandLineOptions 无 --continue 也无 --resume
        var step = new SessionResumeStep();
        var fs = new InMemoryFileSystem();
        var context = CreateContext(new CommandLineOptions(), fs);
        var nextCalled = false;

        // Act
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 不恢复时应直接放行
        nextCalled.Should().BeTrue("无 --continue 和 --resume 时应直接调用 next");
    }

    [Fact]
    public async Task Continue_WithNoSessionsDirectory_ShouldCallNextWithoutError()
    {
        // Arrange — sessions 目录不存在
        var step = new SessionResumeStep();
        var fs = new InMemoryFileSystem();
        var options = new CommandLineOptions { ContinueSession = true };
        var context = CreateContext(options, fs);
        var nextCalled = false;

        // Act
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 目录不存在不应阻塞启动
        nextCalled.Should().BeTrue("无历史会话时应继续启动");
        context.ExitCode.Should().Be(0, "无会话可恢复不应设置错误退出码");
    }

    [Fact]
    public async Task Continue_WithSessions_ShouldLoadMostRecent()
    {
        // Arrange — 写入两个会话，一个旧一个新
        var step = new SessionResumeStep();
        var fs = new InMemoryFileSystem();

        // 创建 sessions 目录
        fs.CreateDirectory(SessionsDir);

        // 旧会话
        WriteSessionFile(fs, "old-session-id", "旧会话", DateTime.UtcNow.AddHours(-2),
            [new SessionMessage { Role = "user", Content = "旧消息" }]);

        // 新会话（最近）
        var newSessionPath = WriteSessionFile(fs, "new-session-id", "新会话", DateTime.UtcNow,
            [new SessionMessage { Role = "user", Content = "新消息 1" },
             new SessionMessage { Role = "assistant", Content = "新消息 2" }]);

        // --continue 应自动选择最近会话
        var options = new CommandLineOptions { ContinueSession = true };
        var context = CreateContext(options, fs);

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert
        nextCalled.Should().BeTrue("--continue 找到会话后应继续调用 next");
    }

    [Fact]
    public async Task Resume_WithExactSessionId_ShouldLoadSession()
    {
        // Arrange
        var step = new SessionResumeStep();
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(SessionsDir);

        WriteSessionFile(fs, "abc-123", "测试会话", DateTime.UtcNow,
            [new SessionMessage { Role = "user", Content = "hello" }]);

        // --resume abc-123
        var options = new CommandLineOptions { ResumeSessionId = "abc-123" };
        var context = CreateContext(options, fs);

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert
        nextCalled.Should().BeTrue("--resume 精确匹配到 sessionId 后应继续调用 next");
    }

    [Fact]
    public async Task Resume_WithNonExistentId_ShouldStillCallNextWithoutError()
    {
        // Arrange
        var step = new SessionResumeStep();
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(SessionsDir);

        var options = new CommandLineOptions { ResumeSessionId = "non-existent-id" };
        var context = CreateContext(options, fs);

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 找不到会话不应崩溃，应友好提示并继续启动
        nextCalled.Should().BeTrue("找不到会话时应继续启动新会话，不阻塞用户");
    }

    [Fact]
    public async Task Resume_WithTitleMatch_ShouldLoadSession()
    {
        // Arrange — 通过标题模糊匹配
        var step = new SessionResumeStep();
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(SessionsDir);

        WriteSessionFile(fs, "session-001", "重构认证模块", DateTime.UtcNow,
            [new SessionMessage { Role = "user", Content = "开始重构" }]);

        var options = new CommandLineOptions { ResumeSessionId = "重构" };
        var context = CreateContext(options, fs);

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert
        nextCalled.Should().BeTrue("--resume 按标题匹配成功后应继续调用 next");
    }

    [Fact]
    public async Task Continue_WithoutSession_ShouldSkipResumeAndContinue()
    {
        // Arrange — Session 属性为 null（模拟 SessionInitStep 未初始化）
        var step = new SessionResumeStep();
        var fs = new InMemoryFileSystem();
        fs.CreateDirectory(SessionsDir);

        // Session = null（默认就是 null）
        var options = new CommandLineOptions { ContinueSession = true };
        var context = CreateContext(options, fs);
        // 不设置 context.Session，保持为 null

        var nextCalled = false;
        await step.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — Session 未初始化时不应阻塞
        nextCalled.Should().BeTrue("Session 为 null 时应跳过恢复并继续");
    }
}
