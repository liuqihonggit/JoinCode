namespace McpToolDispatch;

/// <summary>
/// JSON 对象流式构建器 — AOT 友好(无反射/emit),替代手拼 StringBuilder + JsonEscapeString 样板
/// <para>用法: new GitHubJsonObjectBuilder().String("title", title).String("head", head).BoolIfTrue("draft", draft).Build()</para>
/// <para>消除 4 个 Create 方法的 StringBuilder 手拼重复(IssueCreate/PrCreate/ReleaseCreate/RepoCreate)</para>
/// </summary>
internal sealed class GitHubJsonObjectBuilder {
    private readonly StringBuilder _sb = new(256);
    private bool _first = true;

    public GitHubJsonObjectBuilder() {
        _sb.Append('{');
    }

    /// <summary>
    /// 添加字符串字段(无条件)
    /// </summary>
    public GitHubJsonObjectBuilder String(string name, string value) {
        AppendComma();
        _sb.Append('"').Append(name).Append("\":").Append(EscapeString(value));
        return this;
    }

    /// <summary>
    /// 添加字符串字段(仅当 value 非空白时)
    /// </summary>
    public GitHubJsonObjectBuilder StringIf(string name, string? value) {
        if (!string.IsNullOrWhiteSpace(value)) String(name, value);
        return this;
    }

    /// <summary>
    /// 添加布尔字段(无条件)
    /// </summary>
    public GitHubJsonObjectBuilder Bool(string name, bool value) {
        AppendComma();
        _sb.Append('"').Append(name).Append("\":").Append(value ? "true" : "false");
        return this;
    }

    /// <summary>
    /// 添加布尔字段(仅当 value == true 时)
    /// </summary>
    public GitHubJsonObjectBuilder BoolIfTrue(string name, bool? value) {
        if (value == true) Bool(name, true);
        return this;
    }

    /// <summary>
    /// 添加原始 JSON 片段(无条件) — 用于嵌套数组/对象,如 Raw("labels", "[\"bug\",\"feat\"]")
    /// </summary>
    public GitHubJsonObjectBuilder Raw(string name, string rawJson) {
        AppendComma();
        _sb.Append('"').Append(name).Append("\":").Append(rawJson);
        return this;
    }

    /// <summary>
    /// 构建最终 JSON 字符串(闭合大括号)
    /// </summary>
    public string Build() {
        _sb.Append('}');
        return _sb.ToString();
    }

    private void AppendComma() {
        if (_first) _first = false;
        else _sb.Append(',');
    }

    /// <summary>
    /// JSON 字符串转义 — 包裹双引号,转义控制字符(AOT 友好,替代 JsonSerializer.Serialize)
    /// </summary>
    internal static string EscapeString(string value) {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value) {
            switch (c) {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                if (c < 0x20) sb.Append($"\\u{(int)c:X4}");
                else sb.Append(c);
                break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
