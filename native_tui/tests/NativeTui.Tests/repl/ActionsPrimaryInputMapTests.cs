namespace NativeTui.Tests.Repl;

/// <summary>
/// 工具主参数映射测试（消息操作域）— 对齐基准 messageActions.tsx PRIMARY_INPUT（12 工具）：
/// Supports/LabelOf 查表 + Args 容错解析（JSON object 字段提取优先、解析失败首引号回退、
/// Tmux args 数组拼接、Agent prompt 优先 description 兜底 — 本项目参数形态适配）。
/// </summary>
public sealed class ActionsPrimaryInputMapTests
{
    /// <summary>Supports — 12 工具全量命中、未知工具不命中。</summary>
    [Fact]
    public void Supports_TwelveToolsOnly()
    {
        var known = new string[] { "Read", "Edit", "Write", "NotebookEdit", "Bash", "Grep", "Glob", "WebFetch", "WebSearch", "Task", "Agent", "Tmux" };
        Assert.All(known, tool => Assert.True(PrimaryInputMap.Supports(tool)));
        Assert.False(PrimaryInputMap.Supports("TodoWrite"));
        Assert.False(PrimaryInputMap.Supports(string.Empty));
    }

    /// <summary>LabelOf — 主参数标签（path/command/pattern/url/query/prompt）。</summary>
    [Fact]
    public void LabelOf_ReturnsPrimaryLabels()
    {
        Assert.Equal("path", PrimaryInputMap.LabelOf("Read"));
        Assert.Equal("command", PrimaryInputMap.LabelOf("Bash"));
        Assert.Equal("pattern", PrimaryInputMap.LabelOf("Grep"));
        Assert.Equal("url", PrimaryInputMap.LabelOf("WebFetch"));
        Assert.Equal("query", PrimaryInputMap.LabelOf("WebSearch"));
        Assert.Equal("prompt", PrimaryInputMap.LabelOf("Agent"));
        Assert.Null(PrimaryInputMap.LabelOf("TodoWrite"));
    }

    /// <summary>JSON object 提取 — 各工具主参数字段命中。</summary>
    [Fact]
    public void Extract_JsonObject_ReadsMappedField()
    {
        Assert.Equal("src/main.rs", PrimaryInputMap.ExtractPrimaryInput("Read", "{\"file_path\":\"src/main.rs\",\"offset\":5}"));
        Assert.Equal("ls -la", PrimaryInputMap.ExtractPrimaryInput("Bash", "{\"command\":\"ls -la\"}"));
        Assert.Equal("TODO", PrimaryInputMap.ExtractPrimaryInput("Grep", "{\"pattern\":\"TODO\"}"));
        Assert.Equal("https://example.com", PrimaryInputMap.ExtractPrimaryInput("WebFetch", "{\"url\":\"https://example.com\"}"));
    }

    /// <summary>NotebookEdit — notebook_path 字段（path 标签的差异化字段名）。</summary>
    [Fact]
    public void Extract_NotebookEdit_ReadsNotebookPath()
    {
        Assert.Equal("nb.ipynb", PrimaryInputMap.ExtractPrimaryInput("NotebookEdit", "{\"notebook_path\":\"nb.ipynb\"}"));
    }

    /// <summary>Agent — prompt 优先命中（基准语义）。</summary>
    [Fact]
    public void Extract_Agent_PromptTakesPriority()
    {
        Assert.Equal("搜索全部用法", PrimaryInputMap.ExtractPrimaryInput("Agent",
            "{\"description\":\"搜索\",\"prompt\":\"搜索全部用法\"}"));
    }

    /// <summary>Agent — 无 prompt 时 description 兜底（本项目引擎 Agent 调用参数形态适配）。</summary>
    [Fact]
    public void Extract_Agent_FallsBackToDescription()
    {
        Assert.Equal("搜索", PrimaryInputMap.ExtractPrimaryInput("Agent", "{\"description\":\"搜索\"}"));
    }

    /// <summary>Tmux — args 数组拼接 "tmux a b"（基准字面）。</summary>
    [Fact]
    public void Extract_Tmux_JoinsArgsArray()
    {
        Assert.Equal("tmux ls -la", PrimaryInputMap.ExtractPrimaryInput("Tmux", "{\"args\":[\"ls\",\"-la\"]}"));
    }

    /// <summary>Tmux — 空数组/非字符串元素全滤后无内容 → null。</summary>
    [Fact]
    public void Extract_Tmux_EmptyArray_ReturnsNull()
    {
        Assert.Null(PrimaryInputMap.ExtractPrimaryInput("Tmux", "{\"args\":[]}"));
    }

    /// <summary>解析失败（非 JSON 简写摘要）— 首个双引号内字符串回退。</summary>
    [Fact]
    public void Extract_NonJson_FallsBackToFirstQuoted()
    {
        Assert.Equal("echo hi", PrimaryInputMap.ExtractPrimaryInput("Bash", "摘要 \"echo hi\" 其后"));
        Assert.Null(PrimaryInputMap.ExtractPrimaryInput("Bash", "无引号摘要"));
    }

    /// <summary>合法 JSON 字符串形态（带引号整体）— 首引号回退提取内容。</summary>
    [Fact]
    public void Extract_JsonString_FallsBackToQuotedContent()
    {
        Assert.Equal("run.sh", PrimaryInputMap.ExtractPrimaryInput("Bash", "\"run.sh\""));
    }

    /// <summary>合法 JSON object 但映射字段全 miss → null（不回退 — object 内首引号是 key，回退会误提取）。</summary>
    [Fact]
    public void Extract_JsonObjectFieldMiss_ReturnsNullWithoutFallback()
    {
        Assert.Null(PrimaryInputMap.ExtractPrimaryInput("Bash", "{\"description\":\"说明\"}"));
    }

    /// <summary>未映射工具 / 空白参数 → null。</summary>
    [Fact]
    public void Extract_UnknownToolOrBlank_ReturnsNull()
    {
        Assert.Null(PrimaryInputMap.ExtractPrimaryInput("TodoWrite", "{\"content\":\"x\"}"));
        Assert.Null(PrimaryInputMap.ExtractPrimaryInput("Bash", "  "));
        Assert.Null(PrimaryInputMap.ExtractPrimaryInput("Bash", string.Empty));
    }
}
