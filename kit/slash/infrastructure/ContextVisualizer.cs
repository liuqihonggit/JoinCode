namespace JoinCode.Cli;

// ─── ContextData / ContextCategory / ContextVisualizer ───

/// <summary>
/// 上下文数据
/// </summary>
public sealed class ContextData
{
    /// <summary>
    /// 模型名称
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 已使用的总 Token 数
    /// </summary>
    public int TotalTokens { get; set; }

    /// <summary>
    /// 模型上下文窗口最大 Token 数
    /// </summary>
    public int MaxTokens { get; set; }

    /// <summary>
    /// 上下文类别名称
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// 上下文项名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 该上下文项的 Token 数
    /// </summary>
    public int TokenCount { get; set; }

    /// <summary>
    /// 占最大 Token 数的百分比
    /// </summary>
    public double Percentage { get; set; }

    /// <summary>
    /// 上下文类别列表
    /// </summary>
    public List<ContextCategory> Categories { get; set; } = [];
}

/// <summary>
/// 上下文类别
/// </summary>
public sealed class ContextCategory
{
    /// <summary>
    /// 类别名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 该类别的 Token 数
    /// </summary>
    public int TokenCount { get; }

    /// <summary>
    /// 排序序号，越小越靠前
    /// </summary>
    public int SortOrder { get; }

    /// <summary>
    /// 构造上下文类别实例
    /// </summary>
    /// <param name="name">类别名称</param>
    /// <param name="tokenCount">该类别的 Token 数</param>
    /// <param name="sortOrder">排序序号，默认 0</param>
    public ContextCategory(string name, int tokenCount, int sortOrder = 0)
    {
        Name = name;
        TokenCount = tokenCount;
        SortOrder = sortOrder;
    }
}

/// <summary>
/// 上下文可视化器 — CLI 简化版
/// </summary>
public sealed class ContextVisualizer
{
    /// <summary>
    /// 渲染单个上下文数据为带类别条形图的文本
    /// </summary>
    /// <param name="data">上下文数据</param>
    /// <returns>渲染后的文本</returns>
    public string Render(ContextData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleEnumConstants.Bold}Context Window{AnsiStyleEnumConstants.Reset}");
        sb.AppendLine($"  Model: {data.Model}");
        sb.AppendLine($"  Tokens: {data.TotalTokens:N0} / {data.MaxTokens:N0}");

        if (data.Categories.Count > 0)
        {
            sb.AppendLine();
            foreach (var cat in data.Categories.OrderBy(c => c.SortOrder))
            {
                var percentage = data.MaxTokens > 0 ? (double)cat.TokenCount / data.MaxTokens * 100 : 0;
                var bar = new string('█', (int)Math.Max(1, percentage / 5));
                sb.AppendLine($"  {TerminalColors.Primary}{cat.Name,-12}{AnsiStyleEnumConstants.Reset} {bar} {cat.TokenCount:N0} ({percentage:F1}%)");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 渲染多个上下文数据为按 Token 数降序排列的条形图文本
    /// </summary>
    /// <param name="data">上下文数据只读列表</param>
    /// <returns>渲染后的文本</returns>
    public static string Render(IReadOnlyList<ContextData> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleEnumConstants.Bold}Context Window{AnsiStyleEnumConstants.Reset}");
        sb.AppendLine();

        foreach (var d in data.OrderByDescending(d => d.TokenCount))
        {
            var bar = new string('█', (int)Math.Max(1, d.Percentage / 5));
            sb.AppendLine($"  {TerminalColors.Primary}{d.Category,-12}{AnsiStyleEnumConstants.Reset} {bar} {d.TokenCount:N0} ({d.Percentage:F1}%)");
        }

        return sb.ToString();
    }
}
