namespace Core.Tests.Permission;

/// <summary>
/// PathPermissionChecker 路径存在性检查单元测试 — 验证步骤8.5
/// 工作目录外读取路径不存在时直接返回 Invalid(不进 ask 面板),乱码路径同理
/// </summary>
public class PathPermissionCheckerExistenceTests
{
    private const string WorkingDir = @"D:\test\project";

    [Fact]
    public void WorkDirOutside_NonExistentPath_ReturnsInvalid()
    {
        var fs = CreateFileSystem(WorkingDir);
        var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var result = sut.CheckReadPermission(@"D:\other\nonexistent.txt");

        result.Decision.Should().Be(PermissionBehavior.Invalid);
        result.Reason.Should().Contain("路径不存在");
    }

    [Fact]
    public void WorkDirOutside_ExistingFile_ReturnsAsk()
    {
        var existingPath = @"D:\other\existing.txt";
        var fs = CreateFileSystem(WorkingDir);
        fs.Setup(x => x.FileExists(It.Is<string>(p => p.Contains("existing", StringComparison.OrdinalIgnoreCase)))).Returns(true);

        var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var result = sut.CheckReadPermission(existingPath);

        result.Decision.Should().Be(PermissionBehavior.Ask);
        result.Reason.Should().Contain("工作目录之外");
    }

    [Fact]
    public void WorkDirInside_NonExistentPath_ReturnsAllow()
    {
        var fs = CreateFileSystem(WorkingDir);
        var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var result = sut.CheckReadPermission(Path.Combine(WorkingDir, "newfile.txt"));

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    [Fact]
    public void GarbledPath_WithReplacementChar_ReturnsInvalid()
    {
        var fs = CreateFileSystem(WorkingDir);
        var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var garbledPath = @"D:\other\bad\uFFFDfile.txt".Replace("uFFFD", "\uFFFD");

        var result = sut.CheckReadPermission(garbledPath);

        result.Decision.Should().Be(PermissionBehavior.Invalid);
        result.Reason.Should().Contain("乱码字符");
    }

    [Fact]
    public void GarbledPath_WithControlChar_ReturnsInvalid()
    {
        var fs = CreateFileSystem(WorkingDir);
        var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var garbledPath = @"D:\other\bad\" + "\x01control.txt";

        var result = sut.CheckReadPermission(garbledPath);

        result.Decision.Should().Be(PermissionBehavior.Invalid);
        result.Reason.Should().Contain("乱码字符");
    }

    [Fact]
    public void UncPath_NonExistent_ReturnsAsk_Step1Priority()
    {
        var fs = CreateFileSystem(WorkingDir);
        var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var result = sut.CheckReadPermission(@"\\network\share\nonexistent.txt");

        result.Decision.Should().Be(PermissionBehavior.Ask);
        result.Reason.Should().Contain("UNC");
    }

    [Fact]
    public void DenyRule_NonExistentPath_ReturnsDeny_Step3Priority()
    {
        var fs = CreateFileSystem(WorkingDir);
        var denyRule = new PathPermissionRule
        {
            ToolType = PathPermissionToolType.Read,
            Behavior = PermissionBehavior.Deny,
            Pattern = @"D:\blocked\**",
            Source = PathPermissionRuleSource.UserSettings
        };
        var sut = new PathPermissionChecker(fs.Object, WorkingDir, rules: [denyRule]);

        var result = sut.CheckReadPermission(@"D:\blocked\nonexistent.txt");

        result.Decision.Should().Be(PermissionBehavior.Deny);
    }

    [Fact]
    public void WritePermission_NonExistentPath_DoesNotCheckExistence()
    {
        var fs = CreateFileSystem(WorkingDir);
        var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var result = sut.CheckWritePermission(@"D:\other\newfile.txt");

        result.Decision.Should().Be(PermissionBehavior.Ask);
        result.Reason.Should().NotContain("路径不存在");
    }

    [Fact]
    public void WorkDirOutside_ExistingDirectory_ReturnsAsk()
    {
        var fs = CreateFileSystem(WorkingDir);
        fs.Setup(x => x.DirectoryExists(It.Is<string>(p => p.Contains("existingdir", StringComparison.OrdinalIgnoreCase)))).Returns(true);

        var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var result = sut.CheckReadPermission(@"D:\other\existingdir");

        result.Decision.Should().Be(PermissionBehavior.Ask);
    }

    [Fact]
    public void AllowRule_NonExistentPath_ReturnsAllow_Step8Priority()
    {
        var fs = CreateFileSystem(WorkingDir);
        var allowRule = new PathPermissionRule
        {
            ToolType = PathPermissionToolType.Read,
            Behavior = PermissionBehavior.Allow,
            Pattern = @"D:\allowed\**",
            Source = PathPermissionRuleSource.UserSettings
        };
        var sut = new PathPermissionChecker(fs.Object, WorkingDir, rules: [allowRule]);

        var result = sut.CheckReadPermission(@"D:\allowed\nonexistent.txt");

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    private static Mock<IFileSystem> CreateFileSystem(string workingDir)
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(x => x.GetCurrentDirectory()).Returns(workingDir);
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(false);
        fs.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(false);
        return fs;
    }
}
