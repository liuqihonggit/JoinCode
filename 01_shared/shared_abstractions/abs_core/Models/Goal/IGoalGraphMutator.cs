
namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 运行时图变更器 — 允许 Function 节点在执行时动态扩展 GoalGraph
/// </summary>
public interface IGoalGraphMutator
{
    void AddNode(string nodeId, GoalNodePayload payload);
    void AddEdge(string edgeId, string fromId, string toId, string? label = null);
    void EnqueueNode(string nodeId);
    void AddEndNode(string nodeId);
}
