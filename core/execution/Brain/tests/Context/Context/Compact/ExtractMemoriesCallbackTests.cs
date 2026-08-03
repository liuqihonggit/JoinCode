using JoinCode.Abstractions.Hooks;
using JoinCode.Abstractions.Interfaces;

namespace Brain.Tests.Context.Compact;

public sealed class ExtractMemoriesCallbackTests
{
    private static Testing.Common.Services.InMemoryFileSystem CreateFileSystem()
    {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        fs.SetCurrentDirectory("/test/project");
        fs.CreateDirectory("/test/project/.jcc/memory");
        return fs;
    }

    private static ExtractMemoriesCallback CreateCallback(
        Testing.Common.Services.InMemoryFileSystem fs,
        IForkSubAgentManager? forkManager = null)
    {
        return new ExtractMemoriesCallback(fs, forkManager);
    }

    private static PostSamplingContext CreateContext(string? querySource = "repl_main_thread", string? sessionId = "test-session")
    {
        return new PostSamplingContext
        {
            EstimatedTokenCount = 5000,
            ToolCallsSinceLastExtraction = 0,
            QuerySource = querySource,
            SessionId = sessionId,
            CancellationToken = CancellationToken.None
        };
    }

    [Fact]
    public async Task OnPostSamplingAsync_NonReplSource_DoesNothing()
    {
        var fs = CreateFileSystem();
        var forkMock = new Mock<IForkSubAgentManager>();
        var callback = CreateCallback(fs, forkMock.Object);

        var context = CreateContext(querySource: "sdk");
        await callback.OnPostSamplingAsync(context).ConfigureAwait(true);

        forkMock.Verify(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnPostSamplingAsync_WithoutForkManager_DoesNotThrow()
    {
        var fs = CreateFileSystem();
        var callback = CreateCallback(fs, forkManager: null);

        var context = CreateContext();
        var act = async () => await callback.OnPostSamplingAsync(context).ConfigureAwait(true);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void BuildExtractAutoOnlyPrompt_DoesNotThrow()
    {
        try
        {
            var result = ExtractMemoriesSection.BuildExtractAutoOnlyPrompt(5, "");
            result.Should().NotBeNullOrEmpty();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"BuildExtractAutoOnlyPrompt failed: {ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    [Fact]
    public async Task OnPostSamplingAsync_WithForkManager_CallsForkAsync()
    {
        var fs = CreateFileSystem();
        var forkMock = new Mock<IForkSubAgentManager>();
        forkMock.Setup(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ForkResult { ForkId = "fork-1", State = ForkState.Completed });

        var callback = CreateCallback(fs, forkMock.Object);
        var context = CreateContext();

        try
        {
            await callback.OnPostSamplingAsync(context).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"ExtractMemoriesCallback threw: {ex.Message}", ex);
        }

        forkMock.Verify(f => f.ForkAsync(
            It.Is<ForkOptions>(o => o.TaskDescription == "extract_memories"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnPostSamplingAsync_ForkFailure_DoesNotThrow()
    {
        var fs = CreateFileSystem();
        var forkMock = new Mock<IForkSubAgentManager>();
        forkMock.Setup(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fork failed"));

        var callback = CreateCallback(fs, forkMock.Object);
        var context = CreateContext();
        var act = async () => await callback.OnPostSamplingAsync(context).ConfigureAwait(true);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnPostSamplingAsync_WithExistingMemoryFiles_ScansDirectory()
    {
        var fs = CreateFileSystem();
        fs.WriteAllText("/test/project/.jcc/memory/user_role.md", "# User Role\nDeveloper");

        var forkMock = new Mock<IForkSubAgentManager>();
        forkMock.Setup(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ForkResult { ForkId = "fork-1", State = ForkState.Completed });

        var callback = CreateCallback(fs, forkMock.Object);
        var context = CreateContext();
        await callback.OnPostSamplingAsync(context).ConfigureAwait(true);

        forkMock.Verify(f => f.ForkAsync(
            It.Is<ForkOptions>(o => o.SystemPrompt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
