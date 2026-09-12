namespace JoinCode.Reasoning.Tests.Weight;

public sealed class BayesianEvidenceUpdaterTests
{
    [Fact]
    public void UpdateBelief_ShouldCreatePosteriorForNewEvidence()
    {
        var updater = new BayesianEvidenceUpdater();

        var result = updater.UpdateBelief("ev1", 0.8, 0.1);

        Assert.InRange(result.Mean, 0, 1);
        Assert.True(result.Variance > 0);
    }

    [Fact]
    public void UpdateBelief_ShouldShiftMeanTowardsLikelihood()
    {
        var updater = new BayesianEvidenceUpdater();

        var result = updater.UpdateBelief("ev1", 0.9, 0.1);

        Assert.True(result.Mean > 0.5);
    }

    [Fact]
    public void UpdateBelief_ShouldReduceVarianceWithMultipleUpdates()
    {
        var updater = new BayesianEvidenceUpdater();

        var r1 = updater.UpdateBelief("ev1", 0.7, 0.1);
        var r2 = updater.UpdateBelief("ev1", 0.8, 0.1);

        Assert.True(r2.Variance < r1.Variance);
    }

    [Fact]
    public void PropagateBelief_ShouldAdjustRelatedBeliefs()
    {
        var updater = new BayesianEvidenceUpdater();
        updater.UpdateBelief("ev1", 0.9, 0.1);
        updater.UpdateBelief("ev2", 0.3, 0.1);

        var before = updater.GetBelief("ev2")!.Mean;
        updater.PropagateBelief("ev1", 0.5, ["ev2"]);
        var after = updater.GetBelief("ev2")!.Mean;

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void GetAverageVariance_ShouldReturnAverage()
    {
        var updater = new BayesianEvidenceUpdater();
        updater.UpdateBelief("ev1", 0.7, 0.1);
        updater.UpdateBelief("ev2", 0.8, 0.1);

        var avg = updater.GetAverageVariance();

        Assert.True(avg > 0);
        Assert.True(avg <= 0.25);
    }

    [Fact]
    public void UpdateFromEvidence_ShouldUseWeightCalculator()
    {
        var updater = new BayesianEvidenceUpdater();
        var evidence = new EvidenceRecord
        {
            Content = "测试证据",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.DirectEvidence,
            SubmittedBy = AgentRole.Prosecutor,
        };

        var result = updater.UpdateFromEvidence(evidence);

        Assert.InRange(result.Mean, 0, 1);
    }
}
