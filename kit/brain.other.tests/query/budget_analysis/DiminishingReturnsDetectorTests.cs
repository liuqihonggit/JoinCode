namespace Core.Tests.Query.BudgetAnalysis;

public class DiminishingReturnsDetectorTests
{
    private readonly DiminishingReturnsDetector _detector = new();

    [Fact]
    public void CheckDiminishingReturns_InsufficientSamples_ShouldReturnNotDiminishing()
    {
        var consumptions = new List<TokenConsumption>
        {
            new() { Amount = 100 }
        };

        var result = _detector.CheckDiminishingReturns(consumptions);

        result.IsDiminishing.Should().BeFalse();
        result.EffectivenessRatio.Should().Be(1.0);
        result.ConsecutiveLowValueIterations.Should().Be(0);
    }

    [Fact]
    public void CheckDiminishingReturns_EmptyList_ShouldReturnNotDiminishing()
    {
        var result = _detector.CheckDiminishingReturns([]);

        result.IsDiminishing.Should().BeFalse();
    }

    [Fact]
    public void CheckDiminishingReturns_NullInput_ShouldThrowArgumentNullException()
    {
        var act = () => _detector.CheckDiminishingReturns(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CheckDiminishingReturns_HighRatioData_ShouldNotTriggerDiminishing()
    {
        var consumptions = new List<TokenConsumption>
        {
            new() { Amount = 100 },
            new() { Amount = 200 },
            new() { Amount = 400 },
            new() { Amount = 800 }
        };

        var result = _detector.CheckDiminishingReturns(consumptions);

        result.IsDiminishing.Should().BeFalse();
        result.EffectivenessRatio.Should().BeGreaterThan(0.1);
    }

    [Fact]
    public void CheckDiminishingReturns_LowRatioData_ShouldTriggerDiminishing()
    {
        var consumptions = new List<TokenConsumption>
        {
            new() { Amount = 1000 },
            new() { Amount = 10 },
            new() { Amount = 1 }
        };

        DiminishingReturnsResult result = new();
        for (var i = 0; i < 3; i++)
        {
            result = _detector.CheckDiminishingReturns(consumptions);
        }

        result.IsDiminishing.Should().BeTrue();
        result.ConsecutiveLowValueIterations.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void CheckDiminishingReturns_LowRatioOnce_ShouldNotTriggerYet()
    {
        var consumptions = new List<TokenConsumption>
        {
            new() { Amount = 1000 },
            new() { Amount = 10 }
        };

        var result = _detector.CheckDiminishingReturns(consumptions);

        result.IsDiminishing.Should().BeFalse();
        result.ConsecutiveLowValueIterations.Should().Be(1);
    }

    [Fact]
    public void CheckDiminishingReturns_LowRatioTwice_ShouldNotTriggerYet()
    {
        var consumptions = new List<TokenConsumption>
        {
            new() { Amount = 1000 },
            new() { Amount = 10 }
        };

        _detector.CheckDiminishingReturns(consumptions);
        var result = _detector.CheckDiminishingReturns(consumptions);

        result.IsDiminishing.Should().BeFalse();
        result.ConsecutiveLowValueIterations.Should().Be(2);
    }

    [Fact]
    public void CheckDiminishingReturns_HighRatioAfterLow_ShouldResetConsecutiveCount()
    {
        var lowConsumptions = new List<TokenConsumption>
        {
            new() { Amount = 1000 },
            new() { Amount = 10 }
        };

        _detector.CheckDiminishingReturns(lowConsumptions);
        _detector.CheckDiminishingReturns(lowConsumptions);

        var highConsumptions = new List<TokenConsumption>
        {
            new() { Amount = 100 },
            new() { Amount = 500 }
        };

        var result = _detector.CheckDiminishingReturns(highConsumptions);

        result.IsDiminishing.Should().BeFalse();
        result.ConsecutiveLowValueIterations.Should().Be(0);
    }

    [Fact]
    public void Reset_ShouldClearConsecutiveCount()
    {
        var lowConsumptions = new List<TokenConsumption>
        {
            new() { Amount = 1000 },
            new() { Amount = 10 }
        };

        _detector.CheckDiminishingReturns(lowConsumptions);
        _detector.CheckDiminishingReturns(lowConsumptions);

        _detector.Reset();

        var result = _detector.CheckDiminishingReturns(lowConsumptions);
        result.ConsecutiveLowValueIterations.Should().Be(1);
    }

    [Fact]
    public void CheckDiminishingReturns_ZeroPreviousAmount_ShouldSkipRatio()
    {
        var consumptions = new List<TokenConsumption>
        {
            new() { Amount = 0 },
            new() { Amount = 100 }
        };

        var result = _detector.CheckDiminishingReturns(consumptions);

        result.IsDiminishing.Should().BeFalse();
    }

    [Fact]
    public void CheckDiminishingReturns_SustainedDiminishing_ShouldRecommendStop()
    {
        var lowConsumptions = new List<TokenConsumption>
        {
            new() { Amount = 1000 },
            new() { Amount = 10 }
        };

        for (var i = 0; i < 5; i++)
        {
            _detector.CheckDiminishingReturns(lowConsumptions);
        }

        var result = _detector.CheckDiminishingReturns(lowConsumptions);

        result.IsDiminishing.Should().BeTrue();
        result.Recommendation.Should().Contain("Stop iteration");
    }
}
