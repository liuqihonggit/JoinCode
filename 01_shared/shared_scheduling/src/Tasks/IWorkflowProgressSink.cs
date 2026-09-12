
namespace Core.Scheduling;

/// <summary>
/// Workflow 执行进度推送接口 — 统一聚合各步骤状态变更事件
/// </summary>
public interface IWorkflowProgressSink
{
    /// <summary>步骤开始执行</summary>
    void OnStepStarted(string workflowId, string stepId, string stepName);

    /// <summary>步骤成功完成</summary>
    void OnStepCompleted(string workflowId, string stepId, TimeSpan duration);

    /// <summary>步骤执行失败</summary>
    void OnStepFailed(string workflowId, string stepId, string error, string? errorCode = null);

    /// <summary>步骤被跳过（依赖失败等）</summary>
    void OnStepSkipped(string workflowId, string stepId, string reason);

    /// <summary>步骤重试</summary>
    void OnStepRetried(string workflowId, string stepId, int attempt);
}
