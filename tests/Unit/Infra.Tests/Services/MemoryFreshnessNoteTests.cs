using JoinCode.Abstractions.Configuration.Settings;
using JoinCode.Abstractions.Configuration.AppData;

namespace Infrastructure.Tests.Services;

public sealed class MemoryFreshnessNoteTests
{
    [Fact]
    public void MemoryAgeDays_Today_ReturnsZero()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        MemoryFreshnessNote.MemoryAgeDays(now).Should().Be(0);
    }

    [Fact]
    public void MemoryAgeDays_Yesterday_ReturnsOne()
    {
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        MemoryFreshnessNote.MemoryAgeDays(yesterday).Should().Be(1);
    }

    [Fact]
    public void MemoryAgeDays_WeekAgo_ReturnsSeven()
    {
        var weekAgo = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds();
        MemoryFreshnessNote.MemoryAgeDays(weekAgo).Should().Be(7);
    }

    [Fact]
    public void MemoryAgeDays_FutureClampsToZero()
    {
        var future = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds();
        MemoryFreshnessNote.MemoryAgeDays(future).Should().Be(0);
    }

    [Fact]
    public void MemoryAge_Today_ReturnsToday()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        MemoryFreshnessNote.MemoryAge(now).Should().Be("today");
    }

    [Fact]
    public void MemoryAge_Yesterday_ReturnsYesterday()
    {
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        MemoryFreshnessNote.MemoryAge(yesterday).Should().Be("yesterday");
    }

    [Fact]
    public void MemoryAge_WeekAgo_ReturnsDaysAgo()
    {
        var weekAgo = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds();
        MemoryFreshnessNote.MemoryAge(weekAgo).Should().Be("7 days ago");
    }

    [Fact]
    public void FreshnessText_Today_ReturnsEmpty()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        MemoryFreshnessNote.FreshnessText(now).Should().BeEmpty();
    }

    [Fact]
    public void FreshnessText_Yesterday_ReturnsEmpty()
    {
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds();
        MemoryFreshnessNote.FreshnessText(yesterday).Should().BeEmpty();
    }

    [Fact]
    public void FreshnessText_TwoDaysOld_ReturnsWarning()
    {
        var twoDays = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        var text = MemoryFreshnessNote.FreshnessText(twoDays);
        text.Should().Contain("2 days old");
        text.Should().Contain("point-in-time observations");
    }

    [Fact]
    public void FreshnessNote_Today_ReturnsEmpty()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        MemoryFreshnessNote.FreshnessNote(now).Should().BeEmpty();
    }

    [Fact]
    public void FreshnessNote_TwoDaysOld_ReturnsSystemReminder()
    {
        var twoDays = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeMilliseconds();
        var note = MemoryFreshnessNote.FreshnessNote(twoDays);
        note.Should().StartWith("<system-reminder>");
        note.Should().Contain("2 days old");
        note.Should().EndWith("</system-reminder>\n");
    }

    [Fact]
    public void IsMemoryFile_NullPath_ReturnsFalse()
    {
        MemoryFreshnessNote.IsMemoryFile(null!).Should().BeFalse();
    }

    [Fact]
    public void IsMemoryFile_EmptyPath_ReturnsFalse()
    {
        MemoryFreshnessNote.IsMemoryFile("").Should().BeFalse();
    }

    [Fact]
    public void IsMemoryFile_RegularFile_ReturnsFalse()
    {
        MemoryFreshnessNote.IsMemoryFile("C:\\Users\\test\\project\\src\\Program.cs").Should().BeFalse();
    }
}
