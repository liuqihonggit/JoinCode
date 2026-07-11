namespace Brain.Tests.Context.Compact;

/// <summary>
/// SessionMemoryExtractionService 单元测试 — 对齐 TS sessionMemory.ts
/// 验证文件初始化、提示词构建、提取阈值判断
/// </summary>
public sealed class SessionMemoryExtractionServiceTests
{
    private static Testing.Common.Services.InMemoryFileSystem CreateFileSystem(string? cwd = null)
    {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        if (cwd is not null) fs.SetCurrentDirectory(cwd);
        return fs;
    }

    [Fact]
    public async Task InitializeSessionMemoryFileAsync_CreatesFileWithTemplate()
    {
        var fs = CreateFileSystem("/test/project");
        var compactService = new SessionMemoryCompactService(
            new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance),
            fileSystem: fs);
        var service = new SessionMemoryExtractionService(compactService, fs);

        var content = await service.InitializeSessionMemoryFileAsync().ConfigureAwait(true);

        content.Should().NotBeNullOrEmpty();
        content.Should().Contain("# 会话标题");
        fs.FileExists("/test/project/.jcc/session-memory.md").Should().BeTrue();
    }

    [Fact]
    public async Task InitializeSessionMemoryFileAsync_ExistingFile_ReturnsContent()
    {
        var fs = CreateFileSystem("/test/project");
        var existingContent = "# 会话标题\nMy Session\n\n# 当前状态\nWorking on tests";
        fs.CreateDirectory("/test/project/.jcc");
        fs.WriteAllText("/test/project/.jcc/session-memory.md", existingContent);

        var compactService = new SessionMemoryCompactService(
            new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance),
            fileSystem: fs);
        var service = new SessionMemoryExtractionService(compactService, fs);

        var content = await service.InitializeSessionMemoryFileAsync().ConfigureAwait(true);

        content.Should().Be(existingContent);
    }

    [Fact]
    public async Task BuildExtractionPromptAsync_ReturnsPromptWithNotesPath()
    {
        var fs = CreateFileSystem("/test/project");
        var compactService = new SessionMemoryCompactService(
            new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance),
            fileSystem: fs);
        var service = new SessionMemoryExtractionService(compactService, fs);

        await service.InitializeSessionMemoryFileAsync().ConfigureAwait(true);
        var prompt = await service.BuildExtractionPromptAsync().ConfigureAwait(true);

        prompt.Should().NotBeNullOrEmpty();
        prompt.Should().Contain("session-memory.md");
        prompt.Should().Contain("Edit");
    }

    [Fact]
    public void ShouldExtract_FirstTime_BelowThreshold_ReturnsFalse()
    {
        var fs = CreateFileSystem("/test/project");
        var compactService = new SessionMemoryCompactService(
            new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance),
            fileSystem: fs);
        var service = new SessionMemoryExtractionService(compactService, fs);

        var result = service.ShouldExtract(5000, 0);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldExtract_FirstTime_AboveThreshold_ReturnsTrue()
    {
        var fs = CreateFileSystem("/test/project");
        var compactService = new SessionMemoryCompactService(
            new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance),
            fileSystem: fs);
        var service = new SessionMemoryExtractionService(compactService, fs);

        var result = service.ShouldExtract(15000, 0);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldExtract_AfterPreviousExtraction_BelowUpdateThreshold_ReturnsFalse()
    {
        var fs = CreateFileSystem("/test/project");
        var compactService = new SessionMemoryCompactService(
            new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance),
            fileSystem: fs);
        var service = new SessionMemoryExtractionService(compactService, fs);

        service.RecordExtractionCompleted(15000);

        var result = service.ShouldExtract(18000, 1);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldExtract_AfterPreviousExtraction_AboveUpdateThreshold_ReturnsTrue()
    {
        var fs = CreateFileSystem("/test/project");
        var compactService = new SessionMemoryCompactService(
            new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance),
            fileSystem: fs);
        var service = new SessionMemoryExtractionService(compactService, fs);

        service.RecordExtractionCompleted(15000);

        var result = service.ShouldExtract(25000, 1);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldExtract_ToolCallsExceedThreshold_ReturnsTrue()
    {
        var fs = CreateFileSystem("/test/project");
        var compactService = new SessionMemoryCompactService(
            new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance),
            fileSystem: fs);
        var service = new SessionMemoryExtractionService(compactService, fs);

        service.RecordExtractionCompleted(15000);

        var result = service.ShouldExtract(16000, 5);

        result.Should().BeTrue();
    }

    [Fact]
    public void GetMemoryFilePath_ReturnsExpectedPath()
    {
        var fs = CreateFileSystem("/my/project");
        var compactService = new SessionMemoryCompactService(
            new MicrocompactService(JoinCode.Abstractions.Clock.SystemClockService.Instance),
            fileSystem: fs);
        var service = new SessionMemoryExtractionService(compactService, fs);

        var path = service.GetMemoryFilePath();

        path.Should().Contain(".jcc");
        path.Should().Contain("session-memory.md");
    }
}
