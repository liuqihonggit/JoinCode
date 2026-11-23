
namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// 运行时图变更器 — 允许 Function 节点在执行时动态扩展 GoalGraph
/// </summary>
public interface IGoalGraphMutator {
    /// <summary>添加节点。</summary>
    void AddNode(string nodeId, GoalNodePayload payload);
    /// <summary>添加边。</summary>
    void AddEdge(string edgeId, string fromId, string toId, string? label = null);
    /// <summary>将节点入队。</summary>
    void EnqueueNode(string nodeId);
    /// <summary>添加终止节点。</summary>
    void AddEndNode(string nodeId);
}
