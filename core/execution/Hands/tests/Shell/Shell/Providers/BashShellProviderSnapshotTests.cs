namespace Hands.Tests.Shell.Providers;

[Trait("Category", "Unit")]
public sealed class BashShellProviderSnapshotTests
{
    private static readonly string CurrentDir = Environment.CurrentDirectory;

    [Fact]
    public void Dispose_WithNoSnapshot_ShouldNotThrow()
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(x => x.GetCurrentDirectory()).Returns(CurrentDir);
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);

        var sut = new BashShellProvider(fs.Object, "cmd.exe", NullLogger.Instance);

        var act = () => sut.Dispose();

        act.Should().NotThrow();
    }

    [Fact(Skip = "Requires bash.exe in CI environment; snapshot creation calls ExecuteShellCommand which cannot be mocked")]
    public void Dispose_ShouldDeleteCurrentSnapshotFile()
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(x => x.GetCurrentDirectory()).Returns(CurrentDir);
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
        fs.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(true);
        fs.Setup(x => x.EnumerateFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SearchOption>()))
            .Returns([]);

        var sut = new BashShellProvider(fs.Object, "bash.exe", NullLogger.Instance);

        sut.Dispose();

        fs.Verify(x => x.DeleteFile(It.Is<string>(p => p.Contains("snapshot-bash"))), Times.AtLeastOnce());
    }

    [Fact]
    public void Constructor_WithCmdExe_ShouldNotCreateSnapshot()
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(x => x.GetCurrentDirectory()).Returns(CurrentDir);
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);

        var sut = new BashShellProvider(fs.Object, "cmd.exe", NullLogger.Instance);

        fs.Verify(x => x.CreateDirectory(It.IsAny<string>()), Times.Never());
        fs.Verify(x => x.WriteAllText(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
    }
}
