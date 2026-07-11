namespace JoinCode.Abstractions.Interfaces;

public interface IGoalApprovalHandler
{
    Task<GoalApprovalResult> ApproveTaskAsync(RuntimeTask task, CancellationToken cancellationToken = default);
}

public sealed class GoalApprovalResult
{
    public bool Approved { get; init; }
    public string? ModifiedDescription { get; init; }
    public string? RejectionReason { get; init; }

    public static GoalApprovalResult Approve() => new() { Approved = true };
    public static GoalApprovalResult ApproveWithModification(string description) => new() { Approved = true, ModifiedDescription = description };
    public static GoalApprovalResult Reject(string? reason = null) => new() { Approved = false, RejectionReason = reason };
}
