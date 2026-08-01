namespace JoinCode.Reasoning.Tests.Weight;

public sealed class EvidenceWeightCalculatorEdgeCaseTests
{
    [Theory]
    [InlineData("政府机构", 0.95)]
    [InlineData("法院判决", 0.90)]
    [InlineData("银行系统", 0.88)]
    [InlineData("公证文件", 0.85)]
    [InlineData("媒体报道", 0.60)]
    [InlineData("个人陈述", 0.40)]
    [InlineData("匿名来源", 0.15)]
    [InlineData("未知来源", 0.30)]
    [InlineData(null, 0.30)]
    [InlineData("", 0.30)]
    public void CalculateWeight_SourceCredibility_ShouldMatchExpected(string? source, double expected)
    {
        var calculator = new EvidenceWeightCalculator();
        var evidence = CreateEvidence(source: source);

        var result = calculator.CalculateWeight(evidence);

        Assert.Equal(expected, result.Components.SourceCredibility, precision: 2);
    }

    [Theory]
    [InlineData(EvidenceCategory.JudicialNotice, 0.95)]
    [InlineData(EvidenceCategory.Physical, 0.90)]
    [InlineData(EvidenceCategory.Documentary, 0.85)]
    [InlineData(EvidenceCategory.Financial, 0.80)]
    [InlineData(EvidenceCategory.Contractual, 0.80)]
    [InlineData(EvidenceCategory.ExpertOpinion, 0.70)]
    [InlineData(EvidenceCategory.Digital, 0.70)]
    [InlineData(EvidenceCategory.Testimonial, 0.55)]
    [InlineData(EvidenceCategory.Circumstantial, 0.40)]
    public void CalculateWeight_EvidenceTypeWeight_ShouldMatchExpected(EvidenceCategory category, double expected)
    {
        var calculator = new EvidenceWeightCalculator();
        var evidence = CreateEvidence(category: category);

        var result = calculator.CalculateWeight(evidence);

        Assert.Equal(expected, result.Components.EvidenceTypeWeight, precision: 2);
    }

    [Theory]
    [InlineData(0, 0.2)]
    [InlineData(1, 0.5)]
    [InlineData(2, 0.8)]
    [InlineData(3, 1.0)]
    [InlineData(10, 1.0)]
    public void CalculateWeight_CorroborationScore_ShouldMatchExpected(int corroborationCount, double expected)
    {
        var calculator = new EvidenceWeightCalculator();
        var evidence = CreateEvidence();

        var result = calculator.CalculateWeight(evidence, corroborationCount);

        Assert.Equal(expected, result.Components.CorroborationScore, precision: 2);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(3, 1.0)]
    [InlineData(10, 0.9)]
    [InlineData(20, 0.9)]
    [InlineData(45, 0.7)]
    [InlineData(120, 0.5)]
    [InlineData(500, 0.3)]
    public void CalculateWeight_Timeliness_ShouldMatchExpected(int daysAgo, double expected)
    {
        var calculator = new EvidenceWeightCalculator();
        var evidence = CreateEvidence(createdAt: DateTime.UtcNow.AddDays(-daysAgo));

        var result = calculator.CalculateWeight(evidence);

        Assert.Equal(expected, result.Components.Timeliness, precision: 2);
    }

    [Fact]
    public void CalculateWeight_VerificationStatus_UrlVerified_ReturnsOne()
    {
        var calculator = new EvidenceWeightCalculator();
        var evidence = CreateEvidence(sourceUrl: "https://example.com", isUrlVerified: true);

        var result = calculator.CalculateWeight(evidence);

        Assert.Equal(1.0, result.Components.VerificationStatus, precision: 2);
    }

    [Fact]
    public void CalculateWeight_VerificationStatus_UnverifiedUrl_ReturnsHalf()
    {
        var calculator = new EvidenceWeightCalculator();
        var evidence = CreateEvidence(sourceUrl: "https://example.com", isUrlVerified: false);

        var result = calculator.CalculateWeight(evidence);

        Assert.Equal(0.5, result.Components.VerificationStatus, precision: 2);
    }

    [Fact]
    public void CalculateWeight_VerificationStatus_NoUrl_ReturnsDefault()
    {
        var calculator = new EvidenceWeightCalculator();
        var evidence = CreateEvidence();

        var result = calculator.CalculateWeight(evidence);

        Assert.Equal(0.7, result.Components.VerificationStatus, precision: 2);
    }

    [Fact]
    public void CalculateTotalWeight_WithWeights_AppliesMultiplication()
    {
        var calculator = new EvidenceWeightCalculator();
        var evidences = new[]
        {
            CreateEvidence(weight: 2.0),
            CreateEvidence(weight: 0.5),
        };

        var total = calculator.CalculateTotalWeight(evidences, _ => 0);

        var expected = calculator.CalculateWeight(evidences[0], 0).Total * 2.0 +
                       calculator.CalculateWeight(evidences[1], 0).Total * 0.5;
        Assert.Equal(expected, total, precision: 5);
    }

    [Fact]
    public void CalculateWeight_Total_IsWeightedSum()
    {
        var calculator = new EvidenceWeightCalculator();
        var evidence = CreateEvidence(
            source: "政府机构",
            category: EvidenceCategory.JudicialNotice,
            sourceUrl: "https://example.com",
            isUrlVerified: true,
            createdAt: DateTime.UtcNow);

        var result = calculator.CalculateWeight(evidence, corroborationCount: 3);

        var expected = 0.95 * 0.30 + 0.95 * 0.25 + 1.0 * 0.20 + 1.0 * 0.15 + 1.0 * 0.10;
        Assert.Equal(expected, result.Total, precision: 5);
    }

    [Fact]
    public void CalculateWeight_RawScore_MapsTrustLevel()
    {
        var calculator = new EvidenceWeightCalculator();
        var evidence = CreateEvidence(trustLevel: TrustLevel.Moderate);

        var result = calculator.CalculateWeight(evidence);

        Assert.Equal((int)TrustLevel.Moderate / 100.0, result.RawScore, precision: 5);
    }

    private static EvidenceRecord CreateEvidence(
        TrustLevel trustLevel = TrustLevel.Moderate,
        EvidenceCategory category = EvidenceCategory.Documentary,
        string? source = null,
        string? sourceUrl = null,
        bool isUrlVerified = false,
        double weight = 1.0,
        DateTime? createdAt = null)
    {
        return new EvidenceRecord
        {
            Content = "测试证据",
            Category = category,
            TrustLevel = trustLevel,
            SubmittedBy = AgentRole.Prosecutor,
            Source = source,
            SourceUrl = sourceUrl,
            IsUrlVerified = isUrlVerified,
            Weight = weight,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };
    }
}
