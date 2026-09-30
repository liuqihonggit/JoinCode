namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 语义搜索选项 — AI 通过 MCP 工具动态控制召回策略。
/// <para>默认只返回元数据（文件路径+行号+符号名+相似度分数），与符号索引行为一致。</para>
/// <para>IncludeSourceText=true 时召回结果携带块原文（函数源码）。</para>
/// <para>IncludeParentDocument=true 时召回结果携带父文档原文（类/文件完整源码）。</para>
/// </summary>
public sealed record SearchOptions {
    /// <summary>是否返回块原文（函数源码）— 默认 false（省内存，AI 可用 read 工具按行号读取）。</summary>
    public bool IncludeSourceText { get; init; }
    /// <summary>是否返回父文档原文（类/文件完整源码）— 默认 false（省内存，AI 可用 read 工具按行号读取）。</summary>
    public bool IncludeParentDocument { get; init; }
}
