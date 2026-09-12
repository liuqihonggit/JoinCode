namespace JoinCode.Reasoning.Weight.Bayesian;

/// <summary>
/// 后验分布 — 高斯共轭模型
/// </summary>
public sealed class Posterior
{
    /// <summary>
    /// 均值
    /// </summary>
    public double Mean { get; set; } = 0.5;

    /// <summary>
    /// 方差（不确定性）
    /// </summary>
    public double Variance { get; set; } = 0.25;
}
