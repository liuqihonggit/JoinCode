namespace McpToolDispatch;

/// <summary>
/// 管道上下文 — 贯穿所有步骤的共享状态,避免步骤间重复传参
/// </summary>
internal sealed class LogContext {
    /// <summary>工具名(页眉显示)</summary>
    public string? ToolName { get; init; }
    /// <summary>跳过前 N 行(偏移量分页)</summary>
    public int? SkipLines { get; init; }
    /// <summary>从第 N 行开始(1-indexed,等价 skip_lines=N-1)</summary>
    public int? StartLine { get; init; }
    /// <summary>每页行数(JCC_PAGE_SIZE 默认20)</summary>
    public int PageSize { get; init; }
    /// <summary>字符安全阀(JCC_MAX_OUTPUT_CHARS 默认30000)</summary>
    public int CharThreshold { get; init; }
    /// <summary>是否加行号</summary>
    public bool AddLineNumbers { get; init; }
    /// <summary>行号前缀格式: true=tab, false=箭头</summary>
    public bool CompactLinePrefix { get; init; }

    // ── 运行时状态(步骤间共享) ──
    /// <summary>实际偏移量(start_line-1 或 skip_lines)</summary>
    public int EffectiveSkip { get; set; }
    /// <summary>总行数(PaginationStep 设置)</summary>
    public int TotalLines { get; set; }
    /// <summary>显示行数(PaginationStep 设置)</summary>
    public int ShownLines { get; set; }
    /// <summary>是否截断(PaginationStep 设置)</summary>
    public bool HasMore { get; set; }
}
