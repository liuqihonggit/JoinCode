namespace Core.Context;

/// <summary>
/// 查询期自动记忆召回 — TASK127（总账 #42）。
/// <para>对齐基准 <c>findRelevantMemories</c> 的「每回合按当前输入召回相关记忆」，但<b>不调用模型</b>：
/// 直接复用 <see cref="IMemoryManagementService.ScanMemoriesAsync"/>（文件扫描 → 确定性打分 → 截断），
/// 零 token、零延迟。sideQuery 版留作后续替换实现 —— <c>IMemoryRelevanceSelector</c> 是接口，换 DI 即可。</para>
/// <para><b>为什么不污染会话历史</b>：写的是 <see cref="IChatContextManager.AddDynamicSystemMessageAsync"/>，
/// 而 <c>SystemPromptMiddleware</c> 每回合先 <c>ClearDynamicSystemMessagesAsync</c> 再重建，所以内容不会作为
/// 对话回放，也不会动静态前缀（<c>CacheBreakMonitor</c> 监控的那部分）。</para>
/// <para><b>fail-soft 是硬要求</b>：扫描失败、取消、格式化异常一律只打点后放行 ——
/// 召回是锦上添花，任何情况下都不许拖累回合。</para>
/// </summary>
[Register(typeof(IPreparePreprocessMiddleware), ServiceLifetime.Singleton)]
public sealed partial class MemoryRecallMiddleware : ServiceEntity, IPreparePreprocessMiddleware {
    /// <summary>单轮最多注入的记忆条数（基准 ≤5）。</summary>
    internal const int MaxMemories = 5;

    /// <summary>注入文本总字符上限 —— 这是本行为唯一的上下文成本。</summary>
    internal const int MaxTotalChars = 1500;

    /// <summary>单条内容截断长度。</summary>
    internal const int MaxContentChars = 240;

    /// <summary>最高分低于此值视为「全不相关」，宁可不注入。</summary>
    internal const double MinTopScore = 0.15;

    /// <summary>触发召回的最小输入长度（基准用「含空格」，中文没有词边界，改用字符数）。</summary>
    internal const int MinQueryLength = 4;

    private readonly IMemoryManagementService? _memoryService;
    private readonly IChatContextManager? _contextManager;
    private readonly WorkflowConfig? _workflowConfig;

    /// <summary>构造查询期召回中间件。</summary>
    /// <param name="memoryService">记忆管理服务（无记忆子系统的宿主为 null → 恒不召回）。</param>
    /// <param name="contextManager">聊天上下文管理器（写动态系统消息的入口）。</param>
    /// <param name="workflowConfig">工作流配置（读 <c>MemoryAutoRecallEnabled</c>；null 视为开启）。</param>
    public MemoryRecallMiddleware(
        IMemoryManagementService? memoryService = null,
        IChatContextManager? contextManager = null,
        WorkflowConfig? workflowConfig = null) {
        _memoryService = memoryService;
        _contextManager = contextManager;
        _workflowConfig = workflowConfig;
    }

    /// <inheritdoc/>
    public async Task InvokeAsync(PreprocessContext context, MiddlewareDelegate<PreprocessContext> next, CancellationToken ct) {
        try {
            var injected = await RecallAsync(context, ct).ConfigureAwait(false);
            if (injected is not null) {
                Diag.WriteLine($"[MEM-RECALL] inject chars={injected.Length}");
            }
        } catch (OperationCanceledException) {
            Diag.WriteLine("[MEM-RECALL] skip: canceled");
        } catch (Exception ex) {
            // 召回失败绝不上抛 —— 调用方只看到「这一轮没注入」
            Diag.WriteLine($"[MEM-RECALL] fail {ex.GetType().Name}: {ex.Message}");
        }

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>执行召回并写入动态系统消息；未命中任何门时返回 null。</summary>
    internal async Task<string?> RecallAsync(PreprocessContext context, CancellationToken ct) {
        if (_workflowConfig?.MemoryAutoRecallEnabled == false) {
            Diag.WriteLine("[MEM-RECALL] skip: disabled");
            return null;
        }

        if (_memoryService is null || _contextManager is null) {
            Diag.WriteLine("[MEM-RECALL] skip: service-missing");
            return null;
        }

        var message = context.Message?.Trim();
        if (string.IsNullOrEmpty(message)) {
            Diag.WriteLine("[MEM-RECALL] skip: empty");
            return null;
        }

        if (message.StartsWith('/')) {
            Diag.WriteLine("[MEM-RECALL] skip: slash-command");
            return null;
        }

        if (message.Length < MinQueryLength) {
            Diag.WriteLine("[MEM-RECALL] skip: too-short");
            return null;
        }

        var result = await _memoryService
            .ScanMemoriesAsync(message, category: null, limit: MaxMemories, ct)
            .ConfigureAwait(false);

        if (result.TotalMemories <= 0) {
            Diag.WriteLine("[MEM-RECALL] skip: no-memories");
            return null;
        }

        var hits = result.RelevantMemories;
        if (hits.Count == 0) {
            Diag.WriteLine($"[MEM-RECALL] skip: no-hits (scanned={result.TotalMemories})");
            return null;
        }

        if (hits[0].RelevanceScore < MinTopScore) {
            Diag.WriteLine($"[MEM-RECALL] skip: low-score {hits[0].RelevanceScore:F2}");
            return null;
        }

        var text = Format(hits);
        if (string.IsNullOrWhiteSpace(text)) {
            return null;
        }

        await _contextManager.AddDynamicSystemMessageAsync(text, ct).ConfigureAwait(false);
        return text;
    }

    /// <summary>格式化注入文本 —— 条数 ≤ <see cref="MaxMemories"/>、总字符 ≤ <see cref="MaxTotalChars"/>。</summary>
    internal static string Format(IReadOnlyList<DetailedScoredMemory> hits) {
        ArgumentNullException.ThrowIfNull(hits);

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("以下记忆与当前问题相关（系统自动检索，非用户提供）：");

        var used = 0;
        var count = 0;
        foreach (var hit in hits) {
            if (count >= MaxMemories) {
                break;
            }

            var line = $"- {DescribeTitle(hit.Memory)}: {Truncate(hit.Memory.Content, MaxContentChars)}";
            if (used + line.Length + 1 > MaxTotalChars) {
                break;
            }

            builder.AppendLine(line);
            used += line.Length + 1;
            count++;
        }

        return count == 0 ? string.Empty : builder.ToString().TrimEnd();
    }

    /// <summary>标题优先取 <c>Title</c>，缺失时取内容首行前 40 字符。</summary>
    private static string DescribeTitle(MemoryEntry memory) {
        if (!string.IsNullOrWhiteSpace(memory.Title)) {
            return memory.Title.Trim();
        }

        var firstLine = memory.Content.AsSpan();
        var breakIndex = firstLine.IndexOfAny('\r', '\n');
        if (breakIndex >= 0) {
            firstLine = firstLine[..breakIndex];
        }

        return Truncate(firstLine.ToString().Trim(), 40);
    }

    private static string Truncate(string value, int max) {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }

        var collapsed = value.ReplaceLineEndings(" ").Trim();
        return collapsed.Length <= max ? collapsed : string.Concat(collapsed.AsSpan(0, max), "…");
    }
}
