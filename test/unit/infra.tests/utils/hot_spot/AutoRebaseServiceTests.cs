namespace Infra.Tests.HotSpot;

public sealed class AutoRebaseServiceTests
{
    private readonly FakeGitRunner _git = new();
    private readonly FakeMailbox _mailbox = new();
    private readonly IAutoRebaseService _sut;

    public AutoRebaseServiceTests()
    {
        _sut = new AutoRebaseService(_git, _mailbox);
    }

    private static AutoRebaseRequest MakeRequest(string? captainId = "captain") =>
        new() { WorktreePath = "/fake/worktree", AgentId = "worker-1", CaptainId = captainId };

    [Fact]
    public async Task FetchFails_ShouldReturnFailed()
    {
        _git.Setup("fetch", success: false, error: "network error");

        var result = await _sut.RebaseSyncAsync(MakeRequest());

        result.FinalState.Should().Be(RebaseSyncState.Failed);
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("fetch");
    }

    [Fact]
    public async Task NoUpstreamChanges_ShouldReturnSkipped()
    {
        _git.Setup("fetch", success: true);
        _git.Setup("rev-list", success: true, output: "0");

        var result = await _sut.RebaseSyncAsync(MakeRequest());

        result.FinalState.Should().Be(RebaseSyncState.Skipped);
        result.WasSkipped.Should().BeTrue();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CleanWorktree_RebaseSucceeds_ShouldReturnCompleted()
    {
        _git.Setup("fetch", success: true);
        _git.Setup("rev-list", success: true, output: "3");
        _git.Setup("status", success: true, output: "");
        _git.Setup("rebase", success: true);

        var result = await _sut.RebaseSyncAsync(MakeRequest());

        result.FinalState.Should().Be(RebaseSyncState.Completed);
        result.Success.Should().BeTrue();
        result.HadConflicts.Should().BeFalse();
        _git.ExecutedArguments.Should().NotContain(a => a.Contains("stash"));
    }

    [Fact]
    public async Task DirtyWorktree_StashThenRebaseSucceeds_ShouldReturnCompleted()
    {
        _git.Setup("fetch", success: true);
        _git.Setup("rev-list", success: true, output: "2");
        _git.Setup("status", success: true, output: " M file1.cs\n");
        _git.Setup("stash push", success: true);
        _git.Setup("rebase", success: true);
        _git.Setup("stash pop", success: true);

        var result = await _sut.RebaseSyncAsync(MakeRequest());

        result.FinalState.Should().Be(RebaseSyncState.Completed);
        result.Success.Should().BeTrue();
        _git.ExecutedArguments.Should().Contain(a => a.Contains("stash push"));
        _git.ExecutedArguments.Should().Contain(a => a.Contains("stash pop"));
    }

    [Fact]
    public async Task RebaseConflict_ShouldAbortAndNotify()
    {
        _git.Setup("fetch", success: true);
        _git.Setup("rev-list", success: true, output: "1");
        _git.Setup("status", success: true, output: "");
        _git.Setup("rebase", success: false, error: "conflict", exitCode: 1);
        _git.Setup("diff --name-only --diff-filter=U", success: true, output: "src/Foo.cs\nsrc/Bar.cs\n");
        _git.Setup("rebase --abort", success: true);

        var result = await _sut.RebaseSyncAsync(MakeRequest());

        result.FinalState.Should().Be(RebaseSyncState.Completed);
        result.HadConflicts.Should().BeTrue();
        result.ConflictFiles.Should().Equal("src/Foo.cs", "src/Bar.cs");
        _git.ExecutedArguments.Should().Contain(a => a.Contains("rebase --abort"));
        _mailbox.SentMessages.Should().HaveCount(1);
        _mailbox.SentMessages[0].Content.Should().Contain("src/Foo.cs");
        _mailbox.SentMessages[0].Content.Should().Contain("src/Bar.cs");
    }

    [Fact]
    public async Task RebaseConflict_DirtyWorktree_ShouldAbortAndPopStashAndNotify()
    {
        _git.Setup("fetch", success: true);
        _git.Setup("rev-list", success: true, output: "1");
        _git.Setup("status", success: true, output: " M file.cs\n");
        _git.Setup("stash push", success: true);
        _git.Setup("rebase", success: false, exitCode: 1);
        _git.Setup("diff --name-only --diff-filter=U", success: true, output: "src/Hot.cs\n");
        _git.Setup("rebase --abort", success: true);
        _git.Setup("stash pop", success: true);

        var result = await _sut.RebaseSyncAsync(MakeRequest());

        result.HadConflicts.Should().BeTrue();
        result.ConflictFiles.Should().Equal("src/Hot.cs");
        _git.ExecutedArguments.Should().Contain(a => a.Contains("rebase --abort"));
        _git.ExecutedArguments.Should().Contain(a => a.Contains("stash pop"));
    }

    [Fact]
    public async Task StashFails_ShouldReturnFailed()
    {
        _git.Setup("fetch", success: true);
        _git.Setup("rev-list", success: true, output: "1");
        _git.Setup("status", success: true, output: " M file.cs\n");
        _git.Setup("stash push", success: false, error: "stash error");

        var result = await _sut.RebaseSyncAsync(MakeRequest());

        result.FinalState.Should().Be(RebaseSyncState.Failed);
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("stash");
    }

    [Fact]
    public async Task RevListFails_ShouldReturnFailed()
    {
        _git.Setup("fetch", success: true);
        _git.Setup("rev-list", success: false, error: "rev-list error");

        var result = await _sut.RebaseSyncAsync(MakeRequest());

        result.FinalState.Should().Be(RebaseSyncState.Failed);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Conflict_NoCaptainId_ShouldSkipMailboxNotify()
    {
        _git.Setup("fetch", success: true);
        _git.Setup("rev-list", success: true, output: "1");
        _git.Setup("status", success: true, output: "");
        _git.Setup("rebase", success: false, exitCode: 1);
        _git.Setup("diff --name-only --diff-filter=U", success: true, output: "src/Foo.cs\n");
        _git.Setup("rebase --abort", success: true);

        var result = await _sut.RebaseSyncAsync(MakeRequest(captainId: null));

        result.HadConflicts.Should().BeTrue();
        _mailbox.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Success_NoStashPopNeeded_ShouldNotPopStash()
    {
        _git.Setup("fetch", success: true);
        _git.Setup("rev-list", success: true, output: "1");
        _git.Setup("status", success: true, output: "");
        _git.Setup("rebase", success: true);

        await _sut.RebaseSyncAsync(MakeRequest());

        _git.ExecutedArguments.Should().NotContain(a => a.Contains("stash pop"));
    }

    [Fact]
    public async Task RebaseSucceeds_ShouldExecuteFetchBeforeRebase()
    {
        _git.Setup("fetch", success: true);
        _git.Setup("rev-list", success: true, output: "1");
        _git.Setup("status", success: true, output: "");
        _git.Setup("rebase", success: true);

        await _sut.RebaseSyncAsync(MakeRequest());

        var fetchIdx = _git.ExecutedArguments.FindIndex(a => a.Contains("fetch"));
        var rebaseIdx = _git.ExecutedArguments.FindIndex(a => a.Contains("rebase") && !a.Contains("abort"));
        fetchIdx.Should().BeLessThan(rebaseIdx, "fetch 必须在 rebase 之前执行");
    }
}

internal sealed class FakeGitRunner : IGitCommandRunner
{
    private readonly List<(string Key, GitCommandResult Result)> _setups = [];
    public List<string> ExecutedArguments { get; } = [];

    public void Setup(string argsContains, bool success, string output = "", string error = "", int exitCode = 0)
    {
        _setups.Add((argsContains, new GitCommandResult { Success = success, Output = output, Error = error, ExitCode = exitCode }));
    }

    public Task<GitCommandResult> ExecuteAsync(string arguments, string? workingDirectory = null, CancellationToken ct = default)
    {
        ExecutedArguments.Add(arguments);
        foreach (var (key, result) in _setups)
        {
            if (arguments.Contains(key, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(result);
        }
        return Task.FromResult(new GitCommandResult { Success = true, Output = string.Empty, ExitCode = 0 });
    }

    public Task<MergeConflictResult> DetectMergeConflictAsync(string branch1, string branch2, string? workingDirectory = null, CancellationToken ct = default)
        => Task.FromResult(new MergeConflictResult { HasConflict = false });

    public Task<StaleConflictMarkerResult> DetectStaleConflictMarkersAsync(string? workingDirectory = null, CancellationToken ct = default)
        => Task.FromResult(new StaleConflictMarkerResult { HasStaleMarkers = false });
}
