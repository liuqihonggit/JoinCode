// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.Reasoning.Tests.Weight;

public sealed class BayesianEvidenceUpdaterTests {
    [Fact]
    public void UpdateBelief_ShouldCreatePosteriorForNewEvidence() {
        var updater = new BayesianEvidenceUpdater();

        var result = updater.UpdateBelief("ev1", 0.8, 0.1);

        Assert.InRange(result.Mean, 0, 1);
        Assert.True(result.Variance > 0);
    }

    [Fact]
    public void UpdateBelief_ShouldShiftMeanTowardsLikelihood() {
        var updater = new BayesianEvidenceUpdater();

        var result = updater.UpdateBelief("ev1", 0.9, 0.1);

        Assert.True(result.Mean > 0.5);
    }

    [Fact]
    public void UpdateBelief_ShouldReduceVarianceWithMultipleUpdates() {
        var updater = new BayesianEvidenceUpdater();

        var r1 = updater.UpdateBelief("ev1", 0.7, 0.1);
        var r2 = updater.UpdateBelief("ev1", 0.8, 0.1);

        Assert.True(r2.Variance < r1.Variance);
    }

    [Fact]
    public void PropagateBelief_ShouldAdjustRelatedBeliefs() {
        var updater = new BayesianEvidenceUpdater();
        updater.UpdateBelief("ev1", 0.9, 0.1);
        updater.UpdateBelief("ev2", 0.3, 0.1);

        var before = updater.GetBelief("ev2")!.Mean;
        updater.PropagateBelief("ev1", 0.5, ["ev2"]);
        var after = updater.GetBelief("ev2")!.Mean;

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void GetAverageVariance_ShouldReturnAverage() {
        var updater = new BayesianEvidenceUpdater();
        updater.UpdateBelief("ev1", 0.7, 0.1);
        updater.UpdateBelief("ev2", 0.8, 0.1);

        var avg = updater.GetAverageVariance();

        Assert.True(avg > 0);
        Assert.True(avg <= 0.25);
    }

    [Fact]
    public void UpdateFromEvidence_ShouldUseWeightCalculator() {
        var updater = new BayesianEvidenceUpdater();
        var evidence = new EvidenceRecord {
            Content = "测试证据",
            Category = EvidenceCategory.Documentary,
            TrustLevel = TrustLevel.DirectEvidence,
            SubmittedBy = AgentRole.Prosecutor,
        };

        var result = updater.UpdateFromEvidence(evidence);

        Assert.InRange(result.Mean, 0, 1);
    }

    [Fact]
    public void UpdateGaussian_ShouldComputeStandardPosterior() {
        // prior=(0.5, 0.25), likelihood=(0.8, 0.1)
        // posteriorVariance = 1/(1/0.25 + 1/0.1) = 1/14 ≈ 0.07142857
        // posteriorMean = 0.07142857 * (0.5/0.25 + 0.8/0.1) = 0.07142857 * 10 = 0.7142857
        var prior = new Posterior { Mean = 0.5, Variance = 0.25 };

        var result = BayesianEvidenceUpdater.UpdateGaussian(prior, 0.8, 0.1);

        Assert.Equal(0.7142857, result.Mean, 0.0001);
        Assert.Equal(0.0714286, result.Variance, 0.0001);
    }

    [Fact]
    public void UpdateGaussian_ShouldClampMeanToAtMostOne() {
        // prior=(0.9, 0.01), likelihood=(2.0, 0.01)
        // posteriorVariance = 1/(100+100) = 0.005
        // posteriorMean = 0.005 * (90 + 200) = 1.45 → clamp 到 1
        var prior = new Posterior { Mean = 0.9, Variance = 0.01 };

        var result = BayesianEvidenceUpdater.UpdateGaussian(prior, 2.0, 0.01);

        Assert.Equal(1.0, result.Mean);
        Assert.Equal(0.005, result.Variance, 0.0001);
    }

    [Fact]
    public void UpdateGaussian_ShouldClampMeanToAtLeastZero() {
        // prior=(0.1, 0.01), likelihood=(-2.0, 0.01)
        // posteriorMean = 0.005 * (10 + (-200)) = -0.95 → clamp 到 0
        var prior = new Posterior { Mean = 0.1, Variance = 0.01 };

        var result = BayesianEvidenceUpdater.UpdateGaussian(prior, -2.0, 0.01);

        Assert.Equal(0.0, result.Mean);
    }

    [Fact]
    public void UpdateGaussian_ShouldClampVarianceToLowerBound() {
        // 极小方差输入 → posteriorVariance < 0.001 → clamp 到 0.001
        var prior = new Posterior { Mean = 0.5, Variance = 0.0001 };

        var result = BayesianEvidenceUpdater.UpdateGaussian(prior, 0.5, 0.0001);

        // posteriorVariance = 1/(10000+10000) = 0.00005 → clamp 0.001
        Assert.Equal(0.001, result.Variance);
    }

    [Fact]
    public void UpdateGaussian_ShouldClampVarianceToUpperBound() {
        // 大方差输入 → posteriorVariance > 0.25 → clamp 到 0.25
        var prior = new Posterior { Mean = 0.5, Variance = 1.0 };

        var result = BayesianEvidenceUpdater.UpdateGaussian(prior, 0.5, 1.0);

        // posteriorVariance = 1/(1+1) = 0.5 → clamp 0.25
        Assert.Equal(0.25, result.Variance);
    }

    [Fact]
    public void UpdateGaussian_ShouldShiftMeanTowardsLikelihood() {
        var prior = new Posterior { Mean = 0.3, Variance = 0.2 };

        var result = BayesianEvidenceUpdater.UpdateGaussian(prior, 0.9, 0.05);

        // 均值应向 likelihood(0.9) 方向移动,大于 prior.Mean(0.3)
        Assert.True(result.Mean > 0.3);
        Assert.True(result.Mean < 0.9);
    }

    [Fact]
    public void UpdateGaussian_ShouldReduceVarianceAfterUpdate() {
        var prior = new Posterior { Mean = 0.5, Variance = 0.25 };

        var result = BayesianEvidenceUpdater.UpdateGaussian(prior, 0.7, 0.1);

        // 后验方差应小于先验方差(信息增加,不确定性降低)
        Assert.True(result.Variance < prior.Variance);
    }
}