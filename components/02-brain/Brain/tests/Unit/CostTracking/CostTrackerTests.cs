
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
        // Arrange
        var model = "gpt-4";
        var promptTokens = 1000;
        var completionTokens = 500;

        // Act
        _costTracker.RecordUsage(model, promptTokens, completionTokens);

        // Assert
        var stats = _costTracker.GetTotalStatistics();
        stats.RequestCount.Should().Be(1);
        stats.PromptTokens.Should().Be(promptTokens);
        stats.CompletionTokens.Should().Be(completionTokens);
        stats.TotalTokens.Should().Be(promptTokens + completionTokens);
        stats.TotalCostUsd.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RecordUsage_MultipleRecords_ShouldAccumulateCorrectly()
    {
        // Arrange & Act
        _costTracker.RecordUsage("gpt-4", 1000, 500);
        _costTracker.RecordUsage("gpt-4", 2000, 1000);
        _costTracker.RecordUsage("gpt-3.5-turbo", 500, 200);

        // Assert
        var stats = _costTracker.GetTotalStatistics();
        stats.RequestCount.Should().Be(3);
        stats.PromptTokens.Should().Be(3500);
        stats.CompletionTokens.Should().Be(1700);
        stats.ModelBreakdown.Should().HaveCount(2);
    }

    [Fact]
    public void RecordUsage_WithSessionId_ShouldTrackBySession()
    {
        // Arrange
        var sessionId = "test-session-001";

        // Act
        _costTracker.RecordUsage("gpt-4", 1000, 500, sessionId);
        _costTracker.RecordUsage("gpt-4", 2000, 1000, sessionId);
        _costTracker.RecordUsage("gpt-4", 500, 200, "other-session");

        // Assert
        var sessionStats = _costTracker.GetSessionStatistics(sessionId);
        sessionStats.RequestCount.Should().Be(2);
        sessionStats.PromptTokens.Should().Be(3000);

        var totalStats = _costTracker.GetTotalStatistics();
        totalStats.RequestCount.Should().Be(3);
    }

    [Theory]
    [InlineData("gpt-4", 1000, 1000, 0.03, 0.06)] // $0.03/1K prompt, $0.06/1K completion
    [InlineData("gpt-4o", 2000, 1000, 0.005, 0.015)] // $0.005/1K prompt, $0.015/1K completion
    [InlineData("gpt-3.5-turbo", 1000, 500, 0.0005, 0.0015)]
    public void RecordUsage_ShouldCalculateCostCorrectly(string model, int promptTokens, int completionTokens, decimal promptCostPer1K, decimal completionCostPer1K)
    {
        // Act
        _costTracker.RecordUsage(model, promptTokens, completionTokens);

        // Assert
        var stats = _costTracker.GetTotalStatistics();
        var expectedCost = (promptTokens / 1000m) * promptCostPer1K +
                          (completionTokens / 1000m) * completionCostPer1K;
        stats.TotalCostUsd.Should().BeApproximately(expectedCost, 0.0001m);
    }

    [Fact]
    public void GetTodayStatistics_ShouldReturnOnlyTodayRecords()
    {
        // 注意：由于我们使用 DateTime.UtcNow，测试中的记录都是今天的
        // Act
        _costTracker.RecordUsage("gpt-4", 1000, 500);

        // Assert
        var todayStats = _costTracker.GetTodayStatistics();
        todayStats.RequestCount.Should().Be(1);
    }

    [Fact]
    public void GetStatistics_WithDateRange_ShouldFilterCorrectly()
    {
        // Act
        _costTracker.RecordUsage("gpt-4", 1000, 500);

        // Assert
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
    public void GetAllModelCosts_ShouldReturnAllConfiguredModels()
    {
        // Act
        var allCosts = _costTracker.GetAllModelCosts();

        // Assert
        allCosts.Should().NotBeEmpty();
        allCosts.Should().ContainKey("gpt-4");
        allCosts.Should().ContainKey("gpt-3.5-turbo");
        allCosts.Should().ContainKey("claude-3-opus");
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
            Model = "gpt-4",
            PromptTokens = 1000,
            CompletionTokens = 500,
            TotalCost = 0.05m
        };

        // Assert
        modelStats.TotalTokens.Should().Be(1500);
    }

    [Theory]
    [InlineData("gpt-4-turbo")]
    [InlineData("gpt-4-turbo-preview")]
    [InlineData("gpt-4-0125-preview")]
    [InlineData("gpt-4-1106-preview")]
    public void RecordUsage_Gpt4TurboVariants_ShouldUseCorrectPricing(string modelVariant)
    {
        // Act
        _costTracker.RecordUsage(modelVariant, 1000, 1000);

        // Assert
        var stats = _costTracker.GetTotalStatistics();
        // gpt-4-turbo: $0.01/1K prompt, $0.03/1K completion
        var expectedCost = 0.01m + 0.03m;
        stats.TotalCostUsd.Should().BeApproximately(expectedCost, 0.0001m);
    }

    [Theory]
    [InlineData("claude-3-opus", 0.015, 0.075)]
    [InlineData("claude-3-sonnet", 0.003, 0.015)]
    [InlineData("claude-3-haiku", 0.00025, 0.00125)]
    public void RecordUsage_ClaudeModels_ShouldUseCorrectPricing(string model, double promptCost, double completionCost)
    {
        // Act
        _costTracker.RecordUsage(model, 1000, 1000);

        // Assert
        var stats = _costTracker.GetTotalStatistics();
        var expectedCost = (decimal)promptCost + (decimal)completionCost;
        stats.TotalCostUsd.Should().BeApproximately(expectedCost, 0.0001m);
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
        // Act
        _costTracker.RecordUsage("gpt-4", 1000, 500);
        _costTracker.RecordUsage("gpt-4", 2000, 1000);
        _costTracker.RecordUsage("gpt-3.5-turbo", 500, 200);
        _costTracker.RecordUsage("claude-3-opus", 1000, 500);

        // Assert
        var stats = _costTracker.GetTotalStatistics();
        stats.ModelBreakdown.Should().HaveCount(3);

        var gpt4Stats = stats.ModelBreakdown.First(m => m.Model == "gpt-4");
        gpt4Stats.RequestCount.Should().Be(2);
        gpt4Stats.PromptTokens.Should().Be(3000);
        gpt4Stats.CompletionTokens.Should().Be(1500);
    }
}
