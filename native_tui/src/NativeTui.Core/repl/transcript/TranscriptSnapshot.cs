namespace NativeTui.Core.Repl;

/// <summary>
/// transcript 冻结快照 — 对齐基准 REPL.tsx handleEnterTranscript（4237-4243：进入 transcript
/// 时冻结 messagesLength + streamingToolUsesLength，handleExitTranscript 4246-4248 退出清空恢复
/// live）。本地收敛为冻结消息全文行集合 + 流式长度；数据经注入提供器在进入时一次性拍摄，
/// 由 TranscriptController 防御拷贝隔离 live 原地变更（基准依赖 React 不可变更新的等价物）。
/// </summary>
/// <param name="Lines">冻结的消息全文行（进入时拍摄，transcript 会话内不变）。</param>
/// <param name="StreamingLength">冻结的流式内容长度（0 = 无流式内容）。</param>
public sealed record TranscriptSnapshot(IReadOnlyList<string> Lines, int StreamingLength)
{
    /// <summary>内容总行数（滚动 clamp 上界）。</summary>
    public int LineCount => Lines.Count;

    /// <summary>值相等 — 行序列内容逐一相等 + 流式长度（Lines 集合属性默认引用相等，
    /// 快照值语义需内容比较；两个同内容快照判等）。</summary>
    public bool Equals(TranscriptSnapshot? other) =>
        other is not null && StreamingLength == other.StreamingLength && Lines.SequenceEqual(other.Lines);

    /// <summary>值哈希（与 Equals 配套 — 长度 + 流式长度组合）。</summary>
    public override int GetHashCode() => HashCode.Combine(Lines.Count, StreamingLength);
}
