namespace Guard.Tests.Permission.Services;


public sealed class SoftSandboxProviderTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly SoftSandboxProvider _sut;

    public SoftSandboxProviderTests()
    {
        _sut = new SoftSandboxProvider(_fs, NullLogger<SoftSandboxProvider>.Instance);
    }

    [Fact]
    public async Task CreateSandboxAsync_ShouldReturnSandboxInfo()
    {
        var options = new SandboxOptions { Type = SandboxType.Soft };
        var info = await _sut.CreateSandboxAsync(options).ConfigureAwait(true);

        info.Should().NotBeNull();
        info.SandboxId.Should().NotBeNullOrEmpty();
        info.SandboxId.Length.Should().Be(12);
        info.Type.Should().Be(SandboxType.Soft);

        await _sut.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public async Task CreateSandboxAsync_ShouldCreateDirectory()
    {
        var options = new SandboxOptions { Type = SandboxType.Soft };
        var info = await _sut.CreateSandboxAsync(options).ConfigureAwait(true);

        _fs.DirectoryExists(info.RootPath).Should().BeTrue();

        await _sut.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public async Task CreateSandboxAsync_WithCustomRoot_ShouldUseCustomRoot()
    {
        var rootPath = $"/test/test-sandbox-{Guid.NewGuid():N}";
        var options = new SandboxOptions { Type = SandboxType.Soft, SandboxRoot = rootPath };
        var info = await _sut.CreateSandboxAsync(options).ConfigureAwait(true);

        info.RootPath.Should().Be(rootPath);

        await _sut.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public async Task CreateSandboxAsync_ShouldStoreSandboxInfo()
    {
        var options = new SandboxOptions { Type = SandboxType.Soft };
        var info = await _sut.CreateSandboxAsync(options).ConfigureAwait(true);

        var retrieved = _sut.GetSandboxInfo(info.SandboxId);
        retrieved.Should().NotBeNull();
        retrieved!.SandboxId.Should().Be(info.SandboxId);
        retrieved.RootPath.Should().NotBeNullOrEmpty();
        retrieved.EnteredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        await _sut.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public async Task IsPathInSandboxAsync_ValidPath_ShouldReturnTrue()
    {
        var options = new SandboxOptions { Type = SandboxType.Soft, RestrictFileSystem = true };
        var info = await _sut.CreateSandboxAsync(options).ConfigureAwait(true);

        var result = await _sut.IsPathInSandboxAsync(
            Path.Combine(info.RootPath, "subdir", "file.txt"), info.SandboxId).ConfigureAwait(true);

        result.Should().BeTrue();

        await _sut.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public async Task IsPathInSandboxAsync_PathOutsideSandbox_ShouldReturnFalse()
    {
        var options = new SandboxOptions { Type = SandboxType.Soft };
        var info = await _sut.CreateSandboxAsync(options).ConfigureAwait(true);

        var result = await _sut.IsPathInSandboxAsync("/completely/different/path", info.SandboxId).ConfigureAwait(true);

        result.Should().BeFalse();

        await _sut.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public async Task IsPathInSandboxAsync_InvalidSandboxId_ShouldReturnFalse()
    {
        var result = await _sut.IsPathInSandboxAsync("/some/path", "nonexistent-id").ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ResolvePath_NormalPath_ShouldResolveWithinSandbox()
    {
        var options = new SandboxOptions { Type = SandboxType.Soft, RestrictFileSystem = true };
        var info = await _sut.CreateSandboxAsync(options).ConfigureAwait(true);

        var resolved = _sut.ResolvePath("subdir/file.txt", info.SandboxId);

        resolved.Should().StartWith(Path.GetFullPath(info.RootPath));
        resolved.Should().Contain("subdir");
        resolved.Should().Contain("file.txt");

        await _sut.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public async Task ResolvePath_PathTraversal_ShouldSanitizePath()
    {
        var options = new SandboxOptions { Type = SandboxType.Soft, RestrictFileSystem = true };
        var info = await _sut.CreateSandboxAsync(options).ConfigureAwait(true);

        var resolved = _sut.ResolvePath("../../../etc/passwd", info.SandboxId);

        resolved.Should().StartWith(Path.GetFullPath(info.RootPath));
        resolved.Should().NotContain("..");

        await _sut.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);
    }

    [Fact]
    public async Task ResolvePath_InvalidSandboxId_ShouldThrowInvalidOperationException()
    {
        var act = () => _sut.ResolvePath("file.txt", "nonexistent-id");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task DestroySandboxAsync_ExistingSandbox_ShouldRemoveFromRegistry()
    {
        var options = new SandboxOptions { Type = SandboxType.Soft };
        var info = await _sut.CreateSandboxAsync(options).ConfigureAwait(true);

        await _sut.DestroySandboxAsync(info.SandboxId).ConfigureAwait(true);

        _sut.GetSandboxInfo(info.SandboxId).Should().BeNull();
    }

    [Fact]
    public async Task DestroySandboxAsync_NonExistentSandbox_ShouldNotThrow()
    {
        var act = async () => await _sut.DestroySandboxAsync("nonexistent-id").ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ActiveSandboxes_ShouldTrackAllInstances()
    {
        var options = new SandboxOptions { Type = SandboxType.Soft };
        var info1 = await _sut.CreateSandboxAsync(options).ConfigureAwait(true);
        var info2 = await _sut.CreateSandboxAsync(options).ConfigureAwait(true);

        _sut.ActiveSandboxes.Should().HaveCount(2);

        await _sut.DestroySandboxAsync(info1.SandboxId).ConfigureAwait(true);
        await _sut.DestroySandboxAsync(info2.SandboxId).ConfigureAwait(true);
    }
}
