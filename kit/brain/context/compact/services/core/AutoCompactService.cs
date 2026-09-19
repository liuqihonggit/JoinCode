
namespace Core.Context.Compact;

/// <summary>
/// 自动压缩服务 — 薄协调层，通过中间件管道执行压缩策略
/// 核心只保留策略调度和阈值判断
/// 遥测统一在管道 Post 回调中执行
/// </summary>
[Register(typeof(ICompactService), ServiceLifetime.Singleton)]
public sealed partial class AutoCompactService : ServiceEntity, ICompactService {
    private readonly MiddlewarePipeline<CompactContext> _compactPipeline;
    private readonly CompactThresholds _thresholds;
    private readonly IMicrocompactService _microcompactService;
    private int _consecutiveFailures;
    private bool _softCompactNoticed;

    /// <summary>
    /// 初始化 <see cref="AutoCompactService"/> 实例
    /// </summary>
    /// <param name="compactPipeline">压缩中间件管道</param>
    /// <param name="microcompactService">微压缩服务，用于 token 估算</param>
    /// <param name="thresholds">可选阈值配置，null 时使用默认配置</param>
    public AutoCompactService(
        MiddlewarePipeline<CompactContext> compactPipeline,
        IMicrocompactService microcompactService,
        IOptions<CompactThresholds>? thresholds = null) {
        _compactPipeline = compactPipeline ?? throw new ArgumentNullException(nameof(compactPipeline));
        _thresholds = thresholds?.Value ?? CompactThresholds.Default;
        _microcompactService = microcompactService;
    }

    /// <summary>
    /// 执行自动压缩 — 通过中间件管道调度压缩策略
    /// </summary>
    /// <param name="request">压缩请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩结果；所有策略均失败时返回未压缩兜底结果</returns>
    public async Task<CompactResult> CompactAsync(CompactRequest request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Messages.Count == 0) {
            return new CompactResult {
                Compacted = false,
                Level = CompactLevel.None,
                Trigger = request.Trigger,
                PreCompactTokenCount = 0,
                PostCompactTokenCount = 0,
                ErrorMessage = "消息不足以压缩"
            };
        }

        var context = new CompactContext {
            Request = request,
            PreCompactTokens = _microcompactService.EstimateMessageTokens(request.Messages),
            ConsecutiveFailures = _consecutiveFailures
        };

        await _compactPipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        if (context.Result is not null) {
            _consecutiveFailures = context.ConsecutiveFailures;
            return context.Result;
        }

        // 所有中间件均未产生结果 → 全量压缩兜底
        _consecutiveFailures++;
        return new CompactResult {
            Compacted = false,
            Level = CompactLevel.None,
            Trigger = request.Trigger,
            PreCompactTokenCount = context.PreCompactTokens,
            PostCompactTokenCount = context.PreCompactTokens,
            ErrorMessage = "所有压缩策略均失败"
        };
    }

    /// <summary>
    /// 执行部分压缩 — 根据枢轴索引和方向对消息子集生成摘要提示
    /// </summary>
    /// <param name="request">部分压缩请求，包含消息、枢轴索引和方向</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩结果，包含摘要提示和保留消息统计</returns>
    public async Task<CompactResult> PartialCompactAsync(PartialCompactRequest request, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Messages.Count == 0) {
            return new CompactResult {
                Compacted = false,
                Level = CompactLevel.None,
                Trigger = CompactTrigger.Manual,
                PreCompactTokenCount = 0,
                PostCompactTokenCount = 0,
                ErrorMessage = "消息不足以压缩"
            };
        }

        var preCompactTokens = _microcompactService.EstimateMessageTokens(request.Messages);

        var messagesToSummarize = request.Direction == CompactDirection.UpTo
            ? request.Messages.Take(request.PivotIndex).ToList()
            : request.Messages.Skip(request.PivotIndex).ToList();

        var messagesToKeep = request.Direction == CompactDirection.UpTo
            ? request.Messages.Skip(request.PivotIndex).ToList()
            : request.Messages.Take(request.PivotIndex).ToList();

        if (messagesToSummarize.Count == 0) {
            return new CompactResult {
                Compacted = false,
                Level = CompactLevel.None,
                Trigger = CompactTrigger.Manual,
                PreCompactTokenCount = preCompactTokens,
                PostCompactTokenCount = preCompactTokens,
                ErrorMessage = request.Direction == CompactDirection.UpTo
                    ? "选中消息之前没有可摘要的内容"
                    : "选中消息之后没有可摘要的内容"
            };
        }

        var direction = request.Direction == CompactDirection.UpTo
            ? CompactDirection.UpTo
            : CompactDirection.From;

        var prompt = CompactPromptTemplate.GetPartialCompactPrompt(
            request.CustomInstructions,
            direction);

        var postCompactTokens = _microcompactService.EstimateMessageTokens(messagesToKeep);

        return new CompactResult {
            Compacted = true,
            Level = CompactLevel.PartialCompact,
            Trigger = CompactTrigger.Manual,
            Summary = prompt,
            PreCompactTokenCount = preCompactTokens,
            PostCompactTokenCount = postCompactTokens,
            MessagesRemoved = messagesToSummarize.Count,
            MessagesPreserved = messagesToKeep.Count,
            Metadata = new Dictionary<string, JsonElement> {
                ["direction"] = JsonElementHelper.FromString(direction.ToString()),
                ["pivotIndex"] = JsonElementHelper.FromInt32(request.PivotIndex),
                ["hasUserFeedback"] = JsonElementHelper.FromBoolean(!string.IsNullOrEmpty(request.UserFeedback))
            }
        };
    }

    /// <summary>
    /// 判断是否应触发自动压缩 — 基于当前 token 数与上下文窗口阈值的比较
    /// </summary>
    /// <param name="currentTokenCount">当前 token 数</param>
    /// <param name="contextWindowTokens">上下文窗口大小</param>
    /// <returns>应触发返回 true；连续失败超限或未达阈值时返回 false</returns>
    public bool ShouldAutoCompact(int currentTokenCount, int contextWindowTokens) {
        if (_consecutiveFailures >= _thresholds.MaxConsecutiveAutoCompactFailures) {
            return false;
        }

        var effectiveWindow = contextWindowTokens - _thresholds.MaxOutputTokensForSummary;
        var threshold = effectiveWindow - _thresholds.AutoCompactBufferTokens;

        return currentTokenCount >= threshold;
    }

    /// <summary>
    /// 判断是否应展示软压缩提示 — 在软阈值与硬阈值之间首次进入时触发一次
    /// </summary>
    /// <param name="currentTokenCount">当前 token 数</param>
    /// <param name="contextWindowTokens">上下文窗口大小</param>
    /// <returns>应展示提示返回 true；已提示过或未达阈值时返回 false</returns>
    public bool ShouldSoftCompactNotice(int currentTokenCount, int contextWindowTokens) {
        var softThreshold = (int)(contextWindowTokens * _thresholds.SoftCompactRatio);
        var effectiveWindow = contextWindowTokens - _thresholds.MaxOutputTokensForSummary;
        var hardThreshold = effectiveWindow - _thresholds.AutoCompactBufferTokens;

        if (currentTokenCount < softThreshold) {
            _softCompactNoticed = false;
            return false;
        }

        if (currentTokenCount >= hardThreshold) {
            return false;
        }

        if (_softCompactNoticed) {
            return false;
        }

        _softCompactNoticed = true;
        return true;
    }

    /// <summary>
    /// 计算压缩警告状态 — 返回剩余百分比及各阈值越界标志
    /// </summary>
    /// <param name="currentTokenCount">当前 token 数</param>
    /// <param name="contextWindowTokens">上下文窗口大小</param>
    /// <returns>压缩警告状态，包含百分比和各级阈值越界信息</returns>
    public CompactWarningState CalculateWarningState(int currentTokenCount, int contextWindowTokens) {
        var effectiveWindow = contextWindowTokens - _thresholds.MaxOutputTokensForSummary;
        var autoCompactThreshold = effectiveWindow - _thresholds.AutoCompactBufferTokens;
        var threshold = ShouldAutoCompact(currentTokenCount, contextWindowTokens)
            ? autoCompactThreshold
            : effectiveWindow;

        var percentLeft = Math.Max(0, (int)Math.Round(((threshold - currentTokenCount) / (double)threshold) * 100));
        var warningThreshold = threshold - _thresholds.WarningBufferTokens;
        var errorThreshold = threshold - _thresholds.ErrorBufferTokens;
        var blockingLimit = effectiveWindow - _thresholds.ManualCompactBufferTokens;
        var softThreshold = (int)(contextWindowTokens * _thresholds.SoftCompactRatio);

        return new CompactWarningState {
            PercentLeft = percentLeft,
            IsAboveWarningThreshold = currentTokenCount >= warningThreshold,
            IsAboveErrorThreshold = currentTokenCount >= errorThreshold,
            IsAboveAutoCompactThreshold = currentTokenCount >= autoCompactThreshold,
            IsAtBlockingLimit = currentTokenCount >= blockingLimit,
            IsAboveSoftCompactThreshold = currentTokenCount >= softThreshold && currentTokenCount < autoCompactThreshold
        };
    }
}