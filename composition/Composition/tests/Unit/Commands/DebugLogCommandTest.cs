using JoinCode.Abstractions.Utils.Diagnostics;
using JoinCode.Abstractions.Prompts;
using JoinCode.Abstractions.Models.ErrorRecovery;
using JoinCode.ChatCommands;
using FluentAssertions;

namespace Composition.Tests.Commands;

/// <summary>
/// DebugLogCommand 单元测试 — 验证参数解析、默认行为、clear 功能
/// 通过 ExecuteAsync 间接测试 ParseFlags 逻辑
/// </summary>
public sealed class DebugLogCommandTest
{
    private readonly Mock<IDebugLogBuffer> _debugLogBuffer;
    private readonly Mock<ICrashSnapshotStore> _crashSnapshotStore;
    private readonly Mock<ISystemPromptProvider> _systemPromptProvider;
    private readonly Mock<IToolRegistry> _toolRegistry;
    private readonly ServiceProvider _serviceProvider;
    private readonly DebugLogCommand _command;

    public DebugLogCommandTest()
    {
        _debugLogBuffer = new Mock<IDebugLogBuffer>();
        _crashSnapshotStore = new Mock<ICrashSnapshotStore>();
        _systemPromptProvider = new Mock<ISystemPromptProvider>();
        _toolRegistry = new Mock<IToolRegistry>();

        // 设置默认 mock 返回值
        _debugLogBuffer.Setup(b => b.Count).Returns(0);
        _debugLogBuffer.Setup(b => b.GetRecent(It.IsAny<int>())).Returns([]);
        _debugLogBuffer.Setup(b => b.GetByLevel(It.IsAny<DebugLogLevel>(), It.IsAny<int>())).Returns([]);
        _crashSnapshotStore.Setup(s => s.TotalCount).Returns(0);
        _crashSnapshotStore.Setup(s => s.UnacknowledgedCount).Returns(0);
        _crashSnapshotStore.Setup(s => s.GetRecent(It.IsAny<int>())).Returns([]);
        _systemPromptProvider.Setup(p => p.GetSections()).Returns([]);
        _toolRegistry.Setup(r => r.GetCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var services = new ServiceCollection();
        services.AddSingleton(_debugLogBuffer.Object);
        services.AddSingleton(_crashSnapshotStore.Object);
        services.AddSingleton(_systemPromptProvider.Object);
        services.AddSingleton(_toolRegistry.Object);
        _serviceProvider = services.BuildServiceProvider();

        _command = new DebugLogCommand();
    }

    #region ParseFlags — 无参数默认显示全部

    [Fact]
    public async Task NoArgs_DefaultShowsAll()
    {
        // 无参数时 ParseFlags 返回 DebugSection.All
        // All 包含 Error 标志，因此 HasFlag(Error) 为 true，进入 AppendErrors 分支
        var context = CreateContext(string.Empty);
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        // All 模式下 HasFlag(Error) 为 true，调用 AppendErrors → GetByLevel(Error)
        _debugLogBuffer.Verify(b => b.GetByLevel(DebugLogLevel.Error, It.IsAny<int>()), Times.AtLeastOnce);
    }

    #endregion

    #region ParseFlags — -a / --all

    [Fact]
    public async Task AllFlag_EntersErrorBranch()
    {
        // -a 设置 DebugSection.All，All 包含 Error，因此进入 AppendErrors 分支
        var context = CreateContext("-a");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _debugLogBuffer.Verify(b => b.GetByLevel(DebugLogLevel.Error, It.IsAny<int>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task AllFlag_LongForm_EntersErrorBranch()
    {
        var context = CreateContext("--all");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _debugLogBuffer.Verify(b => b.GetByLevel(DebugLogLevel.Error, It.IsAny<int>()), Times.AtLeastOnce);
    }

    #endregion

    #region ParseFlags — -e / --error

    [Fact]
    public async Task ErrorFlag_ShowsOnlyErrors()
    {
        var context = CreateContext("-e");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        // Error 模式下调用 GetByLevel(Error)
        _debugLogBuffer.Verify(b => b.GetByLevel(DebugLogLevel.Error, It.IsAny<int>()), Times.AtLeastOnce);
        // 不应调用 GetRecent（非 All 模式下的 AppendLogs）
        _debugLogBuffer.Verify(b => b.GetRecent(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ErrorFlag_LongForm_ShowsOnlyErrors()
    {
        var context = CreateContext("--error");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _debugLogBuffer.Verify(b => b.GetByLevel(DebugLogLevel.Error, It.IsAny<int>()), Times.AtLeastOnce);
    }

    #endregion

    #region ParseFlags — -w / --warn

    [Fact]
    public async Task WarnFlag_ShowsWarningsAndErrors()
    {
        var context = CreateContext("-w");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        // Warn 模式下调用 GetByLevel(Error)（AppendWarningsAndErrors 内部）
        _debugLogBuffer.Verify(b => b.GetByLevel(DebugLogLevel.Error, It.IsAny<int>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task WarnFlag_LongForm_ShowsWarningsAndErrors()
    {
        var context = CreateContext("--warn");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _crashSnapshotStore.Verify(s => s.GetRecent(It.IsAny<int>()), Times.AtLeastOnce);
    }

    #endregion

    #region ParseFlags — -i / --init

    [Fact]
    public async Task InitFlag_ShowsInitInfo()
    {
        var context = CreateContext("-i");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        // Init 模式下查询 CrashSnapshotStore
        _crashSnapshotStore.Verify(s => s.TotalCount, Times.AtLeastOnce);
    }

    [Fact]
    public async Task InitFlag_LongForm_ShowsInitInfo()
    {
        var context = CreateContext("--init");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _crashSnapshotStore.Verify(s => s.TotalCount, Times.AtLeastOnce);
    }

    #endregion

    #region ParseFlags — -p / --prompt

    [Fact]
    public async Task PromptFlag_ShowsSystemPrompt()
    {
        var context = CreateContext("-p");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _systemPromptProvider.Verify(p => p.GetSections(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task PromptFlag_LongForm_ShowsSystemPrompt()
    {
        var context = CreateContext("--prompt");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _systemPromptProvider.Verify(p => p.GetSections(), Times.AtLeastOnce);
    }

    #endregion

    #region ParseFlags — -l / --log

    [Fact]
    public async Task LogFlag_ShowsDiagnosticLogs()
    {
        var context = CreateContext("-l");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _debugLogBuffer.Verify(b => b.GetRecent(It.IsAny<int>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task LogFlag_LongForm_ShowsDiagnosticLogs()
    {
        var context = CreateContext("--log");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _debugLogBuffer.Verify(b => b.GetRecent(It.IsAny<int>()), Times.AtLeastOnce);
    }

    #endregion

    #region ParseFlags — -c / --clear

    [Fact]
    public async Task ClearFlag_ClearsBuffer()
    {
        var context = CreateContext("-c");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _debugLogBuffer.Verify(b => b.Clear(), Times.Once);
    }

    [Fact]
    public async Task ClearFlag_LongForm_ClearsBuffer()
    {
        var context = CreateContext("--clear");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _debugLogBuffer.Verify(b => b.Clear(), Times.Once);
    }

    [Fact]
    public async Task ClearFlag_ReturnsContinue()
    {
        var context = CreateContext("-c");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    #endregion

    #region ParseFlags — Combined flags

    [Fact]
    public async Task CombinedErrorAndWarn_ErrorTakesPrecedence()
    {
        // -e -w 组合时，Error 标志优先（代码中 HasFlag(Error) 先判断）
        var context = CreateContext("-e -w");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        // Error 模式下调用 GetByLevel(Error)
        _debugLogBuffer.Verify(b => b.GetByLevel(DebugLogLevel.Error, It.IsAny<int>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CombinedInitAndLog_ShowsInitOnly()
    {
        // -i -l 组合时，Init 标志先判断，sb.Clear() 后只显示 Init
        var context = CreateContext("-i -l");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _crashSnapshotStore.Verify(s => s.TotalCount, Times.AtLeastOnce);
    }

    #endregion

    #region Missing Services

    [Fact]
    public async Task NoDebugLogBuffer_DoesNotThrow()
    {
        var emptyServices = new ServiceCollection().BuildServiceProvider();
        var context = CreateContextWithProvider(string.Empty, emptyServices);

        var result = await _command.ExecuteAsync(context);
        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task NoCrashSnapshotStore_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_debugLogBuffer.Object);
        var provider = services.BuildServiceProvider();
        var context = CreateContextWithProvider(string.Empty, provider);

        var result = await _command.ExecuteAsync(context);
        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task NoSystemPromptProvider_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_debugLogBuffer.Object);
        services.AddSingleton(_crashSnapshotStore.Object);
        var provider = services.BuildServiceProvider();
        var context = CreateContextWithProvider(string.Empty, provider);

        var result = await _command.ExecuteAsync(context);
        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task ClearFlag_NoDebugLogBuffer_DoesNotThrow()
    {
        var emptyServices = new ServiceCollection().BuildServiceProvider();
        var context = CreateContextWithProvider("-c", emptyServices);

        var result = await _command.ExecuteAsync(context);
        result.ShouldContinue.Should().BeTrue();
    }

    #endregion

    #region Error Section with Crash Data

    [Fact]
    public async Task ErrorFlag_WithCrashErrors_DisplaysErrors()
    {
        var crashSnapshot = new CrashSnapshot("TestFence", CrashSeverity.Error, new Exception("test error"));
        _crashSnapshotStore.Setup(s => s.GetRecent(It.IsAny<int>()))
            .Returns([crashSnapshot]);

        var context = CreateContext("-e");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _crashSnapshotStore.Verify(s => s.GetRecent(It.IsAny<int>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task WarnFlag_WithWarnings_DisplaysWarnings()
    {
        var warningSnapshot = new CrashSnapshot("WarnFence", CrashSeverity.Warning, new Exception("warning"));
        _crashSnapshotStore.Setup(s => s.GetRecent(It.IsAny<int>()))
            .Returns([warningSnapshot]);

        var context = CreateContext("-w");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _crashSnapshotStore.Verify(s => s.GetRecent(It.IsAny<int>()), Times.AtLeastOnce);
    }

    #endregion

    #region Log Section with Entries

    [Fact]
    public async Task LogFlag_WithEntries_DisplaysLogs()
    {
        var entries = new List<DebugLogEntry>
        {
            new(DateTimeOffset.UtcNow, DebugLogLevel.Info, "STEP", "[STEP] test message"),
            new(DateTimeOffset.UtcNow, DebugLogLevel.Error, "ERROR", "[DIAG-ERR] error message"),
        };
        _debugLogBuffer.Setup(b => b.GetRecent(It.IsAny<int>())).Returns(entries);
        _debugLogBuffer.Setup(b => b.Count).Returns(2);

        var context = CreateContext("-l");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _debugLogBuffer.Verify(b => b.GetRecent(It.IsAny<int>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task LogFlag_EmptyBuffer_DisplaysNoLogsMessage()
    {
        _debugLogBuffer.Setup(b => b.GetRecent(It.IsAny<int>())).Returns([]);
        _debugLogBuffer.Setup(b => b.Count).Returns(0);

        var context = CreateContext("-l");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
    }

    #endregion

    #region Prompt Section with Sections

    [Fact]
    public async Task PromptFlag_WithSections_DisplaysPromptContent()
    {
        var section = SystemPromptSection.Cached("test-section", () => "test prompt content");
        _systemPromptProvider.Setup(p => p.GetSections()).Returns([section]);

        var context = CreateContext("-p");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _systemPromptProvider.Verify(p => p.GetSections(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task PromptFlag_EmptySections_DisplaysNoSectionsMessage()
    {
        _systemPromptProvider.Setup(p => p.GetSections()).Returns([]);

        var context = CreateContext("-p");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
    }

    #endregion

    #region Init Section

    [Fact]
    public async Task InitFlag_WithToolRegistry_DisplaysToolCount()
    {
        _toolRegistry.Setup(r => r.GetCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(42);

        var context = CreateContext("-i");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _toolRegistry.Verify(r => r.GetCountAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task InitFlag_WithCrashStore_DisplaysCrashInfo()
    {
        _crashSnapshotStore.Setup(s => s.TotalCount).Returns(5);
        _crashSnapshotStore.Setup(s => s.UnacknowledgedCount).Returns(2);

        var context = CreateContext("-i");
        var result = await _command.ExecuteAsync(context);

        result.ShouldContinue.Should().BeTrue();
        _crashSnapshotStore.Verify(s => s.TotalCount, Times.AtLeastOnce);
    }

    #endregion

    #region Helper Methods

    private ChatCommandContext CreateContext(string arguments)
    {
        return CreateContextWithProvider(arguments, _serviceProvider);
    }

    private static ChatCommandContext CreateContextWithProvider(string arguments, IServiceProvider serviceProvider)
    {
        return new ChatCommandContext
        {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
            Services = serviceProvider,
        };
    }

    #endregion
}
