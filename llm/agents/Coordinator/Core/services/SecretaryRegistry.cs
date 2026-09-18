namespace Core.Agents.Coordinator;

/// <summary>
/// 队长秘书注册表 — 管理队长与秘书的映射关系，确保每队长仅 spawn 一次秘书
/// </summary>
internal sealed class SecretaryRegistry
{
    private readonly ConcurrentDictionary<string, string> _secretaries = new(StringComparer.Ordinal);
    private readonly Func<string, SubAgentOptions, CancellationToken, Task<IAgent>> _spawnFunc;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造秘书注册表实例
    /// </summary>
    /// <param name="spawnFunc">spawn 子 Agent 的委托，签名为 (task, options, ct) → IAgent</param>
    /// <param name="logger">可选日志记录器</param>
    public SecretaryRegistry(Func<string, SubAgentOptions, CancellationToken, Task<IAgent>> spawnFunc, ILogger? logger)
    {
        _spawnFunc = spawnFunc;
        _logger = logger;
    }

    /// <summary>
    /// 确保队长秘书已 spawn 常驻 — 复用 ExecutorVariant.Teammate 变体
    /// 秘书职责：队长改热文件时找调用点+批量改+编译自检；整理任务表(DAG)；发广播邮件；记录任务状态
    /// 通信：队长通过 IMailbox 给秘书派活，秘书做完回结果
    /// </summary>
    /// <param name="ownerId">队长标识（goalId 或 agentId）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>秘书的 agentId</returns>
    public async Task<string> EnsureSecretaryAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        if (_secretaries.TryGetValue(ownerId, out var existingSecretaryId))
        {
            return existingSecretaryId;
        }

        const string secretarySystemPrompt = """
            你是队长的秘书，负责处理杂活：
            1. 队长改热文件（接口/枚举/公共签名）时，用 CodeSemanticSearch + grep 找所有调用点，批量连带改，跑编译自检
            2. 整理任务表(DAG)，维护任务状态
            3. 发广播邮件通知其他队员契约变更
            4. 记录任务状态到 TodoWrite DAG
            收到队长的指令后执行，完成后通过邮箱回复结果。不主动发起任务，只响应队长指令。
            """;

        var secretaryOptions = new SubAgentOptions
        {
            Role = AgentRole.Executor,
            Variant = ExecutorVariant.Teammate,
            DisplayName = "秘书",
            SystemPrompt = secretarySystemPrompt,
            SubagentName = $"secretary-{ownerId}",
            GoalId = ownerId,
        };

        var secretary = await _spawnFunc("等待队长指令", secretaryOptions, cancellationToken).ConfigureAwait(false);
        var secretaryId = secretary.ObjectId.UniqueId;
        _secretaries[ownerId] = secretaryId;
        _logger?.LogInformation("{Prefix} 队长 {OwnerId} 的秘书已 spawn: {SecretaryId}", AgentCoordinatorConstants.LogMessages.AgentCoordinatorPrefix, ownerId, secretaryId);
        return secretaryId;
    }

    /// <summary>
    /// 获取队长的秘书 agentId（已 spawn 则返回，未 spawn 则 null）
    /// </summary>
    public string? GetSecretaryId(string ownerId) => _secretaries.TryGetValue(ownerId, out var id) ? id : null;
}
