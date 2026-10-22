namespace NativeTui.Markdown;

/// <summary>
/// 流式 Markdown 增量投影器（方案 B — 块级增量，对齐基准 Markdown.tsx 块边界切分机制）：
/// Markdig 解析累积全文取最后一个顶层块边界为切分点 — 边界前稳定前缀的完整块投影 memoize 追加
///（切分点推进才投影新跨过的块，旧块永不重投影），最后块为增长尾（每 delta 重新解析，
/// 调用方纯文本渲染）；未闭合围栏尾标记 TailOpenFence（渲染 dim 提示态）。
/// 无语法快路径（共享 MarkdigProjector.HasMarkdownSyntax 采样语义 — 流式与落定
/// ProjectOptional 判定一致，视觉无跳变）；相同累积长度直接返回缓存投影
///（每帧 Measure/Render 只投影一次）。
/// Markdig 依赖隔离在本卫星项目（ADR 0140 — Core 零依赖，宿主注入）。
/// </summary>
public sealed class StreamingMarkdownProjector : IStreamingMarkdownProjector
{
    /// <summary>稳定切分点（上次投影纳入稳定前缀的块边界；0 = 无稳定块）。</summary>
    private int _stableSplit;

    /// <summary>稳定块投影累积（memoize — 切分点推进时追加；不可变快照，旧缓存投影引用安全）。</summary>
    private IReadOnlyList<MarkdownBlock> _stableBlocks = [];

    /// <summary>上次投影的累积长度（-1 = 未投影；相同长度直接返回缓存）。</summary>
    private int _projectedLength = -1;

    /// <summary>缓存投影结果（相同累积长度复用 — 帧内重复调用零解析成本）。</summary>
    private StreamingMarkdownProjection? _cached;

    /// <summary>投影累积文本 — 块边界切分：稳定前缀块（memoize 追加）+ 增长尾 + 未闭合围栏标记。</summary>
    public StreamingMarkdownProjection Project(string accumulated)
    {
        if (_cached is not null && _projectedLength == accumulated.Length)
        {
            return _cached;
        }

        if (accumulated.Length < _stableSplit)
        {
            // 防御：累积收缩（流式只追加不会发生）— memoize 态重置防脏缓存
            _stableSplit = 0;
            _stableBlocks = [];
        }

        if (!MarkdigProjector.HasMarkdownSyntax(accumulated))
        {
            // 无语法快路径 — 纯文本全尾零解析（落定 ProjectOptional 同判定降级纯文本项，视觉一致）
            return Cache(new StreamingMarkdownProjection([], accumulated, false), accumulated.Length);
        }

        var document = Markdig.Markdown.Parse(accumulated, MarkdigProjector.SharedPipeline);
        var lastBlock = document.LastOrDefault();
        var split = lastBlock?.Span.Start ?? 0;
        if (split > _stableSplit)
        {
            // 切分点推进 — 新跨过的完整块独立解析追加投影（块级局部性：块边界切片独立解析
            // 与全量解析一致；memoize：旧块列表永不重投影）
            var appended = MarkdigProjector.ProjectBlocks(
                Markdig.Markdown.Parse(accumulated[_stableSplit..split], MarkdigProjector.SharedPipeline));
            _stableBlocks = [.. _stableBlocks, .. appended];
            _stableSplit = split;
        }

        var tail = accumulated[_stableSplit..];
        var openFence = lastBlock is FencedCodeBlock { IsOpen: true };
        return Cache(new StreamingMarkdownProjection(_stableBlocks, tail, openFence), accumulated.Length);
    }

    /// <summary>记录缓存并返回（帧内重复调用复用）。</summary>
    private StreamingMarkdownProjection Cache(StreamingMarkdownProjection projection, int length)
    {
        _cached = projection;
        _projectedLength = length;
        return projection;
    }
}
