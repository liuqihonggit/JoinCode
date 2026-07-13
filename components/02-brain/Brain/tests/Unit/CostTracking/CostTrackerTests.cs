
namespace Core.Tests.CostTracking;

public class CostTrackerTests : IDisposable, IAsyncLifetime
{
    private string _tempStoragePath = null!;
    private Core.CostTracking.CostTracker _costTracker = null!;
    private Mock<IFileOperationService> _fileOperationServiceMock = null!;

    public Task InitializeAsync()
    {
        _tempStoragePath = "/test/costs.json";
        _fileOperationServiceMock = new Mock<IFileOperationService>();
        _costTracker = new Core.CostTracking.CostTracker(_fileOperationServiceMock.Object, storagePath: _tempStoragePath, NullLogger<Core.CostTracking.CostTracker>.Instance);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public void RecordUsage_WithValidData_ShouldRecordSuccessfully()
    {
        var model = "test-model";
        var promptTokens = 1000;
        var completionTokens = 500;

        _costTracker.RecordUsage(model, promptTokens, completionTokens);

        var stats = _costTracker.GetTotalStatistics();
        stats.RequestCount.Should().Be(1);
        stats.PromptTokens.Should().Be(promptTokens);
        stats.CompletionTokens.Should().Be(completionTokens);
        stats.TotalTokens.Should().Be(promptTokens + completionTokens);
    }

    [Fact]
    public void RecordUsage_MultipleRecords_ShouldAccumulateCorrectly()
    {
        _costTracker.RecordUsage("model-a", 1000, 500);
        _costTracker.RecordUsage("model-a", 2000, 1000);
        _costTracker.RecordUsage("model-b", 500, 200);

        var stats = _costTracker.GetTotalStatistics();
        stats.RequestCount.Should().Be(3);
        stats.PromptTokens.Should().Be(3500);
        stats.CompletionTokens.Should().Be(1700);
        stats.ModelBreakdown.Should().HaveCount(2);
    }

    [Fact]
    public void RecordUsage_WithSessionId_ShouldTrackBySession()
    {
        var sessionId = "test-session-001";

        _costTracker.RecordUsage("model-a", 1000, 500, sessionId);
        _costTracker.RecordUsage("model-a", 2000, 1000, sessionId);
        _costTracker.RecordUsage("model-a", 500, 200, "other-session");

        var sessionStats = _costTracker.GetSessionStatistics(sessionId);
        sessionStats.RequestCount.Should().Be(2);
        sessionStats.PromptTokens.Should().Be(3000);

        var totalStats = _costTracker.GetTotalStatistics();
        totalStats.RequestCount.Should().Be(3);
    }

    [Fact]
    public void RecordUsage_ShouldCalculateCostCorrectly()
    {
        var customModel = "pricing-test-model";
        var promptCost = 0.02m;
        var completionCost = 0.06m;
        _costTracker.SetModelCost(customModel, promptCost, completionCost);

        _costTracker.RecordUsage(customModel, 1000, 1000);

        var stats = _costTracker.GetTotalStatistics();
        var expectedCost = promptCost + completionCost;
        stats.TotalCostUsd.Should().BeApproximately(expectedCost, 0.0001m);
    }

    [Fact]
    public void GetTodayStatistics_ShouldReturnOnlyTodayRecords()
    {
        _costTracker.RecordUsage("model-a", 1000, 500);

        var todayStats = _costTracker.GetTodayStatistics();
        todayStats.RequestCount.Should().Be(1);
    }

    [Fact]
    public void GetStatistics_WithDateRange_ShouldFilterCorrectly()
    {
        _costTracker.RecordUsage("model-a", 1000, 500);

        var startDate = DateTime.UtcNow.AddDays(-1);
        var endDate = DateTime.UtcNow.AddDays(1);
        var stats = _costTracker.GetStatistics(startDate, endDate);
        stats.RequestCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void SetModelCost_ShouldOverrideDefaultPricing()
    {
        // Arrange
        var customModel = "custom-model";
        var customPromptCost = 0.05m;
        var customCompletionCost = 0.10m;

        // Act
        _costTracker.SetModelCost(customModel, customPromptCost, customCompletionCost);
        _costTracker.RecordUsage(customModel, 1000, 1000);

        // Assert
        var modelCost = _costTracker.GetModelCost(customModel);
        modelCost.Should().NotBeNull();
        modelCost!.PromptCostPer1KTokens.Should().Be(customPromptCost);
        modelCost.CompletionCostPer1KTokens.Should().Be(customCompletionCost);

        var stats = _costTracker.GetTotalStatistics();
        var expectedCost = customPromptCost + customCompletionCost;
        stats.TotalCostUsd.Should().BeApproximately(expectedCost, 0.0001m);
    }

    [Fact]
    public void GetModelCost_UnknownModel_ShouldReturnNull()
    {
        // Act
        var costInfo = _costTracker.GetModelCost("completely-unknown-model-xyz");

        // Assert - GetModelCost returns null for unknown models, use CalculateCost instead
        costInfo.Should().BeNull();
    }

    [Fact]
    public void CalculateCost_UnknownModel_ShouldUseDefaultPricing()
    {
        // Act - RecordUsage with unknown model should still calculate cost using defaults
        _costTracker.RecordUsage("completely-unknown-model", 1000, 1000);

        // Assert
        var stats = _costTracker.GetTotalStatistics();
        // Default pricing is $0.01/1K prompt + $0.03/1K completion = $0.04
        stats.TotalCostUsd.Should().BeApproximately(0.04m, 0.001m);
    }

    [Fact]
    public void GetAllModelCosts_ShouldNotBeEmpty()
    {
        var allCosts = _costTracker.GetAllModelCosts();
        allCosts.Should().NotBeEmpty();
    }

    [Fact]
    public void GetTotalStatistics_NoRecords_ShouldReturnEmptyStats()
    {
        // Arrange - fresh CostTracker with no records
        var freshTracker = new CostTracker(_fileOperationServiceMock.Object, storagePath: "/test/empty_costs.json");

        // Act
        var stats = freshTracker.GetTotalStatistics();

        // Assert
        stats.RequestCount.Should().Be(0);
        stats.PromptTokens.Should().Be(0);
        stats.CompletionTokens.Should().Be(0);
        stats.TotalTokens.Should().Be(0);
        stats.TotalCostUsd.Should().Be(0);
        stats.ModelBreakdown.Should().BeEmpty();
    }

    [Fact]
    public void ModelCostStatistics_TotalTokens_ShouldCalculateCorrectly()
    {
        // Arrange
        var modelStats = new ModelCostStatistics
        {
            Model = "test-model",
            PromptTokens = 1000,
            CompletionTokens = 500,
            TotalCost = 0.05m
        };

        // Assert
        modelStats.TotalTokens.Should().Be(1500);
    }

    [Fact]
    public void GetSessionStatistics_UnknownSession_ShouldReturnEmptyStats()
    {
        // Act
        var stats = _costTracker.GetSessionStatistics("non-existent-session");

        // Assert
        stats.RequestCount.Should().Be(0);
        stats.TotalCostUsd.Should().Be(0);
    }

    [Fact]
    public void RecordUsage_ModelBreakdown_ShouldGroupByModel()
    {
        var model1 = "test-model-a";
        var model2 = "test-model-b";

        _costTracker.RecordUsage(model1, 1000, 500);
        _costTracker.RecordUsage(model1, 2000, 1000);
        _costTracker.RecordUsage(model2, 500, 200);

        var stats = _costTracker.GetTotalStatistics();
        stats.ModelBreakdown.Should().HaveCount(2);

        var model1Stats = stats.ModelBreakdown.First(m => m.Model == model1);
        model1Stats.RequestCount.Should().Be(2);
        model1Stats.PromptTokens.Should().Be(3000);
        model1Stats.CompletionTokens.Should().Be(1500);
    }
}
