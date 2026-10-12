namespace Core.Tests.Permission;

/// <summary>
/// PathPermissionChecker 路径存在性检查单元测试 — 验证步骤8.5
/// 工作目录外读取路径不存在时直接返回 Invalid(不进 ask 面板),乱码路径同理
/// <para>跨平台: 用预处理符号定义平台路径常量,Windows 用 D:\test\,Linux 用 /tmp/test/</para>
/// </summary>
public class PathPermissionCheckerExistenceTests {
#if WINDOWS
    private const string WorkingDir = @"D:\test\project";
    private const string OtherDir = @"D:\other";
    private const string BlockedDir = @"D:\blocked";
    private const string AllowedDir = @"D:\allowed";
#else
    private const string WorkingDir = @"/tmp/test/project";
    private const string OtherDir = @"/other";
    private const string BlockedDir = @"/blocked";
    private const string AllowedDir = @"/allowed";
#endif

    [Fact]
    public async Task WorkDirOutside_NonExistentPath_ReturnsInvalid() {
        var fs = CreateFileSystem(WorkingDir);
        await using var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var result = sut.CheckReadPermission(Path.Combine(OtherDir, "nonexistent.txt"));

        result.Decision.Should().Be(PermissionBehavior.Invalid);
        result.Reason.Should().Contain("路径不存在");
    }

    [Fact]
    public async Task WorkDirOutside_ExistingFile_ReturnsAsk() {
        var existingPath = Path.Combine(OtherDir, "existing.txt");
        var fs = CreateFileSystem(WorkingDir);
        fs.Setup(x => x.FileExists(It.Is<string>(p => p.Contains("existing", StringComparison.OrdinalIgnoreCase)))).Returns(true);
        await using var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var result = sut.CheckReadPermission(existingPath);

        result.Decision.Should().Be(PermissionBehavior.Ask);
        result.Reason.Should().Contain("工作目录之外");
    }

    [Fact]
    public async Task WorkDirInside_NonExistentPath_ReturnsAllow() {
        var fs = CreateFileSystem(WorkingDir);
        await using var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var result = sut.CheckReadPermission(Path.Combine(WorkingDir, "newfile.txt"));

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    [Fact]
    public async Task GarbledPath_WithReplacementChar_ReturnsInvalid() {
        var fs = CreateFileSystem(WorkingDir);
        await using var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var garbledPath = Path.Combine(OtherDir, "bad", "\uFFFDfile.txt");

        var result = sut.CheckReadPermission(garbledPath);

        result.Decision.Should().Be(PermissionBehavior.Invalid);
        result.Reason.Should().Contain("乱码字符");
    }

    [Fact]
    public async Task GarbledPath_WithControlChar_ReturnsInvalid() {
        var fs = CreateFileSystem(WorkingDir);
        await using var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var garbledPath = Path.Combine(OtherDir, "bad", "\x01control.txt");

        var result = sut.CheckReadPermission(garbledPath);

        result.Decision.Should().Be(PermissionBehavior.Invalid);
        result.Reason.Should().Contain("乱码字符");
    }

#if WINDOWS
    [Fact]
    public async Task UncPath_NonExistent_ReturnsAsk_Step1Priority() {
        var fs = CreateFileSystem(WorkingDir);
        await using var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var result = sut.CheckReadPermission(@"\\network\share\nonexistent.txt");

        result.Decision.Should().Be(PermissionBehavior.Ask);
        result.Reason.Should().Contain("UNC");
    }
#endif

    [Fact]
    public async Task DenyRule_NonExistentPath_ReturnsDeny_Step3Priority() {
        var fs = CreateFileSystem(WorkingDir);
        var denyRule = new PathPermissionRule {
            ToolType = PathPermissionToolType.Read,
            Behavior = PermissionBehavior.Deny,
            Pattern = Path.Combine(BlockedDir, "**"),
            Source = PathPermissionRuleSource.UserSettings
        };
        await using var sut = new PathPermissionChecker(fs.Object, WorkingDir, rules: [denyRule]);

        var result = sut.CheckReadPermission(Path.Combine(BlockedDir, "nonexistent.txt"));

        result.Decision.Should().Be(PermissionBehavior.Deny);
    }

    [Fact]
    public async Task WritePermission_NonExistentPath_DoesNotCheckExistence() {
        var fs = CreateFileSystem(WorkingDir);
        await using var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var result = sut.CheckWritePermission(Path.Combine(OtherDir, "newfile.txt"));

        result.Decision.Should().Be(PermissionBehavior.Ask);
        result.Reason.Should().NotContain("路径不存在");
    }

    [Fact]
    public async Task WorkDirOutside_ExistingDirectory_ReturnsAsk() {
        var fs = CreateFileSystem(WorkingDir);
        fs.Setup(x => x.DirectoryExists(It.Is<string>(p => p.Contains("existingdir", StringComparison.OrdinalIgnoreCase)))).Returns(true);
        await using var sut = new PathPermissionChecker(fs.Object, WorkingDir);

        var result = sut.CheckReadPermission(Path.Combine(OtherDir, "existingdir"));

        result.Decision.Should().Be(PermissionBehavior.Ask);
    }

    [Fact]
    public async Task AllowRule_NonExistentPath_ReturnsAllow_Step8Priority() {
        var fs = CreateFileSystem(WorkingDir);
        var allowRule = new PathPermissionRule {
            ToolType = PathPermissionToolType.Read,
            Behavior = PermissionBehavior.Allow,
            Pattern = Path.Combine(AllowedDir, "**"),
            Source = PathPermissionRuleSource.UserSettings
        };
        await using var sut = new PathPermissionChecker(fs.Object, WorkingDir, rules: [allowRule]);

        var result = sut.CheckReadPermission(Path.Combine(AllowedDir, "nonexistent.txt"));

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    private static Mock<IFileSystem> CreateFileSystem(string workingDir) {
        var fs = new Mock<IFileSystem>();
        fs.Setup(x => x.GetCurrentDirectory()).Returns(workingDir);
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(false);
        fs.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(false);
        return fs;
    }
}
