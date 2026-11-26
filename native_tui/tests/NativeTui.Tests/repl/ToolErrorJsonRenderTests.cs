namespace NativeTui.Tests.Repl;

/// <summary>
/// 工具错误 JSON 展示归一门禁（TASK108 P13）— 真机现象：工具权限拒绝时摘要行原样上屏
/// <c>⎿ Error: {"type":"permission_denied","tool":"bash","message":"…"}</c>，用户看到机读结构。
/// <see cref="ToolErrorJson"/> 是有意的数据面契约（模型解析用），不能改；展示层归一为 message。
/// 误伤保护：只处理 `Error: ` 前缀 + JSON object + message 字段三条件同时成立，
/// 普通工具输出（`cat x.json`）与纯文本摘要一律原样。
/// </summary>
public sealed class ToolErrorJsonRenderTests
{
    private const string PermissionDeniedJson =
        "Error: {\"type\":\"permission_denied\",\"error_code\":\"PD002\",\"tool\":\"bash\",\"message\":\"用户拒绝工具执行: bash\"}";

    [Fact]
    public void DisplaySummary_ErrorJson_ExtractsMessage()
    {
        var item = new ToolResultItem(PermissionDeniedJson);

        Assert.Equal("Error: 用户拒绝工具执行: bash", item.DisplaySummary);
    }

    [Fact]
    public void Render_ErrorJson_NoJsonSyntaxOnScreen()
    {
        using var screen = new Screen(cols: 120, rows: 4);
        var item = new ToolResultItem(PermissionDeniedJson);

        item.Render(screen, new Rect(0, 0, 120, 4), Theme.Dark);

        var row = RowText.Of(screen, 1, 0, 120);
        Assert.Contains("用户拒绝工具执行: bash", row);
        Assert.DoesNotContain("\"type\"", row);
        Assert.DoesNotContain("permission_denied", row);
        Assert.DoesNotContain("{", row);
    }

    [Fact]
    public void DisplaySummary_ErrorJsonWithDetails_AppendsDetails()
    {
        var json = "Error: {\"type\":\"invalid_parameter\",\"tool\":\"bash\",\"message\":\"参数非法\","
            + "\"details\":[{\"Key\":\"pattern\",\"Value\":\"rm -rf\"}]}";
        var item = new ToolResultItem(json);

        Assert.Equal("Error: 参数非法（pattern: rm -rf）", item.DisplaySummary);
    }

    [Fact]
    public void DisplaySummary_PlainJsonOutput_Unchanged()
    {
        // 工具输出本身是 JSON（cat package.json 之类）—— 无 `Error: ` 前缀，不得改写
        const string output = "{\"name\":\"app\",\"message\":\"hello\"}";
        var item = new ToolResultItem(output);

        Assert.Equal(output, item.DisplaySummary);
    }

    [Fact]
    public void DisplaySummary_ErrorPrefixButNotJson_Unchanged()
    {
        const string text = "Error: 连接超时 (retry 3)";
        var item = new ToolResultItem(text);

        Assert.Equal(text, item.DisplaySummary);
    }

    [Fact]
    public void DisplaySummary_MalformedJson_FallsBackToRaw()
    {
        const string text = "Error: {\"type\":\"x\",\"message\":";
        var item = new ToolResultItem(text);

        Assert.Equal(text, item.DisplaySummary);
    }

    [Fact]
    public void DisplaySummary_JsonWithoutMessage_FallsBackToRaw()
    {
        const string text = "Error: {\"type\":\"x\",\"tool\":\"bash\"}";
        var item = new ToolResultItem(text);

        Assert.Equal(text, item.DisplaySummary);
    }

    [Fact]
    public void Render_IsErrorJson_ErrorBlockHumanized()
    {
        using var screen = new Screen(cols: 120, rows: 4);
        var item = new ToolResultItem(
            PermissionDeniedJson, null, PermissionDeniedJson, 0, isError: true);

        item.Render(screen, new Rect(0, 0, 120, 4), Theme.Dark);

        var row = RowText.Of(screen, 1, 0, 120);
        Assert.Contains("Error: 用户拒绝工具执行: bash", row);
        Assert.DoesNotContain("\"type\"", row);
    }

    [Fact]
    public void DisplaySummary_DiffStatsStillWins()
    {
        var item = new ToolResultItem(
            PermissionDeniedJson,
            [
                new DiffHunk
                {
                    Header = "@@ -1 +1 @@",
                    Lines =
                    [
                        new DiffLine { Kind = DiffLineKind.Removed, Content = "old" },
                        new DiffLine { Kind = DiffLineKind.Added, Content = "new" },
                    ],
                },
            ],
            null,
            0);

        Assert.Contains("Added", item.DisplaySummary);
        Assert.DoesNotContain("用户拒绝", item.DisplaySummary);
    }
}
