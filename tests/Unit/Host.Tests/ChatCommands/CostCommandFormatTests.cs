namespace JoinCode.ChatCommands.Tests;

public class CostCommandFormatTests
{
    [Fact]
    public void FormatCost_SmallAmount_ShouldUse4Decimals()
    {
        CostCommand.FormatCost(0.0012m).Should().Be("$0.0012");
    }

    [Fact]
    public void FormatCost_LargeAmount_ShouldUse2Decimals()
    {
        CostCommand.FormatCost(1.234m).Should().Be("$1.23");
    }

    [Fact]
    public void FormatCost_Zero_ShouldUse4Decimals()
    {
        CostCommand.FormatCost(0m).Should().Be("$0.0000");
    }

    [Fact]
    public void FormatCost_ExactlyHalf_ShouldUse2Decimals()
    {
        CostCommand.FormatCost(0.5m).Should().Be("$0.50");
    }

    [Fact]
    public void FormatDuration_Zero_ShouldReturn0s()
    {
        DurationFormatter.Format(TimeSpan.Zero).Should().Be("0s");
    }

    [Fact]
    public void FormatDuration_Milliseconds_ShouldReturnMs()
    {
        DurationFormatter.Format(TimeSpan.FromMilliseconds(500)).Should().Be("500ms");
    }

    [Fact]
    public void FormatDuration_Seconds_ShouldReturnS()
    {
        DurationFormatter.Format(TimeSpan.FromSeconds(30)).Should().Be("30s");
    }

    [Fact]
    public void FormatDuration_MinutesAndSeconds_ShouldReturnMmSs()
    {
        DurationFormatter.Format(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(20)).Should().Be("3m 20s");
    }

    [Fact]
    public void FormatDuration_HoursMinutesSeconds_ShouldReturnHhMmSs()
    {
        DurationFormatter.Format(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(45)).Should().Be("1h 5m 45s");
    }

    [Fact]
    public void FormatDuration_DaysHoursMinutes_ShouldReturnDdHhMm()
    {
        DurationFormatter.Format(TimeSpan.FromDays(2) + TimeSpan.FromHours(3) + TimeSpan.FromMinutes(15)).Should().Be("2d 3h 15m");
    }

    [Fact]
    public void GetCanonicalName_UnknownModel_ShouldReturnOriginal()
    {
        ModelNameHelper.GetCanonicalName("my-custom-model").Should().Be("my-custom-model");
    }

    [Fact]
    public void FormatTotalCost_WithStats_ShouldContainAllFields()
    {
        var stats = new CostStatistics
        {
            RequestCount = 5,
            PromptTokens = 12345,
            CompletionTokens = 6789,
            TotalCostUsd = 0.1234m,
            ApiDuration = TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(20),
            WallDuration = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(45),
            LinesAdded = 42,
            LinesRemoved = 18,
            HasUnknownModelCost = false,
            ModelBreakdown =
            [
                new ModelCostStatistics
                {
                    Model = "claude-3-5-sonnet-20241022",
                    RequestCount = 3,
                    PromptTokens = 10000,
                    CompletionTokens = 5000,
                    CacheCreationTokens = 500,
                    CacheReadTokens = 2000,
                    TotalCost = 0.10m
                }
            ]
        };

        var output = CostCommand.FormatTotalCost(stats);

        output.Should().Contain("Total cost:");
        output.Should().Contain("$0.1234");
        output.Should().Contain("3m 20s");
        output.Should().Contain("5m 45s");
        output.Should().Contain("42 lines added");
        output.Should().Contain("18 lines removed");
        output.Should().Contain("Usage by model:");
        output.Should().Contain("claude-3-5-sonnet");
        output.Should().Contain("cache read");
        output.Should().Contain("cache write");
    }

    [Fact]
    public void FormatTotalCost_UnknownModelCost_ShouldShowWarning()
    {
        var stats = new CostStatistics
        {
            TotalCostUsd = 0.5m,
            HasUnknownModelCost = true,
            ModelBreakdown = []
        };

        var output = CostCommand.FormatTotalCost(stats);

        output.Should().Contain("costs may be inaccurate due to usage of unknown models");
    }

    [Fact]
    public void FormatTotalCost_NoModelBreakdown_ShouldShowZeroUsage()
    {
        var stats = new CostStatistics
        {
            TotalCostUsd = 0m,
            ModelBreakdown = []
        };

        var output = CostCommand.FormatTotalCost(stats);

        output.Should().Contain("0 input, 0 output, 0 cache read, 0 cache write");
    }

    [Fact]
    public void FormatModelUsage_ShouldAggregateByShortName()
    {
        var breakdown = new List<ModelCostStatistics>
        {
            new()
            {
                Model = "claude-3-5-sonnet-20241022",
                RequestCount = 2,
                PromptTokens = 5000,
                CompletionTokens = 2000,
                CacheCreationTokens = 100,
                CacheReadTokens = 500,
                TotalCost = 0.05m
            },
            new()
            {
                Model = "claude-sonnet-4-6-20250514",
                RequestCount = 1,
                PromptTokens = 3000,
                CompletionTokens = 1000,
                CacheCreationTokens = 50,
                CacheReadTokens = 300,
                TotalCost = 0.03m
            }
        };

        var output = CostCommand.FormatModelUsage(breakdown);

        output.Should().Contain("claude-3-5-sonnet");
        output.Should().Contain("claude-sonnet-4-6");
        output.Should().Contain("8,000 input");
        output.Should().Contain("3,000 output");
        output.Should().Contain("800 cache read");
        output.Should().Contain("150 cache write");
    }

    [Fact]
    public void FormatTotalCost_SingleLineChange_ShouldUseSingular()
    {
        var stats = new CostStatistics
        {
            LinesAdded = 1,
            LinesRemoved = 1,
            ModelBreakdown = []
        };

        var output = CostCommand.FormatTotalCost(stats);

        output.Should().Contain("1 line added");
        output.Should().Contain("1 line removed");
    }
}
