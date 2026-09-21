
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 集群计划验证器 — 检测文件冲突、DAG环、子任务数量限制
/// </summary>
public interface IClusterPlanValidator {
    /// <summary>验证集群计划。</summary>
    /// <param name="plan">集群计划。</param>
    ClusterPlanValidationResult Validate(ClusterPlan plan);
}