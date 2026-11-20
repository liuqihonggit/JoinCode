namespace Brain.Tests.Context;

/// <summary>
/// 消息转储服务契约 — JCC_DUMP_MESSAGES=1 时必须把「发给模型的完整消息列表」写到文件。
/// <para>这是提示词可观测性的最后一层：QueryEngine 的 MessageDumpMiddleware 在调用模型前 await 本服务；
/// 这里锁住服务本身确实写了 system 消息，真机部分由 `jcc -p` 验证服务被接上。</para>
/// </summary>
public sealed class ChatFileContextServiceDumpTests {
    [Fact]
    public async Task DumpMessageListAsync_WhenEnabled_WritesSystemPromptAndUserMessage() {
        var previous = Environment.GetEnvironmentVariable("JCC_DUMP_MESSAGES");
        Environment.SetEnvironmentVariable("JCC_DUMP_MESSAGES", "1");
        try {
            var fs = new Testing.Common.Services.InMemoryFileSystem();
            fs.SetCurrentDirectory("/test/project");
            await using var tracker = new Core.Prompts.FileContextTracker();
            await using var sut = new ChatFileContextService(tracker, fs);

            var messages = new List<ApiMessage> {
                new(MessageRole.System, "# 使用您的工具\n - goal_get"),
                new(MessageRole.User, "hi")
            };

            await sut.DumpMessageListAsync(messages, "dump_probe", 1, 0);

            var dir = JoinCode.Abstractions.Configuration.AppData.AppDataConstants.Paths.DumpsDirectory;
            var files = fs.GetFiles(dir, "chat_dump_probe_turn1_iter0.txt", System.IO.SearchOption.TopDirectoryOnly);
            files.Should().ContainSingle();
            var text = fs.ReadAllText(files[0]);
            text.Should().Contain("[System]");
            text.Should().Contain("# 使用您的工具");
            text.Should().Contain("[User]");
            text.Should().Contain("hi");
        } finally {
            Environment.SetEnvironmentVariable("JCC_DUMP_MESSAGES", previous);
        }
    }

    [Fact]
    public async Task DumpMessageListAsync_WhenDisabled_WritesNothing() {
        var previous = Environment.GetEnvironmentVariable("JCC_DUMP_MESSAGES");
        Environment.SetEnvironmentVariable("JCC_DUMP_MESSAGES", "0");
        try {
            var fs = new Testing.Common.Services.InMemoryFileSystem();
            fs.SetCurrentDirectory("/test/project-disabled");
            await using var tracker = new Core.Prompts.FileContextTracker();
            await using var sut = new ChatFileContextService(tracker, fs);

            await sut.DumpMessageListAsync([new ApiMessage(MessageRole.User, "hi")], "noop", 1, 0);

            var dir = JoinCode.Abstractions.Configuration.AppData.AppDataConstants.Paths.DumpsDirectory;
            fs.GetFiles(dir, "chat_noop_*", System.IO.SearchOption.TopDirectoryOnly).Should().BeEmpty();
        } finally {
            Environment.SetEnvironmentVariable("JCC_DUMP_MESSAGES", previous);
        }
    }
}
