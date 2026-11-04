namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 知识图谱三元组 — (subject, predicate, object) 结构化关系，AI 可直接解析。
/// </summary>
public sealed record GraphTriple {
    /// <summary>主体符号（如调用方）。</summary>
    public required string Subject { get; init; }
    /// <summary>谓词（关系类型，如 "calls"/"depends"/"sameCommunity"/"sameFile"）。</summary>
    public required string Predicate { get; init; }
    /// <summary>客体符号（如被调用方）。</summary>
    public required string Object { get; init; }

    /// <summary>格式化为三元组文本行：(subject, predicate, object)。</summary>
    public override string ToString() => $"({Subject}, {Predicate}, {Object})";
}
