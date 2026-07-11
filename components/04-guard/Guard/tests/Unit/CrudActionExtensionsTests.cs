namespace Host.Tests.ChatCommands;

/// <summary>
/// CrudAction 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / CrudActionConstants 常量值 / 大小写不敏感
/// 9 个枚举值 (List/Ls/Create/New/Read/Update/Delete/Rm/Remove) + 别名映射测试
/// </summary>
public sealed class CrudActionExtensionsTests
{
    // ===== ToValue 测试 =====

    [Fact]
    public void ToValue_List_Should_Return_list()
    {
        CrudAction.List.ToValue().Should().Be("list");
    }

    [Fact]
    public void ToValue_Ls_Should_Return_ls()
    {
        CrudAction.Ls.ToValue().Should().Be("ls");
    }

    [Fact]
    public void ToValue_Create_Should_Return_create()
    {
        CrudAction.Create.ToValue().Should().Be("create");
    }

    [Fact]
    public void ToValue_New_Should_Return_new()
    {
        CrudAction.New.ToValue().Should().Be("new");
    }

    [Fact]
    public void ToValue_Read_Should_Return_read()
    {
        CrudAction.Read.ToValue().Should().Be("read");
    }

    [Fact]
    public void ToValue_Update_Should_Return_update()
    {
        CrudAction.Update.ToValue().Should().Be("update");
    }

    [Fact]
    public void ToValue_Delete_Should_Return_delete()
    {
        CrudAction.Delete.ToValue().Should().Be("delete");
    }

    [Fact]
    public void ToValue_Rm_Should_Return_rm()
    {
        CrudAction.Rm.ToValue().Should().Be("rm");
    }

    [Fact]
    public void ToValue_Remove_Should_Return_remove()
    {
        CrudAction.Remove.ToValue().Should().Be("remove");
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("list", CrudAction.List)]
    [InlineData("ls", CrudAction.Ls)]
    [InlineData("create", CrudAction.Create)]
    [InlineData("new", CrudAction.New)]
    [InlineData("read", CrudAction.Read)]
    [InlineData("update", CrudAction.Update)]
    [InlineData("delete", CrudAction.Delete)]
    [InlineData("rm", CrudAction.Rm)]
    [InlineData("remove", CrudAction.Remove)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, CrudAction expected)
    {
        CrudActionExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void FromValue_Should_Be_CaseInsensitive()
    {
        CrudActionExtensions.FromValue("LIST").Should().Be(CrudAction.List);
        CrudActionExtensions.FromValue("Ls").Should().Be(CrudAction.Ls);
        CrudActionExtensions.FromValue("CREATE").Should().Be(CrudAction.Create);
        CrudActionExtensions.FromValue("New").Should().Be(CrudAction.New);
        CrudActionExtensions.FromValue("delete").Should().Be(CrudAction.Delete);
        CrudActionExtensions.FromValue("RM").Should().Be(CrudAction.Rm);
        CrudActionExtensions.FromValue("REMOVE").Should().Be(CrudAction.Remove);
    }

    [Fact]
    public void FromValue_InvalidString_Should_Return_Null()
    {
        CrudActionExtensions.FromValue("invalid").Should().BeNull();
    }

    [Fact]
    public void FromValue_EmptyString_Should_Return_Null()
    {
        CrudActionExtensions.FromValue("").Should().BeNull();
    }

    [Fact]
    public void FromValue_Null_Should_Return_Null()
    {
        CrudActionExtensions.FromValue(null).Should().BeNull();
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(CrudAction.List, true)]
    [InlineData(CrudAction.Ls, true)]
    [InlineData(CrudAction.Create, true)]
    [InlineData(CrudAction.New, true)]
    [InlineData(CrudAction.Read, true)]
    [InlineData(CrudAction.Update, true)]
    [InlineData(CrudAction.Delete, true)]
    [InlineData(CrudAction.Rm, true)]
    public void IsDefined_AllValidValues_Should_Return_True(CrudAction value, bool expected)
    {
        CrudActionExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== CrudActionConstants 测试 =====

    [Fact]
    public void Constants_Should_Match_EnumValues()
    {
        CrudActionConstants.List.Should().Be("list");
        CrudActionConstants.Ls.Should().Be("ls");
        CrudActionConstants.Create.Should().Be("create");
        CrudActionConstants.New.Should().Be("new");
        CrudActionConstants.Read.Should().Be("read");
        CrudActionConstants.Update.Should().Be("update");
        CrudActionConstants.Delete.Should().Be("delete");
        CrudActionConstants.Rm.Should().Be("rm");
    }

    // ===== 往返一致性测试 =====

    [Theory]
    [InlineData(CrudAction.List)]
    [InlineData(CrudAction.Ls)]
    [InlineData(CrudAction.Create)]
    [InlineData(CrudAction.New)]
    [InlineData(CrudAction.Read)]
    [InlineData(CrudAction.Update)]
    [InlineData(CrudAction.Delete)]
    [InlineData(CrudAction.Rm)]
    public void ToValue_FromValue_RoundTrip_Should_Be_Consistent(CrudAction value)
    {
        var str = value.ToValue();
        CrudActionExtensions.FromValue(str).Should().Be(value);
    }
}
