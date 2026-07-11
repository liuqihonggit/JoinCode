using JoinCode.Abstractions.Hooks;
using JoinCode.Abstractions.Interfaces;

namespace Brain.Tests.Prompts;

public sealed class PromptSuggestionCallbackTests
{
    private static PromptSuggestionCallback CreateCallback(IForkSubAgentManager? forkManager = null)
    {
        return new PromptSuggestionCallback(forkManager);
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
        var forkMock = new Mock<IForkSubAgentManager>();
        var callback = CreateCallback(forkMock.Object);

        var context = CreateContext(querySource: "sdk");
        await callback.OnPostSamplingAsync(context).ConfigureAwait(true);

        forkMock.Verify(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnPostSamplingAsync_WithoutForkManager_DoesNotThrow()
    {
        var callback = CreateCallback(forkManager: null);

        var context = CreateContext();
        var act = async () => await callback.OnPostSamplingAsync(context).ConfigureAwait(true);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnPostSamplingAsync_WithForkManager_CallsForkAsync()
    {
        var forkMock = new Mock<IForkSubAgentManager>();
        forkMock.Setup(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ForkResult { ForkId = "fork-1", State = ForkState.Completed, Result = "run the tests" });

        var callback = CreateCallback(forkMock.Object);
        var context = CreateContext();
        await callback.OnPostSamplingAsync(context).ConfigureAwait(true);

        forkMock.Verify(f => f.ForkAsync(
            It.Is<ForkOptions>(o => o.TaskDescription == "prompt_suggestion" && o.MaxIterations == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnPostSamplingAsync_FilteredSuggestion_DoesNotLogAsValid()
    {
        var forkMock = new Mock<IForkSubAgentManager>();
        forkMock.Setup(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ForkResult { ForkId = "fork-1", State = ForkState.Completed, Result = "done" });

        var callback = CreateCallback(forkMock.Object);
        var context = CreateContext();
        await callback.OnPostSamplingAsync(context).ConfigureAwait(true);

        forkMock.Verify(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnPostSamplingAsync_ForkFailure_DoesNotThrow()
    {
        var forkMock = new Mock<IForkSubAgentManager>();
        forkMock.Setup(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fork failed"));

        var callback = CreateCallback(forkMock.Object);
        var context = CreateContext();
        var act = async () => await callback.OnPostSamplingAsync(context).ConfigureAwait(true);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnPostSamplingAsync_ForkReturnsFailed_DoesNotProcessResult()
    {
        var forkMock = new Mock<IForkSubAgentManager>();
        forkMock.Setup(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ForkResult { ForkId = "fork-1", State = ForkState.Failed });

        var callback = CreateCallback(forkMock.Object);
        var context = CreateContext();
        await callback.OnPostSamplingAsync(context).ConfigureAwait(true);

        forkMock.Verify(f => f.ForkAsync(It.IsAny<ForkOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
