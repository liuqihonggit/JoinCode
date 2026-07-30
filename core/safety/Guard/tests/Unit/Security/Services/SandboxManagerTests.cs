namespace Guard.Tests.Security.Services;

using JoinCode.Abstractions.Security.Sandbox;

public sealed class SandboxManagerTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;

    [Fact]
    public void IsInSandbox_Initially_ShouldBeFalse()
    {
        var sut = CreateSut();
        sut.IsInSandbox.Should().BeFalse();
    }

    [Fact]
    public void CurrentSandbox_Initially_ShouldBeNull()
    {
        var sut = CreateSut();
        sut.CurrentSandbox.Should().BeNull();
    }

    [Fact]
    public async Task EnterSandboxAsync_NullOptions_ShouldThrowArgumentNullException()
    {
        var sut = CreateSut();
        var act = async () => await sut.EnterSandboxAsync(null!).ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task EnterSandboxAsync_SoftType_ShouldSetSandboxState()
    {
        var sut = CreateSut();
        var options = new SandboxOptions
        {
            Type = SandboxType.Soft,
            RestrictFileSystem = true,
            RestrictNetwork = true
        };

        var info = await sut.EnterSandboxAsync(options).ConfigureAwait(true);

        info.Should().NotBeNull();
        info.Type.Should().Be(SandboxType.Soft);
        info.IsRestricted.Should().BeTrue();
        info.EnteredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        sut.IsInSandbox.Should().BeTrue();
        sut.CurrentSandbox.Should().NotBeNull();

        await sut.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task EnterSandboxAsync_WithCustomRootPath_ShouldUseCustomPath()
    {
        var sut = CreateSut();
        var rootPath = $"/test/test-sandbox-{Guid.NewGuid():N}";
        var options = new SandboxOptions
        {
            Type = SandboxType.Soft,
            SandboxRoot = rootPath
        };

        var info = await sut.EnterSandboxAsync(options).ConfigureAwait(true);

        info.RootPath.Should().Be(rootPath);

        await sut.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task EnterSandboxAsync_NoneType_ShouldDefaultToSoft()
    {
        var originalEnv = Environment.GetEnvironmentVariable("JCC_SANDBOX_MODE");
        try
        {
            Environment.SetEnvironmentVariable("JCC_SANDBOX_MODE", null);

            var sut = CreateSut();
            var options = new SandboxOptions
            {
                Type = SandboxType.None
            };

            var info = await sut.EnterSandboxAsync(options).ConfigureAwait(true);

            info.Type.Should().Be(SandboxType.Soft);

            await sut.ExitSandboxAsync().ConfigureAwait(true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_SANDBOX_MODE", originalEnv);
        }
    }

    [Fact]
    public async Task EnterSandboxAsync_AlreadyInSandbox_ShouldThrowInvalidOperationException()
    {
        var sut = CreateSut();
        var options = new SandboxOptions
        {
            Type = SandboxType.Soft
        };

        await sut.EnterSandboxAsync(options).ConfigureAwait(true);

        var act = async () => await sut.EnterSandboxAsync(options).ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        await sut.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExitSandboxAsync_WhenInSandbox_ShouldClearState()
    {
        var sut = CreateSut();
        var options = new SandboxOptions
        {
            Type = SandboxType.Soft
        };

        await sut.EnterSandboxAsync(options).ConfigureAwait(true);
        sut.IsInSandbox.Should().BeTrue();

        await sut.ExitSandboxAsync().ConfigureAwait(true);

        sut.IsInSandbox.Should().BeFalse();
        sut.CurrentSandbox.Should().BeNull();
    }

    [Fact]
    public async Task ExitSandboxAsync_WhenNotInSandbox_ShouldNotThrow()
    {
        var sut = CreateSut();
        var act = async () => await sut.ExitSandboxAsync().ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public void ResolvePath_NotInSandbox_ShouldReturnFullPath()
    {
        var sut = CreateSut();
        var path = "relative/path/file.txt";

        var resolved = sut.ResolvePath(path);

        resolved.Should().Be(Path.GetFullPath(path));
    }

    [Fact]
    public async Task ResolvePath_InSandboxRestricted_PathInsideSandbox_ShouldReturnFullPath()
    {
        var sut = CreateSut();
        var rootPath = $"/test/test-sandbox-{Guid.NewGuid():N}";
        var options = new SandboxOptions
        {
            Type = SandboxType.Soft,
            SandboxRoot = rootPath,
            RestrictFileSystem = true
        };

        await sut.EnterSandboxAsync(options).ConfigureAwait(true);

        var path = Path.Combine(rootPath, "subdir", "file.txt");
        var resolved = sut.ResolvePath(path);

        resolved.Should().Be(Path.GetFullPath(path));

        await sut.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ResolvePath_InSandboxRestricted_PathOutsideSandbox_ShouldRemapToSandboxRoot()
    {
        var sut = CreateSut();
        var rootPath = $"/test/test-sandbox-{Guid.NewGuid():N}";
        var options = new SandboxOptions
        {
            Type = SandboxType.Soft,
            SandboxRoot = rootPath,
            RestrictFileSystem = true
        };

        await sut.EnterSandboxAsync(options).ConfigureAwait(true);

        var outsidePath = "/outside/sandbox/file.txt";
        var resolved = sut.ResolvePath(outsidePath);

        var normalizedRoot = Path.GetFullPath(rootPath);
        resolved.Should().StartWith(normalizedRoot);
        resolved.Should().Contain("file.txt");

        await sut.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task SwitchProviderAsync_SoftToProcess_ShouldSwitch()
    {
        var sut = CreateSut();
        var options = new SandboxOptions
        {
            Type = SandboxType.Soft,
            RestrictFileSystem = true,
            RestrictNetwork = true
        };

        await sut.EnterSandboxAsync(options).ConfigureAwait(true);
        sut.ActiveSandboxType.Should().Be(SandboxType.Soft);

        if (sut.AvailableTypes.Contains(SandboxType.Process))
        {
            await sut.SwitchProviderAsync(SandboxType.Process).ConfigureAwait(true);
            sut.ActiveSandboxType.Should().Be(SandboxType.Process);
        }

        await sut.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public void AvailableTypes_ShouldContainSoft()
    {
        var sut = CreateSut();
        sut.AvailableTypes.Should().Contain(SandboxType.Soft);
    }

    [Fact]
    public async Task CreateSandboxAsync_MultiInstance_ShouldWork()
    {
        var sut = CreateSut();
        var options1 = new SandboxOptions { Type = SandboxType.Soft };
        var options2 = new SandboxOptions { Type = SandboxType.Soft };

        var info1 = await sut.CreateSandboxAsync(SandboxType.Soft, options1).ConfigureAwait(true);
        var info2 = await sut.CreateSandboxAsync(SandboxType.Soft, options2).ConfigureAwait(true);

        info1.SandboxId.Should().NotBe(info2.SandboxId);

        sut.GetSandboxInfo(info1.SandboxId).Should().NotBeNull();
        sut.GetSandboxInfo(info2.SandboxId).Should().NotBeNull();

        await sut.DestroySandboxAsync(info1.SandboxId).ConfigureAwait(true);
        await sut.DestroySandboxAsync(info2.SandboxId).ConfigureAwait(true);

        sut.GetSandboxInfo(info1.SandboxId).Should().BeNull();
        sut.GetSandboxInfo(info2.SandboxId).Should().BeNull();
    }

    private SandboxManager CreateSut()
    {
        var providers = new List<ISandboxProvider>
        {
            new SoftSandboxProvider(_fs, NullLogger<SoftSandboxProvider>.Instance),
            new ProcessSandboxProvider(_fs, Mock.Of<IProcessService>(), NullLogger<ProcessSandboxProvider>.Instance)
        };

        return new SandboxManager(providers, _fs, ipcClient: null, NullLogger<SandboxManager>.Instance);
    }
}
