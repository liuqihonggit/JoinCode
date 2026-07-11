using Infrastructure.Utils.Text;

namespace JoinCode.Infra.Tests.Text;

public class DurationFormatterTests
{
    [Fact]
    public void Format_Zero_ShouldReturn0s()
    {
        DurationFormatter.Format(TimeSpan.Zero).Should().Be("0s");
    }

    [Fact]
    public void Format_Milliseconds_ShouldReturnMs()
    {
        DurationFormatter.Format(TimeSpan.FromMilliseconds(500)).Should().Be("500ms");
    }

    [Fact]
    public void Format_Seconds_ShouldReturnS()
    {
        DurationFormatter.Format(TimeSpan.FromSeconds(5)).Should().Be("5s");
    }

    [Fact]
    public void Format_Sub10Seconds_ShouldReturnDecimal()
    {
        DurationFormatter.Format(TimeSpan.FromMilliseconds(2500)).Should().Be("2.5s");
    }

    [Fact]
    public void Format_MinutesAndSeconds_ShouldReturnM_S()
    {
        DurationFormatter.Format(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(30)))
            .Should().Be("2m 30s");
    }

    [Fact]
    public void Format_MinutesZeroSeconds_ShouldReturnM()
    {
        DurationFormatter.Format(TimeSpan.FromMinutes(5))
            .Should().Be("5m");
    }

    [Fact]
    public void Format_HoursAndMinutes_ShouldReturnH_M()
    {
        DurationFormatter.Format(TimeSpan.FromHours(2).Add(TimeSpan.FromMinutes(30)))
            .Should().Be("2h 30m");
    }

    [Fact]
    public void Format_DaysAndHours_ShouldReturnD_H()
    {
        DurationFormatter.Format(TimeSpan.FromDays(1).Add(TimeSpan.FromHours(3)))
            .Should().Be("1d 3h");
    }

    [Fact]
    public void Format_Chinese_ShouldReturnChineseFormat()
    {
        var options = new DurationFormatOptions { UseAbbreviations = false };
        DurationFormatter.Format(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(30)), options)
            .Should().Be("5分钟30秒");
    }

    [Fact]
    public void Format_ChineseHours_ShouldReturnChineseFormat()
    {
        var options = new DurationFormatOptions { UseAbbreviations = false };
        DurationFormatter.Format(TimeSpan.FromHours(2).Add(TimeSpan.FromMinutes(30)), options)
            .Should().Be("2小时30分钟");
    }

    [Fact]
    public void Format_ChineseDays_ShouldReturnChineseFormat()
    {
        var options = new DurationFormatOptions { UseAbbreviations = false };
        DurationFormatter.Format(TimeSpan.FromDays(1).Add(TimeSpan.FromHours(3)), options)
            .Should().Be("1天3小时");
    }

    [Fact]
    public void Format_MostSignificantOnly_Hours_ShouldReturnSingleUnit()
    {
        var options = DurationFormatOptions.MostSignificant;
        DurationFormatter.Format(TimeSpan.FromHours(2).Add(TimeSpan.FromMinutes(30)), options)
            .Should().Be("2.5h");
    }

    [Fact]
    public void Format_MostSignificantOnly_Minutes_ShouldReturnSingleUnit()
    {
        var options = DurationFormatOptions.MostSignificant;
        DurationFormatter.Format(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(30)), options)
            .Should().Be("5.5m");
    }

    [Fact]
    public void Format_MostSignificantOnly_Seconds_ShouldReturnSingleUnit()
    {
        var options = DurationFormatOptions.MostSignificant;
        DurationFormatter.Format(TimeSpan.FromSeconds(3.5), options)
            .Should().Be("3.5s");
    }

    [Fact]
    public void Format_MostSignificantOnly_Milliseconds_ShouldReturnMs()
    {
        var options = DurationFormatOptions.MostSignificant;
        DurationFormatter.Format(TimeSpan.FromMilliseconds(500), options)
            .Should().Be("500ms");
    }

    [Fact]
    public void Format_FromMilliseconds_ShouldWork()
    {
        DurationFormatter.Format(5000L).Should().Be("5s");
    }

    [Fact]
    public void Format_Verbose_ShouldShowZeroValues()
    {
        var options = DurationFormatOptions.Verbose;
        DurationFormatter.Format(TimeSpan.FromHours(2), options)
            .Should().Be("2h 0m 0s");
    }

    [Fact]
    public void Format_Default_ShouldHideZeroTrailing()
    {
        DurationFormatter.Format(TimeSpan.FromHours(2))
            .Should().Be("2h");
    }
}