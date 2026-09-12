
namespace Core.Tests.Scheduling.Cron;

public class CronExpressionParserTests
{
    [Theory]
    [InlineData("0 9 * * *", true)]  // 每天9点
    [InlineData("0 */6 * * *", true)]  // 每6小时
    [InlineData("0 9 * * 1-5", true)]  // 工作日9点
    [InlineData("*/5 * * * *", true)]  // 每5分钟
    [InlineData("0 0 1 * *", true)]  // 每月1号
    [InlineData("invalid", false)]  // 无效
    [InlineData("", false)]  // 空
    [InlineData("* * *", false)]  // 字段不足
    public void IsValid_ReturnsExpectedResult(string expression, bool expected)
    {
        var result = CronExpressionParser.IsValid(expression);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Parse_ValidExpression_ReturnsFields()
    {
        var fields = CronExpressionParser.Parse("0 9 * * *");

        Assert.NotNull(fields);
        Assert.Single(fields.Minute, 0);
        Assert.Single(fields.Hour, 9);
        Assert.Equal(31, fields.DayOfMonth.Length);  // 1-31
        Assert.Equal(12, fields.Month.Length);  // 1-12
        Assert.Equal(7, fields.DayOfWeek.Length);  // 0-6
    }

    [Fact]
    public void Parse_StepExpression_ReturnsCorrectValues()
    {
        var fields = CronExpressionParser.Parse("*/15 * * * *");

        Assert.NotNull(fields);
        Assert.Equal(new[] { 0, 15, 30, 45 }, fields.Minute);
    }

    [Fact]
    public void Parse_RangeExpression_ReturnsCorrectValues()
    {
        var fields = CronExpressionParser.Parse("0 9-17 * * 1-5");

        Assert.NotNull(fields);
        Assert.Equal(new[] { 0 }, fields.Minute);
        Assert.Equal(Enumerable.Range(9, 9).ToArray(), fields.Hour);  // 9-17
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, fields.DayOfWeek);
    }

    [Fact]
    public void Parse_ListExpression_ReturnsCorrectValues()
    {
        var fields = CronExpressionParser.Parse("0,30 9,12,18 * * *");

        Assert.NotNull(fields);
        Assert.Equal(new[] { 0, 30 }, fields.Minute);
        Assert.Equal(new[] { 9, 12, 18 }, fields.Hour);
    }

    [Fact]
    public void Parse_DayOfWeekSeven_ReturnsZero()
    {
        var fields = CronExpressionParser.Parse("0 9 * * 7");

        Assert.NotNull(fields);
        Assert.Single(fields.DayOfWeek, 0);  // 7 应该被转换为 0（周日）
    }

    [Fact]
    public void Parse_InvalidExpression_ReturnsNull()
    {
        var fields = CronExpressionParser.Parse("invalid cron");
        Assert.Null(fields);
    }

    [Fact]
    public void Parse_OutOfRangeValue_ReturnsNull()
    {
        var fields = CronExpressionParser.Parse("60 25 32 13 8");
        Assert.Null(fields);
    }
}
