namespace Core.Context;

/// <summary>会话翻页选项（ADR 0167）。</summary>
public sealed record SessionEpochOptions {
    /// <summary>新会话的项目路径（落 meta.json，/resume 列表与 TUI 历史预热依赖它）。</summary>
    public string? ProjectPath { get; init; }
    /// <summary>模型标识（仅记录用途）。</summary>
    public string? ModelId { get; init; }
    /// <summary>供应商（仅记录用途）。</summary>
    public string? Vendor { get; init; }
    /// <summary>是否回收旧代的内存桶 — 无在途查询时 true；有在途查询时 false（旧回合还要写）。</summary>
    public bool ReleaseOldBucket { get; init; } = true;
}

/// <summary>
/// `/clear` 的会话翻页实现（ADR 0167）— 唯一落点，CLI 斜杠命令与 TUI 新会话共用。
/// <para>语义：生成新 sessionId → 切换指针 → 写新会话元数据 → 按需回收旧桶。</para>
/// <para>关键不变式：**不清空旧桶**。旧代的消息与 transcript 原样留在旧 sessionId 下，
/// 在途回合通过 <see cref="ChatQueryScope.SessionId"/> 继续写入旧代；新提交落新代。
/// 这样 `/clear` 在磁盘上真实兑现（resume 旧 id 看得到旧代、新 id 看不到），
/// 且不会像「清当前桶」那样把在途回合的 transcript 静默丢掉。</para>
/// </summary>
public static class SessionEpochSwitcher {
    /// <summary>开启新会话代次并返回新 sessionId。</summary>
    /// <param name="contextManager">上下文管理器（切指针 + 回收旧桶）。</param>
    /// <param name="transcriptService">transcript 服务（null = 不落 meta，测试/无宿主持有场景）。</param>
    /// <param name="options">翻页选项。</param>
    /// <param name="logger">可选日志。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新会话标识。</returns>
    public static async Task<string> BeginNewAsync(
        IChatContextManager contextManager,
        ITranscriptService? transcriptService = null,
        SessionEpochOptions? options = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(contextManager);

        var opts = options ?? new SessionEpochOptions();
        var oldSessionId = contextManager.SessionId;
        var newSessionId = global::Core.Utils.SessionIdFactory.CreateParent(opts.ProjectPath);
        contextManager.SwitchSession(newSessionId);

        if (transcriptService is not null) {
            try {
                await transcriptService.EnsureSessionAsync(newSessionId, new JoinCode.Abstractions.Interfaces.SessionInfo {
                    Id = newSessionId,
                    ProjectPath = opts.ProjectPath ?? Environment.CurrentDirectory,
                    ModelId = opts.ModelId ?? string.Empty,
                    Vendor = opts.Vendor ?? string.Empty,
                    CreatedAt = DateTime.UtcNow,
                }, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                // 元数据失败不阻断 /clear — 降级为「无 meta 的新会话」，transcript 仍能落盘
                logger?.LogWarning(ex, "[SESSION-EPOCH] 新会话元数据落盘失败（{SessionId}），继续翻页", newSessionId);
            }
        }

        if (opts.ReleaseOldBucket && !string.Equals(oldSessionId, newSessionId, StringComparison.Ordinal)) {
            contextManager.ReleaseSession(oldSessionId);
        }

        logger?.LogInformation("[SESSION-EPOCH] 会话翻页 {Old} → {New}（释放旧桶={Released}）",
            oldSessionId, newSessionId, opts.ReleaseOldBucket);
        return newSessionId;
    }
}
