namespace Host.Tests.ChatCommands;

/// <summary>
/// PermissionsAction 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / PermissionsActionConstants 常量值
/// 6 个枚举值(add/clear/workspace/dirs/directories/show)
/// </summary>
public sealed class PermissionsActionExtensionsTests
{
    // ===== ToValue 测试 =====

    [Theory]
    [InlineData(PermissionsAction.Add, "add")]
    [InlineData(PermissionsAction.Clear, "clear")]
    [InlineData(PermissionsAction.Workspace, "workspace")]
    [InlineData(PermissionsAction.Dirs, "dirs")]
    [InlineData(PermissionsAction.Directories, "directories")]
    [InlineData(PermissionsAction.Show, "show")]
    public void ToValue_Should_Return_CorrectString(PermissionsAction value, string expected)
    {
        value.ToValue().Should().Be(expected);
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("add", PermissionsAction.Add)]
    [InlineData("clear", PermissionsAction.Clear)]
    [InlineData("workspace", PermissionsAction.Workspace)]
    [InlineData("dirs", PermissionsAction.Dirs)]
    [InlineData("directories", PermissionsAction.Directories)]
    [InlineData("show", PermissionsAction.Show)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, PermissionsAction expected)
    {
        PermissionsActionExtensions.FromValue(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("ADD")]
    [InlineData("Clear")]
    [InlineData("WORKSPACE")]
    [InlineData("Dirs")]
    [InlineData("DIRECTORIES")]
    [InlineData("Show")]
    public void FromValue_CaseInsensitive_Should_Return_CorrectEnum(string input)
    {
        PermissionsActionExtensions.FromValue(input).Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("list")]
    [InlineData("remove")]
    [InlineData("delete")]
    [InlineData("rm")]
    [InlineData("create")]
    [InlineData("new")]
    [InlineData("adding")]
    [InlineData("cleared")]
    public void FromValue_InvalidString_Should_Return_Null(string? input)
    {
        PermissionsActionExtensions.FromValue(input).Should().BeNull();
    }

    // ===== RoundTrip 测试 =====

    [Theory]
    [InlineData(PermissionsAction.Add)]
    [InlineData(PermissionsAction.Clear)]
    [InlineData(PermissionsAction.Workspace)]
    [InlineData(PermissionsAction.Dirs)]
    [InlineData(PermissionsAction.Directories)]
    [InlineData(PermissionsAction.Show)]
    public void RoundTrip_ToValue_ThenFromValue_Should_Return_Same(PermissionsAction value)
    {
        var s = value.ToValue();
        PermissionsActionExtensions.FromValue(s).Should().Be(value);
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(PermissionsAction.Add, true)]
    [InlineData(PermissionsAction.Clear, true)]
    [InlineData(PermissionsAction.Workspace, true)]
    [InlineData(PermissionsAction.Dirs, true)]
    [InlineData(PermissionsAction.Directories, true)]
    [InlineData(PermissionsAction.Show, true)]
    public void IsDefined_KnownValue_Should_Return_True(PermissionsAction value, bool expected)
    {
        PermissionsActionExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== Constants 测试 =====

    [Fact]
    public void Constants_All_Should_Match_EnumValues()
    {
        PermissionsActionConstants.Add.Should().Be("add");
        PermissionsActionConstants.Clear.Should().Be("clear");
        PermissionsActionConstants.Workspace.Should().Be("workspace");
        PermissionsActionConstants.Dirs.Should().Be("dirs");
        PermissionsActionConstants.Directories.Should().Be("directories");
        PermissionsActionConstants.Show.Should().Be("show");
    }

    // ===== 枚举值数量验证 =====

    [Fact]
    public void AllValues_Should_Be_6()
    {
        var values = Enum.GetValues<PermissionsAction>();
        values.Should().HaveCount(6);
    }

    // ===== 与 CrudAction 边界值不冲突 =====

    [Fact]
    public void FromValue_CrudActionValues_Should_Return_Null()
    {
        // list/ls/create/new/delete/rm/remove 是 CrudAction 范围
        PermissionsActionExtensions.FromValue("list").Should().BeNull();
        PermissionsActionExtensions.FromValue("ls").Should().BeNull();
        PermissionsActionExtensions.FromValue("create").Should().BeNull();
        PermissionsActionExtensions.FromValue("new").Should().BeNull();
        PermissionsActionExtensions.FromValue("delete").Should().BeNull();
        PermissionsActionExtensions.FromValue("rm").Should().BeNull();
        PermissionsActionExtensions.FromValue("remove").Should().BeNull();
    }

    // ===== 别名验证 =====

    [Fact]
    public void FromValue_DirsAndDirectories_Should_Be_DifferentEnumValues()
    {
        // dirs/directories 是 workspace 的别名,但作为独立枚举值保留(不同 case 分支可能需要区分)
        PermissionsActionExtensions.FromValue("dirs").Should().Be(PermissionsAction.Dirs);
        PermissionsActionExtensions.FromValue("directories").Should().Be(PermissionsAction.Directories);
        PermissionsActionExtensions.FromValue("workspace").Should().Be(PermissionsAction.Workspace);
    }
}
