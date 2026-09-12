namespace JoinCode.Reasoning.Tests.Weight;

public sealed class EvidenceWeightCalculatorTests
{
    [Fact]
    public void CalculateWeight_ShouldReturnTotalBetween0And1()
    {
        var calculator = new EvidenceWeightCalculator();
        var evidence = CreateEvidence();

        var result = calculator.CalculateWeight(evidence);

        Assert.InRange(result.Total, 0, 1);
    }

    [Fact]
    public void CalculateWeight_ShouldHaveHigherScoreForDirectEvidence()
    {
        var calculator = new EvidenceWeightCalculator();
        var direct = CreateEvidence(trustLevel: TrustLevel.DirectEvidence, category: EvidenceCategory.Physical);
        var hearsay = CreateEvidence(trustLevel: TrustLevel.Hearsay, category: EvidenceCategory.Circumstantial);

        var directWeight = calculator.CalculateWeight(direct);
        var hearsayWeight = calculator.CalculateWeight(hearsay);

        Assert.True(directWeight.Components.EvidenceTypeWeight > hearsayWeight.Components.EvidenceTypeWeight);
    }

    [Fact]
    public void CalculateWeight_SourceCredibility_ShouldScoreGovernmentHigher()
    {
        var calculator = new EvidenceWeightCalculator();
        var gov = CreateEvidence(source: "政府机构");
        var anon = CreateEvidence(source: "匿名来源");

        var govWeight = calculator.CalculateWeight(gov);
        var anonWeight = calculator.CalculateWeight(anon);

        Assert.True(govWeight.Components.SourceCredibility > anonWeight.Components.SourceCredibility);
    }

    [Fact]
    public void CalculateWeight_VerificationStatus_ShouldScoreVerifiedHigher()
    {
        var calculator = new EvidenceWeightCalculator();
        var verified = CreateEvidence(isUrlVerified: true, sourceUrl: "https://example.com");
        var unverified = CreateEvidence(isUrlVerified: false, sourceUrl: "https://example.com");

        var verifiedWeight = calculator.CalculateWeight(verified);
        var unverifiedWeight = calculator.CalculateWeight(unverified);

        Assert.True(verifiedWeight.Components.VerificationStatus > unverifiedWeight.Components.VerificationStatus);
    }

    [Fact]
    public void CalculateWeight_Corroboration_ShouldScoreHigherWithMoreSources()
    {
        var calculator = new EvidenceWeightCalculator();
        var evidence = CreateEvidence();

        var low = calculator.CalculateWeight(evidence, corroborationCount: 0);
        var high = calculator.CalculateWeight(evidence, corroborationCount: 3);

        Assert.True(high.Components.CorroborationScore > low.Components.CorroborationScore);
    }

    [Fact]
    public void CalculateWeight_Timeliness_ShouldDecayWithAge()
    {
        var calculator = new EvidenceWeightCalculator();
        var recent = CreateEvidence(createdAt: DateTime.UtcNow);
        var old = CreateEvidence(createdAt: DateTime.UtcNow.AddDays(-400));

        var recentWeight = calculator.CalculateWeight(recent);
        var oldWeight = calculator.CalculateWeight(old);

        Assert.True(recentWeight.Components.Timeliness > oldWeight.Components.Timeliness);
    }

    [Fact]
    public void CalculateTotalWeight_ShouldReturnWeightedSum()
    {
        var calculator = new EvidenceWeightCalculator();
        var evidences = new[]
        {
            CreateEvidence(weight: 2.0),
            CreateEvidence(weight: 1.0),
        };

        var total = calculator.CalculateTotalWeight(evidences, _ => 0);

        Assert.True(total > 0);
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
