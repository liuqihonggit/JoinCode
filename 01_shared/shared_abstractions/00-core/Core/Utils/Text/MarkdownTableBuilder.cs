namespace JoinCode.Abstractions.Utils.Text;

/// <summary>
/// Markdown 表格构建器 — 统一替代散落各处的 StringBuilder + AppendLine("|...|") 手写模式
/// <para>
/// 用法: new MarkdownTableBuilder().AddHeader("列A","列B").AddRow("1","2").Build()
/// </para>
/// </summary>
public sealed class MarkdownTableBuilder
{
    private readonly List<string> _headers = new();
    private readonly List<string[]> _rows = new();
    private string? _title;

    /// <summary>设置表格标题(可选),渲染为 ## 标题</summary>
    public MarkdownTableBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    /// <summary>添加表头列</summary>
    public MarkdownTableBuilder AddHeader(params string[] columns)
    {
        _headers.Clear();
        _headers.AddRange(columns);
        return this;
    }

    /// <summary>添加数据行(列数应与表头一致,不足补空,超出截断)</summary>
    public MarkdownTableBuilder AddRow(params string[] values)
    {
        _rows.Add(values);
        return this;
    }

    /// <summary>构建 Markdown 表格字符串</summary>
    public string Build()
    {
        if (_headers.Count == 0) return string.Empty;

        var sb = new StringBuilder(256);
        if (_title is not null)
        {
            sb.AppendLine($"## {_title}");
            sb.AppendLine();
        }

        sb.Append('|');
        foreach (var h in _headers)
            sb.Append(' ').Append(Escape(h)).Append(" |");
        sb.AppendLine();

        sb.Append('|');
        for (var i = 0; i < _headers.Count; i++)
            sb.Append("---|");
        sb.AppendLine();

        foreach (var row in _rows)
        {
            sb.Append('|');
            for (var i = 0; i < _headers.Count; i++)
            {
                var val = i < row.Length ? row[i] : string.Empty;
                sb.Append(' ').Append(Escape(val)).Append(" |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>转义单元格内容中的管道符和换行</summary>
    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Contains('|') || value.Contains('\n') || value.Contains('\r')
            ? value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ")
            : value;
    }
}
