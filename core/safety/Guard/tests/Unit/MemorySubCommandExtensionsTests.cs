namespace Host.Tests.ChatCommands;

/// <summary>
/// MemorySubCommand 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / MemorySubCommandConstants 常量值
/// </summary>
public sealed class MemorySubCommandExtensionsTests
{
    // ===== ToValue 测试 =====

    [Fact]
    public void ToValue_Edit_Should_Return_edit()
    {
        MemorySubCommand.Edit.ToValue().Should().Be("edit");
    }

    [Fact]
    public void ToValue_Open_Should_Return_open()
    {
        MemorySubCommand.Open.ToValue().Should().Be("open");
    }

    [Fact]
    public void ToValue_Add_Should_Return_add()
    {
        MemorySubCommand.Add.ToValue().Should().Be("add");
    }

    [Fact]
    public void ToValue_Search_Should_Return_search()
    {
        MemorySubCommand.Search.ToValue().Should().Be("search");
    }

    [Fact]
    public void ToValue_Db_Should_Return_db()
    {
        MemorySubCommand.Db.ToValue().Should().Be("db");
    }

    [Fact]
    public void ToValue_Stats_Should_Return_stats()
    {
        MemorySubCommand.Stats.ToValue().Should().Be("stats");
    }

    [Fact]
    public void ToValue_Health_Should_Return_health()
    {
        MemorySubCommand.Health.ToValue().Should().Be("health");
    }

    [Fact]
    public void ToValue_Cleanup_Should_Return_cleanup()
    {
        MemorySubCommand.Cleanup.ToValue().Should().Be("cleanup");
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("edit", MemorySubCommand.Edit)]
    [InlineData("open", MemorySubCommand.Open)]
    [InlineData("add", MemorySubCommand.Add)]
    [InlineData("search", MemorySubCommand.Search)]
    [InlineData("db", MemorySubCommand.Db)]
    [InlineData("stats", MemorySubCommand.Stats)]
    [InlineData("health", MemorySubCommand.Health)]
    [InlineData("cleanup", MemorySubCommand.Cleanup)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, MemorySubCommand expected)
    {
        MemorySubCommandExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void FromValue_Should_Be_CaseInsensitive()
    {
        MemorySubCommandExtensions.FromValue("EDIT").Should().Be(MemorySubCommand.Edit);
        MemorySubCommandExtensions.FromValue("STATS").Should().Be(MemorySubCommand.Stats);
        MemorySubCommandExtensions.FromValue("Health").Should().Be(MemorySubCommand.Health);
        MemorySubCommandExtensions.FromValue("DB").Should().Be(MemorySubCommand.Db);
        MemorySubCommandExtensions.FromValue("CLEANUP").Should().Be(MemorySubCommand.Cleanup);
    }

    [Fact]
    public void FromValue_InvalidString_Should_Return_Null()
    {
        MemorySubCommandExtensions.FromValue("invalid").Should().BeNull();
    }

    [Fact]
    public void FromValue_EmptyString_Should_Return_Null()
    {
        MemorySubCommandExtensions.FromValue("").Should().BeNull();
    }

    [Fact]
    public void FromValue_Null_Should_Return_Null()
    {
        MemorySubCommandExtensions.FromValue(null).Should().BeNull();
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(MemorySubCommand.Edit, true)]
    [InlineData(MemorySubCommand.Open, true)]
    [InlineData(MemorySubCommand.Add, true)]
    [InlineData(MemorySubCommand.Search, true)]
    [InlineData(MemorySubCommand.Db, true)]
    [InlineData(MemorySubCommand.Stats, true)]
    [InlineData(MemorySubCommand.Health, true)]
    [InlineData(MemorySubCommand.Cleanup, true)]
    public void IsDefined_AllValidValues_Should_Return_True(MemorySubCommand value, bool expected)
    {
        MemorySubCommandExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== MemorySubCommandConstants 测试 =====

    [Fact]
    public void Constants_Should_Match_EnumValues()
    {
        MemorySubCommandConstants.Edit.Should().Be("edit");
        MemorySubCommandConstants.Open.Should().Be("open");
        MemorySubCommandConstants.Add.Should().Be("add");
        MemorySubCommandConstants.Search.Should().Be("search");
        MemorySubCommandConstants.Db.Should().Be("db");
        MemorySubCommandConstants.Stats.Should().Be("stats");
        MemorySubCommandConstants.Health.Should().Be("health");
        MemorySubCommandConstants.Cleanup.Should().Be("cleanup");
    }

    // ===== 往返一致性测试 =====

    [Theory]
    [InlineData(MemorySubCommand.Edit)]
    [InlineData(MemorySubCommand.Open)]
    [InlineData(MemorySubCommand.Add)]
    [InlineData(MemorySubCommand.Search)]
    [InlineData(MemorySubCommand.Db)]
    [InlineData(MemorySubCommand.Stats)]
    [InlineData(MemorySubCommand.Health)]
    [InlineData(MemorySubCommand.Cleanup)]
    public void ToValue_FromValue_RoundTrip_Should_Be_Consistent(MemorySubCommand value)
    {
        var str = value.ToValue();
        MemorySubCommandExtensions.FromValue(str).Should().Be(value);
    }

    // ===== 数量验证 =====

    [Fact]
    public void AllValues_Should_Be_8()
    {
        var values = Enum.GetValues<MemorySubCommand>();
        values.Should().HaveCount(8);
    }
}
