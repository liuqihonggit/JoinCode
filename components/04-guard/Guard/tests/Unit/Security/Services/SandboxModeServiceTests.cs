namespace Guard.Tests.Security.Services;

public sealed class SandboxModeServiceTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly SandboxModeService _sut;

    public SandboxModeServiceTests()
    {
        _sut = new SandboxModeService(_fs, NullLogger<SandboxModeService>.Instance);
    }

    [Fact]
    public void IsInSandbox_Initially_ShouldBeFalse()
    {
        _sut.IsInSandbox.Should().BeFalse();
    }

    [Fact]
    public void CurrentSandbox_Initially_ShouldBeNull()
    {
        _sut.CurrentSandbox.Should().BeNull();
    }

    [Fact]
    public async Task EnterSandboxAsync_NullOptions_ShouldThrowArgumentNullException()
    {
        var act = async () => await _sut.EnterSandboxAsync(null!).ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task EnterSandboxAsync_ProcessType_ShouldSetSandboxState()
    {
        var options = new SandboxOptions
        {
            Type = SandboxType.Process,
            RestrictFileSystem = true,
            RestrictNetwork = true
        };

        var info = await _sut.EnterSandboxAsync(options).ConfigureAwait(true);

        info.Should().NotBeNull();
        info.Type.Should().Be(SandboxType.Process);
        info.IsRestricted.Should().BeTrue();
        info.EnteredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        _sut.IsInSandbox.Should().BeTrue();
        _sut.CurrentSandbox.Should().NotBeNull();
    }

    [Fact]
    public async Task EnterSandboxAsync_WithCustomRootPath_ShouldUseCustomPath()
    {
        var rootPath = $"/test/test-sandbox-{Guid.NewGuid():N}";
        var options = new SandboxOptions
        {
            Type = SandboxType.Process,
            SandboxRoot = rootPath
        };

        var info = await _sut.EnterSandboxAsync(options).ConfigureAwait(true);

        info.RootPath.Should().Be(rootPath);
        _fs.DirectoryExists(rootPath).Should().BeTrue();
    }

    [Fact]
    public async Task EnterSandboxAsync_NoneType_ShouldDefaultToProcess()
    {
        var originalEnv = Environment.GetEnvironmentVariable("JCC_SANDBOX_MODE");
        try
        {
            Environment.SetEnvironmentVariable("JCC_SANDBOX_MODE", null);

            var options = new SandboxOptions
            {
                Type = SandboxType.None
            };

            var info = await _sut.EnterSandboxAsync(options).ConfigureAwait(true);

            info.Type.Should().Be(SandboxType.Process);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_SANDBOX_MODE", originalEnv);
            await _sut.ExitSandboxAsync().ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task EnterSandboxAsync_WithEnvVar_ShouldUseEnvVarType()
    {
        var originalEnv = Environment.GetEnvironmentVariable("JCC_SANDBOX_MODE");
        try
        {
            Environment.SetEnvironmentVariable("JCC_SANDBOX_MODE", "Docker");

            var options = new SandboxOptions
            {
                Type = SandboxType.None
            };

            var info = await _sut.EnterSandboxAsync(options).ConfigureAwait(true);

            info.Type.Should().Be(SandboxType.Docker);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JCC_SANDBOX_MODE", originalEnv);
            await _sut.ExitSandboxAsync().ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task EnterSandboxAsync_AlreadyInSandbox_ShouldThrowInvalidOperationException()
    {
        var options = new SandboxOptions
        {
            Type = SandboxType.Process
        };

        await _sut.EnterSandboxAsync(options).ConfigureAwait(true);

        var act = async () => await _sut.EnterSandboxAsync(options).ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        await _sut.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task EnterSandboxAsync_NoRestrictions_ShouldSetIsRestrictedFalse()
    {
        var options = new SandboxOptions
        {
            Type = SandboxType.Process,
            RestrictFileSystem = false,
            RestrictNetwork = false
        };

        var info = await _sut.EnterSandboxAsync(options).ConfigureAwait(true);

        info.IsRestricted.Should().BeFalse();

        await _sut.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExitSandboxAsync_WhenInSandbox_ShouldClearState()
    {
        var options = new SandboxOptions
        {
            Type = SandboxType.Process
        };

        await _sut.EnterSandboxAsync(options).ConfigureAwait(true);
        _sut.IsInSandbox.Should().BeTrue();

        await _sut.ExitSandboxAsync().ConfigureAwait(true);

        _sut.IsInSandbox.Should().BeFalse();
        _sut.CurrentSandbox.Should().BeNull();
    }

    [Fact]
    public async Task ExitSandboxAsync_WhenNotInSandbox_ShouldNotThrow()
    {
        var act = async () => await _sut.ExitSandboxAsync().ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public void ResolvePath_NotInSandbox_ShouldReturnFullPath()
    {
        var path = "relative/path/file.txt";

        var resolved = _sut.ResolvePath(path);

        resolved.Should().Be(Path.GetFullPath(path));
    }

    [Fact]
    public async Task ResolvePath_InSandboxRestricted_PathInsideSandbox_ShouldReturnFullPath()
    {
        var rootPath = $"/test/test-sandbox-{Guid.NewGuid():N}";
        var options = new SandboxOptions
        {
            Type = SandboxType.Process,
            SandboxRoot = rootPath,
            RestrictFileSystem = true
        };

        await _sut.EnterSandboxAsync(options).ConfigureAwait(true);

        var path = Path.Combine(rootPath, "subdir", "file.txt");
        var resolved = _sut.ResolvePath(path);

        resolved.Should().Be(Path.GetFullPath(path));

        await _sut.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ResolvePath_InSandboxRestricted_PathOutsideSandbox_ShouldRemapToSandboxRoot()
    {
        var rootPath = $"/test/test-sandbox-{Guid.NewGuid():N}";
        var options = new SandboxOptions
        {
            Type = SandboxType.Process,
            SandboxRoot = rootPath,
            RestrictFileSystem = true
        };

        await _sut.EnterSandboxAsync(options).ConfigureAwait(true);

        var outsidePath = "/outside/sandbox/file.txt";
        var resolved = _sut.ResolvePath(outsidePath);

        // Path.GetFullPath 在 Windows 上会将 /test 转为 d:\test，需要规范化比较
        var normalizedRoot = Path.GetFullPath(rootPath);
        resolved.Should().StartWith(normalizedRoot);
        resolved.Should().Contain("file.txt");

        await _sut.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ResolvePath_InSandboxNotRestricted_ShouldReturnFullPath()
    {
        var rootPath = $"/test/test-sandbox-{Guid.NewGuid():N}";
        var options = new SandboxOptions
        {
            Type = SandboxType.Process,
            SandboxRoot = rootPath,
            RestrictFileSystem = false,
            RestrictNetwork = false
        };

        await _sut.EnterSandboxAsync(options).ConfigureAwait(true);

        var outsidePath = "/outside/sandbox/file.txt";
        var resolved = _sut.ResolvePath(outsidePath);

        resolved.Should().Be(Path.GetFullPath(outsidePath));

        await _sut.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task EnterSandboxAsync_DockerType_ShouldSetDockerType()
    {
        var options = new SandboxOptions
        {
            Type = SandboxType.Docker
        };

        var info = await _sut.EnterSandboxAsync(options).ConfigureAwait(true);

        info.Type.Should().Be(SandboxType.Docker);

        await _sut.ExitSandboxAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task EnterSandboxAsync_BubblewrapType_ShouldSetBubblewrapType()
    {
        var options = new SandboxOptions
        {
            Type = SandboxType.Bubblewrap
        };

        var info = await _sut.EnterSandboxAsync(options).ConfigureAwait(true);

        info.Type.Should().Be(SandboxType.Bubblewrap);

        await _sut.ExitSandboxAsync().ConfigureAwait(true);
    }
}
