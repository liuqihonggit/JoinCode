namespace NativeTui.Tests.Repl;

/// <summary>
/// 工具标题行摘要派生测试（S2 工具折叠渲染域）— ToolTitleFormatter per-tool Args 解析：
/// Bash 命令 2 行/160 字符两级截断+尾 …（BashTool/UI.tsx L104-127）；Read/Write/Edit 路径提取；
/// Grep/Glob pattern+path 引号包裹（GrepTool/UI.tsx L141-145）；Edit Create/Update 派生
///（FileEditTool/UI.tsx old_string 空 = Create）；纯文本降级原文（模拟引擎/旧调用兼容）；
/// Write 结果句式 + countLines 尾换行终结语义；ToolCallItem 集成（显示名/摘要/多行高度缓存）。
/// </summary>
public sealed class ToolTitleFormatterTests
{
    [Fact]
    public void Bash_ShortCommand_ReturnedUntruncated()
    {
        // 短命令（JSON command 提取）— 原样返回无截断标记
        var result = ToolTitleFormatter.Format("Bash", """{"command":"dotnet build"}""");
        Assert.Equal("dotnet build", result);
    }

    [Fact]
    public void Bash_MultiLineCommand_TruncatesToTwoLinesWithEllipsis()
    {
        // 3 行命令 → 前 2 行 + "…"（MAX_COMMAND_DISPLAY_LINES=2）
        var command = "echo one\necho two\necho three";
        var result = ToolTitleFormatter.Format("Bash", $$"""{"command":"{{command.Replace("\n", "\\n")}}"}""");

        Assert.Equal("echo one\necho two…", result);
    }

    [Fact]
    public void Bash_OverCharLimit_TruncatesTo160WithEllipsis()
    {
        // 单行 200 字符 → 截断到 160 + "…"（MAX_COMMAND_DISPLAY_CHARS=160）
        var command = new string('a', 200);
        var result = ToolTitleFormatter.Format("Bash", $$"""{"command":"{{command}}"}""");

        Assert.Equal($"{new string('a', 160)}…", result);
    }

    [Fact]
    public void Bash_PlainTextArgs_CommandTruncationStillApplies()
    {
        // 纯文本降级 — Args 原文即命令，截断保护同样生效（模拟引擎场景）
        var result = ToolTitleFormatter.Format("Bash", "plain\ntext\ncommand");

        Assert.Equal("plain\ntext…", result);
    }

    [Fact]
    public void Bash_Exact160Chars_NoEllipsis()
    {
        // 恰好 160 字符 — 未超限不加 …（> 判定，非 >=）
        var command = new string('a', 160);
        var result = ToolTitleFormatter.Format("Bash", $$"""{"command":"{{command}}"}""");

        Assert.Equal(command, result);
    }

    [Fact]
    public void Read_JsonArgs_ExtractsFilePath()
    {
        var result = ToolTitleFormatter.Format("Read", """{"file_path":"src/NativeTui.Core/Theme.cs"}""");

        Assert.Equal("src/NativeTui.Core/Theme.cs", result);
    }

    [Fact]
    public void Read_PlainTextArgs_FallsBackToRaw()
    {
        // 纯文本（模拟引擎/旧调用 "f.cs"）— 原文降级不破坏既有渲染断言
        var result = ToolTitleFormatter.Format("Read", "src/main.rs");

        Assert.Equal("src/main.rs", result);
    }

    [Fact]
    public void Grep_QuotesPatternAndPath()
    {
        // pattern + path 双字段 — 引号包裹逗号分隔（GrepTool/UI.tsx parts.join(', ')）
        var result = ToolTitleFormatter.Format("Grep", """{"pattern":"TODO","path":"src"}""");

        Assert.Equal("pattern: \"TODO\", path: \"src\"", result);
    }

    [Fact]
    public void Glob_PatternOnly_omitsPath()
    {
        // 无 path — 仅 pattern 段（GlobTool/UI.tsx L29）
        var result = ToolTitleFormatter.Format("Glob", """{"pattern":"**/*.cs"}""");

        Assert.Equal("pattern: \"**/*.cs\"", result);
    }

    [Fact]
    public void Write_ExtractsFilePath_FromJson()
    {
        var result = ToolTitleFormatter.Format("Write", """{"file_path":"docs/adr.md"}""");

        Assert.Equal("docs/adr.md", result);
    }

    [Fact]
    public void UnknownTool_ReturnsArgsRaw()
    {
        // 未知工具名防御降级 — 引擎新增工具不崩
        var result = ToolTitleFormatter.Format("NotebookEdit", "whatever");

        Assert.Equal("whatever", result);
    }

    [Fact]
    public void EditLabel_EmptyOldString_DerivesCreate()
    {
        // old_string 空 = 新建文件 → Create（FileEditTool/UI.tsx userFacingName）
        var result = ToolTitleFormatter.DeriveEditLabel("""{"file_path":"new.cs","old_string":"","new_string":"x"}""");

        Assert.Equal("Create", result);
    }

    [Fact]
    public void EditLabel_NonEmptyOldString_DerivesUpdate()
    {
        var result = ToolTitleFormatter.DeriveEditLabel("""{"file_path":"a.cs","old_string":"old","new_string":"new"}""");

        Assert.Equal("Update", result);
    }

    [Fact]
    public void EditLabel_PlainTextArgs_DerivesUpdate()
    {
        // 纯文本降级 — 无 old_string 可判 → Update（基准 input null 同值）
        Assert.Equal("Update", ToolTitleFormatter.DeriveEditLabel("demo/simulated.cs"));
    }

    [Fact]
    public void WriteResultSummary_ChineseSentenceForm()
    {
        // "写入 N 行到 path" 句式（Wrote N lines to path 汉化 — Driver 组 Summary 消费）
        Assert.Equal("写入 12 行到 src/gen.cs", ToolTitleFormatter.WriteResultSummary(12, "src/gen.cs"));
    }

    [Fact]
    public void CountLines_TrailingNewlineIsTerminator()
    {
        // 尾换行 = 行终结符不计新行（FileWriteTool/UI.tsx countLines L35-38；空串 = 1 行同基准 split 口径）
        Assert.Equal(1, ToolTitleFormatter.CountLines("a\n"));
        Assert.Equal(2, ToolTitleFormatter.CountLines("a\nb\n"));
        Assert.Equal(3, ToolTitleFormatter.CountLines("a\nb\nc"));
        Assert.Equal(1, ToolTitleFormatter.CountLines(""));
    }

    [Fact]
    public void ToolCallItem_Edit_DerivesDisplayNameFromArgs()
    {
        // ToolCallItem 集成 — Edit 显示名 Create/Update 派生（old_string 空 = Create）
        var create = new ToolCallItem("Edit", """{"file_path":"new.cs","old_string":"","new_string":"x"}""");
        var update = new ToolCallItem("Edit", """{"file_path":"a.cs","old_string":"o","new_string":"n"}""");

        Assert.Equal("Create", create.DisplayToolName);
        Assert.Equal("Update", update.DisplayToolName);
        Assert.Equal("new.cs", create.DisplayArgs);
        Assert.Equal("a.cs", update.DisplayArgs);
        Assert.Equal("Edit", create.ToolName);
    }

    [Fact]
    public void ToolCallItem_MultiLineCommand_MeasuresAndRendersTwoRows()
    {
        // 多行命令显示语义 — 2 行命令高度 3（margin + 首行 + 续行），续行缩进对齐参数列
        using var screen = new Screen(cols: 40, rows: 5);
        var item = new ToolCallItem("Bash", "echo one\necho two");

        Assert.Equal(3, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        Assert.Equal("● Bash(echo one", RowText.Of(screen, 1, 0, 15));
        // 续行列 = 工具名后括号列（● 2 + "Bash" 4 = col 6）起，末行闭合括号
        Assert.Equal("echo two)", RowText.Of(screen, 2, 6, 9));
    }

    [Fact]
    public void ToolCallItem_SingleLineArgs_KeepsLegacyRender()
    {
        // 单行参数 — 与既有渲染完全一致（● Name(args) 闭括号同行）
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new ToolCallItem("Read", "src/main.rs");

        Assert.Equal(2, item.MeasureHeight(40));
        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        Assert.Equal("● Read(src/main.rs)", RowText.Of(screen, 1, 0, 19));
    }
}
