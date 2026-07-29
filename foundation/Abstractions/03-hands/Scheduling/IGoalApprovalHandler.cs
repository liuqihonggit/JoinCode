namespace JoinCode.Abstractions.Interfaces;

public interface IGoalApprovalHandler
{
    Task<OperationResult<string?>> ApproveTaskAsync(RuntimeTask task, CancellationToken cancellationToken = default);
}
