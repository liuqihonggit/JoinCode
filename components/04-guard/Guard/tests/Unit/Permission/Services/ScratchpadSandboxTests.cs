namespace Guard.Tests.Permission.Services;

public sealed class ScratchpadSandboxTests
{
    private readonly Mock<IFileOperationService> _fileOpMock;
    private readonly ScratchpadSandbox _sut;

    public ScratchpadSandboxTests()
    {
        _fileOpMock = new Mock<IFileOperationService>();
        _fileOpMock.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(false);
        _fileOpMock.Setup(f => f.CreateDirectory(It.IsAny<string>()))
            .Returns(new DirectoryInfo("/tmp/jcc-sandbox"));

        _sut = new ScratchpadSandbox(_fileOpMock.Object, NullLogger<ScratchpadSandbox>.Instance);
    }

    [Fact]
    public void Constructor_NullFileOperationService_ShouldThrowArgumentNullException()
    {
        var act = () => new ScratchpadSandbox(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CreateSandboxAsync_ShouldReturnSandboxId()
    {
        var sandboxId = await _sut.CreateSandboxAsync().ConfigureAwait(true);

        sandboxId.Should().NotBeNullOrEmpty();
        sandboxId.Length.Should().Be(12);
    }

    [Fact]
    public async Task CreateSandboxAsync_ShouldCreateDirectory()
    {
        await _sut.CreateSandboxAsync().ConfigureAwait(true);

        _fileOpMock.Verify(f => f.CreateDirectory(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CreateSandboxAsync_ExistingDirectory_ShouldNotCreateDirectory()
    {
        _fileOpMock.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);

        await _sut.CreateSandboxAsync().ConfigureAwait(true);

        _fileOpMock.Verify(f => f.CreateDirectory(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateSandboxAsync_WithBasePath_ShouldUseBasePath()
    {
        var basePath = "/custom/base";
        var sandboxId = await _sut.CreateSandboxAsync(basePath).ConfigureAwait(true);
        var info = _sut.GetSandboxInfo(sandboxId);

        info.RootPath.Should().Contain("custom");
        info.RootPath.Should().Contain("base");
        info.RootPath.Should().Contain(sandboxId);
    }

    [Fact]
    public async Task CreateSandboxAsync_ShouldStoreSandboxInfo()
    {
        var sandboxId = await _sut.CreateSandboxAsync().ConfigureAwait(true);
        var info = _sut.GetSandboxInfo(sandboxId);

        info.Should().NotBeNull();
        info.SandboxId.Should().Be(sandboxId);
        info.RootPath.Should().NotBeNullOrEmpty();
        info.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IsPathInSandboxAsync_ValidPath_ShouldReturnTrue()
    {
        var sandboxId = await _sut.CreateSandboxAsync().ConfigureAwait(true);
        var info = _sut.GetSandboxInfo(sandboxId);

        var result = await _sut.IsPathInSandboxAsync(
            Path.Combine(info.RootPath, "subdir", "file.txt"), sandboxId).ConfigureAwait(true);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsPathInSandboxAsync_PathOutsideSandbox_ShouldReturnFalse()
    {
        var sandboxId = await _sut.CreateSandboxAsync().ConfigureAwait(true);

        var result = await _sut.IsPathInSandboxAsync("/completely/different/path", sandboxId).ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsPathInSandboxAsync_PathTraversal_ShouldReturnFalse()
    {
        var sandboxId = await _sut.CreateSandboxAsync().ConfigureAwait(true);
        var info = _sut.GetSandboxInfo(sandboxId);

        var traversalPath = Path.Combine(info.RootPath, "..", "..", "etc", "passwd");
        var result = await _sut.IsPathInSandboxAsync(traversalPath, sandboxId).ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsPathInSandboxAsync_InvalidSandboxId_ShouldReturnFalse()
    {
        var result = await _sut.IsPathInSandboxAsync("/some/path", "nonexistent-id").ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveSandboxPathAsync_NormalPath_ShouldResolveWithinSandbox()
    {
        var sandboxId = await _sut.CreateSandboxAsync().ConfigureAwait(true);
        var info = _sut.GetSandboxInfo(sandboxId);

        var resolved = await _sut.ResolveSandboxPathAsync("subdir/file.txt", sandboxId).ConfigureAwait(true);

        resolved.Should().StartWith(info.RootPath);
        resolved.Should().Contain("subdir");
        resolved.Should().Contain("file.txt");
    }

    [Fact]
    public async Task ResolveSandboxPathAsync_PathTraversal_ShouldSanitizePath()
    {
        var sandboxId = await _sut.CreateSandboxAsync().ConfigureAwait(true);
        var info = _sut.GetSandboxInfo(sandboxId);

        var resolved = await _sut.ResolveSandboxPathAsync("../../../etc/passwd", sandboxId).ConfigureAwait(true);

        resolved.Should().StartWith(info.RootPath);
        resolved.Should().NotContain("..");
    }

    [Fact]
    public async Task ResolveSandboxPathAsync_DotSegments_ShouldBeRemoved()
    {
        var sandboxId = await _sut.CreateSandboxAsync().ConfigureAwait(true);
        var info = _sut.GetSandboxInfo(sandboxId);

        var resolved = await _sut.ResolveSandboxPathAsync("./subdir/./file.txt", sandboxId).ConfigureAwait(true);

        resolved.Should().StartWith(info.RootPath);
        resolved.Should().Contain("subdir");
        resolved.Should().Contain("file.txt");
    }

    [Fact]
    public async Task ResolveSandboxPathAsync_InvalidSandboxId_ShouldThrowInvalidOperationException()
    {
        var act = async () => await _sut.ResolveSandboxPathAsync("file.txt", "nonexistent-id").ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task DestroySandboxAsync_ExistingSandbox_ShouldRemoveFromRegistry()
    {
        var sandboxId = await _sut.CreateSandboxAsync().ConfigureAwait(true);

        _fileOpMock.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
        _fileOpMock.Setup(f => f.ListDirectoryAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DirectoryListResult.SuccessResult("/sandbox", [], []));

        await _sut.DestroySandboxAsync(sandboxId).ConfigureAwait(true);

        var act = () => _sut.GetSandboxInfo(sandboxId);
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public async Task DestroySandboxAsync_NonExistentSandbox_ShouldNotThrow()
    {
        var act = async () => await _sut.DestroySandboxAsync("nonexistent-id").ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public void GetSandboxInfo_NonExistentSandbox_ShouldThrowKeyNotFoundException()
    {
        var act = () => _sut.GetSandboxInfo("nonexistent-id");

        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public async Task DisposeAsync_ShouldDestroyAllSandboxes()
    {
        var id1 = await _sut.CreateSandboxAsync().ConfigureAwait(true);
        var id2 = await _sut.CreateSandboxAsync().ConfigureAwait(true);

        _fileOpMock.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
        _fileOpMock.Setup(f => f.ListDirectoryAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DirectoryListResult.SuccessResult("/sandbox", [], []));

        await _sut.DisposeAsync().ConfigureAwait(true);

        var act1 = () => _sut.GetSandboxInfo(id1);
        var act2 = () => _sut.GetSandboxInfo(id2);

        act1.Should().Throw<KeyNotFoundException>();
        act2.Should().Throw<KeyNotFoundException>();
    }
}
