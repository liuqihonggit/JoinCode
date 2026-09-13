namespace Abs.Tests.Utils;

/// <summary>
/// EnvironmentSnapshot 单元测试 — 验证 CaptureQuick 快照采集与 FormatReadable 格式化输出
/// </summary>
public sealed class EnvironmentSnapshotTest
{
    // === CaptureQuick ===

    [Fact]
    public void CaptureQuick_NoFileSystem_SetsWorkingDirectoryAndNoGitRepo()
    {
        var snapshot = EnvironmentSnapshot.CaptureQuick(fs: null, workingDirectory: "/tmp/test");

        snapshot.WorkingDirectory.Should().Be("/tmp/test");
        snapshot.IsGitRepo.Should().BeFalse();
    }

    [Fact]
    public void CaptureQuick_NullWorkingDirectory_UsesCurrentDirectory()
    {
        var snapshot = EnvironmentSnapshot.CaptureQuick(fs: null, workingDirectory: null);

        snapshot.WorkingDirectory.Should().Be(Environment.CurrentDirectory);
        snapshot.IsGitRepo.Should().BeFalse();
    }

    [Fact]
    public void CaptureQuick_DetectGitFalse_NeverChecksGitRepo()
    {
        var fs = new Mock<IFileSystem>();

        var snapshot = EnvironmentSnapshot.CaptureQuick(fs.Object, workingDirectory: "/repo", detectGit: false);

        snapshot.IsGitRepo.Should().BeFalse();
        fs.Verify(x => x.DirectoryExists(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CaptureQuick_GitDirectoryExists_SetsIsGitRepoTrue()
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(x => x.CombinePath(It.IsAny<string>(), ".git")).Returns("/repo/.git");
        fs.Setup(x => x.DirectoryExists("/repo/.git")).Returns(true);

        var snapshot = EnvironmentSnapshot.CaptureQuick(fs.Object, workingDirectory: "/repo", detectGit: true);

        snapshot.IsGitRepo.Should().BeTrue();
        snapshot.WorkingDirectory.Should().Be("/repo");
    }

    [Fact]
    public void CaptureQuick_GitDirectoryNotExists_SetsIsGitRepoFalse()
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(x => x.CombinePath(It.IsAny<string>(), ".git")).Returns("/repo/.git");
        fs.Setup(x => x.DirectoryExists("/repo/.git")).Returns(false);

        var snapshot = EnvironmentSnapshot.CaptureQuick(fs.Object, workingDirectory: "/repo", detectGit: true);

        snapshot.IsGitRepo.Should().BeFalse();
    }

    [Fact]
    public void CaptureQuick_SetsConsoleEncodingFromCurrentConsole()
    {
        var snapshot = EnvironmentSnapshot.CaptureQuick(fs: null, workingDirectory: "/tmp");

        snapshot.ConsoleEncoding.Should().Be(Console.OutputEncoding?.WebName);
    }

    [Fact]
    public void CaptureQuick_PopulatesRuntimeInfo()
    {
        var snapshot = EnvironmentSnapshot.CaptureQuick(fs: null, workingDirectory: "/tmp");

        snapshot.OsDescription.Should().NotBeNullOrEmpty();
        snapshot.FrameworkDescription.Should().NotBeNullOrEmpty();
        snapshot.RuntimeVersion.Should().NotBeNullOrEmpty();
        snapshot.ProcessArchitecture.Should().NotBeNullOrEmpty();
        snapshot.OsArchitecture.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CaptureQuick_TimestampIsUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var snapshot = EnvironmentSnapshot.CaptureQuick(fs: null, workingDirectory: "/tmp");
        var after = DateTime.UtcNow.AddSeconds(1);

        snapshot.Timestamp.Should().BeOnOrAfter(before);
        snapshot.Timestamp.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void CaptureQuick_DefaultDevToolsIsEmpty()
    {
        var snapshot = EnvironmentSnapshot.CaptureQuick(fs: null, workingDirectory: "/tmp");

        snapshot.DevTools.Should().BeEmpty();
    }

    // === FormatReadable ===

    [Fact]
    public void FormatReadable_IncludesAllCoreFields()
    {
        var snapshot = new EnvironmentSnapshot
        {
            WorkingDirectory = "/repo",
            IsGitRepo = true,
            ConsoleEncoding = "utf-8",
        };

        var output = snapshot.FormatReadable();

        output.Should().Contain("OS:");
        output.Should().Contain("架构:");
        output.Should().Contain("运行时:");
        output.Should().Contain("工作目录: /repo");
        output.Should().Contain("Git仓库: 是");
        output.Should().Contain("控制台编码: utf-8");
    }

    [Fact]
    public void FormatReadable_NoWorkingDirectory_OmitsWorkDirSection()
    {
        var snapshot = new EnvironmentSnapshot
        {
            WorkingDirectory = null,
        };

        var output = snapshot.FormatReadable();

        output.Should().NotContain("工作目录");
        output.Should().NotContain("Git仓库");
    }

    [Fact]
    public void FormatReadable_IsGitRepoFalse_ShowsNo()
    {
        var snapshot = new EnvironmentSnapshot
        {
            WorkingDirectory = "/tmp",
            IsGitRepo = false,
        };

        var output = snapshot.FormatReadable();

        output.Should().Contain("Git仓库: 否");
    }

    [Fact]
    public void FormatReadable_NonUtf8Encoding_AnnotatesWarning()
    {
        var snapshot = new EnvironmentSnapshot
        {
            WorkingDirectory = "/tmp",
            ConsoleEncoding = "gbk",
        };

        var output = snapshot.FormatReadable();

        output.Should().Contain("控制台编码: gbk (非UTF-8)");
    }

    [Fact]
    public void FormatReadable_Utf8Encoding_NoWarningAnnotation()
    {
        var snapshot = new EnvironmentSnapshot
        {
            WorkingDirectory = "/tmp",
            ConsoleEncoding = "utf-8",
        };

        var output = snapshot.FormatReadable();

        output.Should().Contain("控制台编码: utf-8");
        output.Should().NotContain("非UTF-8");
    }

    [Fact]
    public void FormatReadable_NullConsoleEncoding_OmitsEncodingLine()
    {
        var snapshot = new EnvironmentSnapshot
        {
            WorkingDirectory = "/tmp",
            ConsoleEncoding = null,
        };

        var output = snapshot.FormatReadable();

        output.Should().NotContain("控制台编码");
    }

    [Fact]
    public void FormatReadable_WithDevTools_ListsToolNamesAndVersions()
    {
        var snapshot = new EnvironmentSnapshot
        {
            WorkingDirectory = "/tmp",
            DevTools = new Dictionary<string, string?>
            {
                ["node"] = "v20.0.0",
                ["python"] = null,
            }.ToFrozenDictionary(),
        };

        var output = snapshot.FormatReadable();

        output.Should().Contain("开发工具:");
        output.Should().Contain("node v20.0.0");
        output.Should().Contain("python");
    }

    [Fact]
    public void FormatReadable_EmptyDevTools_OmitsDevToolsLine()
    {
        var snapshot = new EnvironmentSnapshot
        {
            WorkingDirectory = "/tmp",
        };

        var output = snapshot.FormatReadable();

        output.Should().NotContain("开发工具:");
    }

    [Fact]
    public void FormatReadable_TimestampFormattedAsUtc()
    {
        var snapshot = new EnvironmentSnapshot
        {
            Timestamp = new DateTime(2026, 1, 15, 10, 30, 45, DateTimeKind.Utc),
        };

        var output = snapshot.FormatReadable();

        output.Should().Contain("2026-01-15 10:30:45");
        output.Should().Contain("UTC");
    }
}
