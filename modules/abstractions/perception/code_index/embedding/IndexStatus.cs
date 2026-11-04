namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 向量索引就绪状态 — 供 Agent 层判断查询路径。
/// </summary>
public enum IndexStatus {
    /// <summary>索引未构建或已清空，不可查询。</summary>
    NotReady,
    /// <summary>索引就绪，可正常查询。</summary>
    Ready,
    /// <summary>部分就绪（增量构建中或部分块嵌入失败），可降级查询。</summary>
    Partial,
    /// <summary>索引错误，不可查询，需重建。</summary>
    Error
}
