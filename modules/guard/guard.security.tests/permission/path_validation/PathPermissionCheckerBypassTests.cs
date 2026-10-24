// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace Core.Tests.Permission;

/// <summary>
/// PathPermissionChecker 路径段绕过测试 — 验证 IsPathUnderDirectory 路径段边界检查
/// <para>防护: C:\Projects\MyAppSecret 不应绕过 C:\Projects\MyApp(路径段边界)</para>
/// <para>防护: /foo/../bar 应被 Path.GetFullPath 解析后再判断</para>
/// <para>跨平台: 用预处理符号定义平台路径常量,Windows 用 D:\test\,Linux 用 /tmp/test/</para>
/// </summary>
public sealed class PathPermissionCheckerBypassTests {
#if WINDOWS
    private const string WorkingDir = @"D:\test\project";
    private const string WorkingDirParent = @"D:\test";
    private const string OtherDriveFile = @"E:\other\file.txt";
    private const string SiblingFile = @"D:\test\other\file.txt";
    private const string ExtraDir = @"D:\extra\dir";
    private const string ExtraDirPrefixFile = @"D:\extra\directory\file.txt";
#else
    private const string WorkingDir = @"/tmp/test/project";
    private const string WorkingDirParent = @"/tmp/test";
    private const string OtherDriveFile = @"/other/file.txt";
    private const string SiblingFile = @"/tmp/test/other/file.txt";
    private const string ExtraDir = @"/extra/dir";
    private const string ExtraDirPrefixFile = @"/extra/directory/file.txt";
#endif

    #region 辅助构造

    private static Mock<IFileSystem> CreateFileSystem(string workingDir) {
        var fs = new Mock<IFileSystem>();
        fs.Setup(x => x.GetCurrentDirectory()).Returns(workingDir);
        fs.Setup(x => x.FileExists(It.IsAny<string>())).Returns(false);
        fs.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(false);
        return fs;
    }

    private static PathPermissionChecker CreateSut(string workingDir = WorkingDir) {
        var fs = CreateFileSystem(workingDir);
        return new PathPermissionChecker(fs.Object, workingDir);
    }

    #endregion

    #region 路径段边界 — 相似前缀绕过防护

    /// <summary>projectSecret 不应被当作 project 的子路径</summary>
    [Fact]
    public async Task CheckReadPermission_相似前缀绕过_不当作子路径_返回Invalid() {
        await using var sut = CreateSut();

        var result = sut.CheckReadPermission(Path.Combine(WorkingDirParent, "projectSecret", "file.txt"));

        result.Decision.Should().Be(PermissionBehavior.Invalid);
        result.Reason.Should().Contain("路径不存在");
    }

    /// <summary>projectexact 不应被当作 project 的子路径(仅前缀匹配)</summary>
    [Fact]
    public async Task CheckReadPermission_前缀匹配但非路径段_返回Invalid() {
        await using var sut = CreateSut();

        var result = sut.CheckReadPermission(Path.Combine(WorkingDirParent, "projectexact"));

        result.Decision.Should().Be(PermissionBehavior.Invalid);
    }

    /// <summary>大小写不同的路径段边界 — PROJECT/file.txt 应 Allow(OrdinalIgnoreCase)</summary>
    [Fact]
    public async Task CheckReadPermission_大小写不同路径段_返回Allow() {
        await using var sut = CreateSut();

        var result = sut.CheckReadPermission(Path.Combine(WorkingDirParent, "PROJECT", "file.txt"));

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    #endregion

    #region 精确匹配与子路径

    /// <summary>精确匹配工作目录本身应 Allow</summary>
    [Fact]
    public async Task CheckReadPermission_精确匹配工作目录_返回Allow() {
        await using var sut = CreateSut();

        var result = sut.CheckReadPermission(WorkingDir);

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    /// <summary>工作目录内子路径应 Allow</summary>
    [Fact]
    public async Task CheckReadPermission_工作目录内子路径_返回Allow() {
        await using var sut = CreateSut();

        var result = sut.CheckReadPermission(Path.Combine(WorkingDir, "file.txt"));

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    /// <summary>工作目录内多层子路径应 Allow</summary>
    [Fact]
    public async Task CheckReadPermission_工作目录内多层子路径_返回Allow() {
        await using var sut = CreateSut();

        var result = sut.CheckReadPermission(Path.Combine(WorkingDir, "src", "sub", "file.cs"));

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    #endregion

    #region 路径遍历 .. 解析

    /// <summary>../other/file.txt 解析后在工作目录外 → Invalid</summary>
    [Fact]
    public async Task CheckReadPermission_父目录遍历_解析后在工作目录外_返回Invalid() {
        await using var sut = CreateSut();

        var result = sut.CheckReadPermission(Path.Combine(WorkingDir, "..", "other", "file.txt"));

        result.Decision.Should().Be(PermissionBehavior.Invalid);
    }

    /// <summary>sub/../file.txt 解析回工作目录内 → Allow</summary>
    [Fact]
    public async Task CheckReadPermission_子目录遍历回工作目录_返回Allow() {
        await using var sut = CreateSut();

        var result = sut.CheckReadPermission(Path.Combine(WorkingDir, "sub", "..", "file.txt"));

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    /// <summary>../../other 解析后在工作目录外 → Invalid</summary>
    [Fact]
    public async Task CheckReadPermission_多层遍历_解析后在工作目录外_返回Invalid() {
        await using var sut = CreateSut();

        var result = sut.CheckReadPermission(Path.Combine(WorkingDir, "..", "..", "other", "file.txt"));

        result.Decision.Should().Be(PermissionBehavior.Invalid);
    }

    /// <summary>sub/../../project/file.txt 遍历回工作目录 → Allow</summary>
    [Fact]
    public async Task CheckReadPermission_遍历回工作目录_返回Allow() {
        await using var sut = CreateSut();

        var result = sut.CheckReadPermission(Path.Combine(WorkingDir, "sub", "..", "..", "project", "file.txt"));

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    #endregion

    #region 工作目录外路径

    /// <summary>完全不同的驱动器/根路径 → Invalid(不存在)</summary>
    [Fact]
    public async Task CheckReadPermission_不同驱动器_返回Invalid() {
        await using var sut = CreateSut();

        var result = sut.CheckReadPermission(OtherDriveFile);

        result.Decision.Should().Be(PermissionBehavior.Invalid);
    }

    /// <summary>同级目录路径 → Invalid(不存在)</summary>
    [Fact]
    public async Task CheckReadPermission_同级目录_返回Invalid() {
        await using var sut = CreateSut();

        var result = sut.CheckReadPermission(SiblingFile);

        result.Decision.Should().Be(PermissionBehavior.Invalid);
    }

    #endregion

    #region 额外工作目录

    /// <summary>额外目录内路径应 Allow</summary>
    [Fact]
    public async Task CheckReadPermission_额外目录内路径_返回Allow() {
        var fs = CreateFileSystem(WorkingDir);
        await using var sut = new PathPermissionChecker(
            fs.Object,
            WorkingDir,
            additionalDirectories: [ExtraDir]);

        var result = sut.CheckReadPermission(Path.Combine(ExtraDir, "file.txt"));

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    /// <summary>额外目录相似前缀不应绕过</summary>
    [Fact]
    public async Task CheckReadPermission_额外目录相似前缀_不当作子路径_返回Invalid() {
        var fs = CreateFileSystem(WorkingDir);
        await using var sut = new PathPermissionChecker(
            fs.Object,
            WorkingDir,
            additionalDirectories: [ExtraDir]);

        var result = sut.CheckReadPermission(ExtraDirPrefixFile);

        result.Decision.Should().Be(PermissionBehavior.Invalid);
    }

    #endregion

    #region 写权限路径段绕过

    /// <summary>写权限: 相似前缀不应绕过</summary>
    [Fact]
    public async Task CheckWritePermission_相似前缀绕过_不当作子路径_返回Ask() {
        await using var sut = CreateSut();

        var result = sut.CheckWritePermission(Path.Combine(WorkingDirParent, "projectSecret", "file.txt"));

        result.Decision.Should().Be(PermissionBehavior.Ask);
    }

    /// <summary>写权限: 工作目录内子路径应 Allow</summary>
    [Fact]
    public async Task CheckWritePermission_工作目录内子路径_返回Allow() {
        await using var sut = CreateSut();

        var result = sut.CheckWritePermission(Path.Combine(WorkingDir, "file.txt"));

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    /// <summary>写权限: 父目录遍历解析后在工作目录外 → Ask</summary>
    [Fact]
    public async Task CheckWritePermission_父目录遍历_解析后在工作目录外_返回Ask() {
        await using var sut = CreateSut();

        var result = sut.CheckWritePermission(Path.Combine(WorkingDir, "..", "other", "file.txt"));

        result.Decision.Should().Be(PermissionBehavior.Ask);
    }

    #endregion
}
