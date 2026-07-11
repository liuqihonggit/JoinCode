namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 上下文压缩方向 — 对齐 TS PartialCompactDirection
/// </summary>
public enum CompactDirection
{
    /// <summary>从指定点向前压缩（older）</summary>
    [EnumValue("from")] From,

    /// <summary>压缩到指定点（newer）</summary>
    [EnumValue("upTo")] UpTo,
}
