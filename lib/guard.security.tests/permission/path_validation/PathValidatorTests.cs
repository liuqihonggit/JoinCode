namespace Core.Tests.Permission;

/// <summary>
/// PathValidator 单元测试 — 覆盖正常路径、null/空、路径遍历、绝对路径、OS 分隔符、危险系统路径、UNC
/// <para>PathValidator 检查 PathEscapePatterns(.. ~ /etc/ C:\Windows \\ 等)和 DangerousPathPrefixes(具体系统目录前缀)。</para>
/// </summary>
public sealed class PathValidatorTests {
    private readonly PathValidator _sut = new();

    #region ValidatePaths — workingDirectory 守卫

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidatePaths_空workingDirectory_返回Invalid(string? workingDirectory) {
        var cmd = ShellCommand.Parse("cat file.txt");

        var result = _sut.ValidatePaths(cmd, workingDirectory!);

        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("Working directory");
    }

    #endregion

    #region ValidatePaths — 正常路径

    [Fact]
    public void ValidatePaths_工作区内相对文件_返回Valid() {
        var cmd = ShellCommand.Parse("cat file.txt");

        var result = _sut.ValidatePaths(cmd, @"D:\test\project");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidatePaths_无引用路径_返回Valid() {
        var cmd = ShellCommand.Parse("ls -la");

        var result = _sut.ValidatePaths(cmd, @"D:\test\project");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidatePaths_工作区内子目录相对路径_返回Valid() {
        var cmd = ShellCommand.Parse("cat src/main.cs");

        var result = _sut.ValidatePaths(cmd, @"D:\test\project");

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ValidatePaths — 路径遍历攻击

    [Fact]
    public void ValidatePaths_父目录遍历_返回Invalid() {
        var cmd = ShellCommand.Parse("cat ../secret.txt");

        var result = _sut.ValidatePaths(cmd, @"D:\test\project");

        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("escape pattern");
    }

    [Fact]
    public void ValidatePaths_多层父目录遍历_返回Invalid() {
        var cmd = ShellCommand.Parse("cat ../../etc/passwd");

        var result = _sut.ValidatePaths(cmd, @"D:\test\project");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidatePaths_家目录引用_返回Invalid() {
        var cmd = ShellCommand.Parse("cat ~/secret.txt");

        var result = _sut.ValidatePaths(cmd, @"D:\test\project");

        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("escape pattern");
    }

    #endregion

    #region ValidatePaths — 危险系统路径

    [Fact]
    public void ValidatePaths_etc系统路径_返回Invalid() {
        var cmd = ShellCommand.Parse("cat /etc/passwd");

        var result = _sut.ValidatePaths(cmd, @"/home/user/project");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidatePaths_Windows系统目录_返回Invalid() {
        var cmd = ShellCommand.Parse("type C:\\Windows\\system32\\config.sys");

        var result = _sut.ValidatePaths(cmd, @"D:\test\project");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidatePaths_工作区内绝对路径_返回Valid() {
        var cmd = ShellCommand.Parse("type D:\\test\\project\\file.txt");

        var result = _sut.ValidatePaths(cmd, @"D:\test\project");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidatePaths_工作区外绝对路径_返回Invalid() {
        var cmd = ShellCommand.Parse("type D:\\other\\secret.txt");

        var result = _sut.ValidatePaths(cmd, @"D:\test\project");

        result.IsValid.Should().BeFalse();
        result.Message.Should().Contain("outside the working directory");
    }

    #endregion

    #region ValidatePaths — UNC 路径

    [Fact]
    public void ValidatePaths_UNC路径_返回Invalid() {
        var cmd = ShellCommand.Parse(@"cat \\server\share\secret.txt");

        var result = _sut.ValidatePaths(cmd, @"D:\test\project");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidatePaths_双斜杠网络路径_返回Invalid() {
        var cmd = ShellCommand.Parse("cat //server/share/secret.txt");

        var result = _sut.ValidatePaths(cmd, @"/home/user/project");

        result.IsValid.Should().BeFalse();
    }

    #endregion

    #region IsPathWithinWorkspace — null/空守卫

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsPathWithinWorkspace_空path_返回False(string? path) {
        var result = _sut.IsPathWithinWorkspace(path!, @"D:\test\project");

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsPathWithinWorkspace_空workingDirectory_返回False(string? workingDirectory) {
        var result = _sut.IsPathWithinWorkspace("file.txt", workingDirectory!);

        result.Should().BeFalse();
    }

    #endregion

    #region IsPathWithinWorkspace — 正常判断

    [Fact]
    public void IsPathWithinWorkspace_工作区内相对路径_返回True() {
        var result = _sut.IsPathWithinWorkspace("file.txt", @"D:\test\project");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPathWithinWorkspace_工作区内子目录_返回True() {
        var result = _sut.IsPathWithinWorkspace("src/main.cs", @"D:\test\project");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPathWithinWorkspace_工作区外绝对路径_返回False() {
        var result = _sut.IsPathWithinWorkspace(@"E:\other\file.txt", @"D:\test\project");

        result.Should().BeFalse();
    }

    #endregion

    #region IsPathWithinWorkspace — 不同 OS 路径分隔符

    [Fact]
    public void IsPathWithinWorkspace_正斜杠分隔符_返回True() {
        var result = _sut.IsPathWithinWorkspace("src/sub/file.txt", @"D:\test\project");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPathWithinWorkspace_混合分隔符_返回True() {
        var result = _sut.IsPathWithinWorkspace(@"src/sub\file.txt", @"D:\test\project");

        result.Should().BeTrue();
    }

    #endregion
}
