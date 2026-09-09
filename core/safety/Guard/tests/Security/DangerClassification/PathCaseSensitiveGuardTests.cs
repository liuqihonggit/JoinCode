namespace Guard.Security.Tests;

/// <summary>
/// PathCaseSensitiveGuard 单元测试 — 验证删除命令的路径大小写守卫
/// 防御 Windows 大小写不敏感误删(如 rm src/ 误删 SRC/)
/// </summary>
public class PathCaseSensitiveGuardTests
{
    private readonly PathCaseSensitiveGuard _guard = new();

    [Fact]
    public void Delete_Command_With_Case_Mismatch_Should_Block_And_Suggest_RealPath()
    {
        var resolver = new Mock<IRealPathResolver>();
        resolver.Setup(r => r.GetRealPath(It.IsAny<string>())).Returns("D:\\proj\\SRC");
        var command = ShellCommand.Parse("rm src/");

        var result = _guard.Check(command, resolver.Object);

        result.Blocked.Should().BeTrue();
        result.SuggestedPath.Should().Be("D:\\proj\\SRC");
        result.Reason.Should().Contain("SRC");
        result.Reason.Should().Contain("大小写");
    }

    [Fact]
    public void Delete_Command_With_Case_Match_Should_Pass()
    {
        var resolver = new Mock<IRealPathResolver>();
        resolver.Setup(r => r.GetRealPath(It.IsAny<string>())).Returns("D:\\proj\\src");
        var command = ShellCommand.Parse("rm src/");

        var result = _guard.Check(command, resolver.Object);

        result.Blocked.Should().BeFalse();
    }

    [Fact]
    public void Non_Delete_Command_Should_Pass()
    {
        var resolver = new Mock<IRealPathResolver>();
        var command = ShellCommand.Parse("ls src/");

        var result = _guard.Check(command, resolver.Object);

        result.Blocked.Should().BeFalse();
    }

    [Fact]
    public void Delete_Command_Path_Not_Exist_Should_Pass()
    {
        var resolver = new Mock<IRealPathResolver>();
        resolver.Setup(r => r.GetRealPath(It.IsAny<string>())).Returns((string?)null);
        var command = ShellCommand.Parse("rm nonexistent/");

        var result = _guard.Check(command, resolver.Object);

        result.Blocked.Should().BeFalse();
    }

    [Theory]
    [InlineData("rm")]
    [InlineData("del")]
    [InlineData("erase")]
    [InlineData("rmdir")]
    [InlineData("rd")]
    [InlineData("Remove-Item")]
    public void All_Delete_Commands_With_Case_Mismatch_Should_Block(string deleteCmd)
    {
        var resolver = new Mock<IRealPathResolver>();
        resolver.Setup(r => r.GetRealPath(It.IsAny<string>())).Returns("D:\\proj\\SRC");
        var command = ShellCommand.Parse($"{deleteCmd} src/");

        var result = _guard.Check(command, resolver.Object);

        result.Blocked.Should().BeTrue();
    }

    [Fact]
    public void Delete_Command_Without_Path_Should_Pass()
    {
        var resolver = new Mock<IRealPathResolver>();
        var command = ShellCommand.Parse("rm");

        var result = _guard.Check(command, resolver.Object);

        result.Blocked.Should().BeFalse();
    }
}
