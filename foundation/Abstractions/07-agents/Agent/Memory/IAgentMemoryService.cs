
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Agent 记忆服务接口 — 对齐 TS agentMemory.ts + agentMemorySnapshot.ts
/// 管理 Agent 的三种作用域记忆（user/project/local）和快照机制
/// </summary>
public interface IAgentMemoryService
{
    /// <summary>
    /// 获取 Agent 记忆目录路径
    /// </summary>
    string GetAgentMemoryDir(string agentType, AgentMemoryScope scope);

    /// <summary>
    /// 获取 Agent 记忆入口文件路径（MEMORY.md）
    /// </summary>
    string GetAgentMemoryEntrypoint(string agentType, AgentMemoryScope scope);

    /// <summary>
    /// 判断路径是否在 Agent 记忆目录内（安全检查）
    /// </summary>
    bool IsAgentMemoryPath(string absolutePath);

    /// <summary>
    /// 获取作用域的显示文本
    /// </summary>
    string GetMemoryScopeDisplay(AgentMemoryScope? scope);

    /// <summary>
    /// 加载 Agent 记忆提示词 — 包含记忆目录信息和行为指导
    /// </summary>
    Task<string> LoadAgentMemoryPromptAsync(string agentType, AgentMemoryScope scope, CancellationToken ct = default);

    /// <summary>
    /// 确保 Agent 记忆目录存在
    /// </summary>
    Task EnsureMemoryDirExistsAsync(string agentType, AgentMemoryScope scope, CancellationToken ct = default);

    /// <summary>
    /// 检查 Agent 记忆快照状态
    /// </summary>
    Task<AgentMemorySnapshotCheck> CheckSnapshotAsync(string agentType, AgentMemoryScope scope, CancellationToken ct = default);

    /// <summary>
    /// 从快照初始化 Agent 记忆（首次使用）
    /// </summary>
    Task InitializeFromSnapshotAsync(string agentType, AgentMemoryScope scope, string snapshotTimestamp, CancellationToken ct = default);

    /// <summary>
    /// 用快照替换本地 Agent 记忆
    /// </summary>
    Task ReplaceFromSnapshotAsync(string agentType, AgentMemoryScope scope, string snapshotTimestamp, CancellationToken ct = default);

    /// <summary>
    /// 标记快照已同步（不修改本地记忆）
    /// </summary>
    Task MarkSnapshotSyncedAsync(string agentType, AgentMemoryScope scope, string snapshotTimestamp, CancellationToken ct = default);
}
