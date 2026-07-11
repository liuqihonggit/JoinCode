namespace Host.Tests.ChatCommands;

/// <summary>
/// TasksAction 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / TasksActionConstants 常量值
/// </summary>
public sealed class TasksActionExtensionsTests
{
    // ===== ToValue 测试 =====

    [Fact]
    public void ToValue_Kill_Should_Return_kill()
    {
        TasksAction.Kill.ToValue().Should().Be("kill");
    }

    [Fact]
    public void ToValue_Detail_Should_Return_detail()
    {
        TasksAction.Detail.ToValue().Should().Be("detail");
    }

    [Fact]
    public void ToValue_Complete_Should_Return_complete()
    {
        TasksAction.Complete.ToValue().Should().Be("complete");
    }

    [Fact]
    public void ToValue_Todo_Should_Return_todo()
    {
        TasksAction.Todo.ToValue().Should().Be("todo");
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("kill", TasksAction.Kill)]
    [InlineData("detail", TasksAction.Detail)]
    [InlineData("complete", TasksAction.Complete)]
    [InlineData("todo", TasksAction.Todo)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, TasksAction expected)
    {
        TasksActionExtensions.FromValue(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("KILL")]
    [InlineData("Detail")]
    [InlineData("COMPLETE")]
    [InlineData("Todo")]
    public void FromValue_CaseInsensitive_Should_Return_CorrectEnum(string input)
    {
        TasksActionExtensions.FromValue(input).Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("list")]
    [InlineData("killtask")]
    public void FromValue_InvalidString_Should_Return_Null(string? input)
    {
        TasksActionExtensions.FromValue(input).Should().BeNull();
    }

    // ===== RoundTrip 测试 =====

    [Theory]
    [InlineData(TasksAction.Kill)]
    [InlineData(TasksAction.Detail)]
    [InlineData(TasksAction.Complete)]
    [InlineData(TasksAction.Todo)]
    public void RoundTrip_ToValue_ThenFromValue_Should_Return_Same(TasksAction value)
    {
        var s = value.ToValue();
        TasksActionExtensions.FromValue(s).Should().Be(value);
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(TasksAction.Kill, true)]
    [InlineData(TasksAction.Detail, true)]
    [InlineData(TasksAction.Complete, true)]
    [InlineData(TasksAction.Todo, true)]
    public void IsDefined_KnownValue_Should_Return_True(TasksAction value, bool expected)
    {
        TasksActionExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== Constants 测试 =====

    [Fact]
    public void Constants_Kill_Should_Be_kill()
    {
        TasksActionConstants.Kill.Should().Be("kill");
    }

    [Fact]
    public void Constants_Detail_Should_Be_detail()
    {
        TasksActionConstants.Detail.Should().Be("detail");
    }

    [Fact]
    public void Constants_Complete_Should_Be_complete()
    {
        TasksActionConstants.Complete.Should().Be("complete");
    }

    [Fact]
    public void Constants_Todo_Should_Be_todo()
    {
        TasksActionConstants.Todo.Should().Be("todo");
    }

    // ===== 枚举值数量验证 =====

    [Fact]
    public void AllValues_Should_Be_4()
    {
        var values = Enum.GetValues<TasksAction>();
        values.Should().HaveCount(4);
    }

    // ===== 与 CrudAction 边界值不冲突 =====

    [Fact]
    public void FromValue_CreateOrUpdate_Should_Return_Null_BecauseCrudAction()
    {
        // create/new/update 是 CrudAction 范围,不应被 TasksAction 解析
        TasksActionExtensions.FromValue("create").Should().BeNull();
        TasksActionExtensions.FromValue("new").Should().BeNull();
        TasksActionExtensions.FromValue("update").Should().BeNull();
    }
}
