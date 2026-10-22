namespace NativeTui.Tests.Repl;

/// <summary>
/// S8 权限对话框形态渲染测试 — PermissionRequestItem 的形态分支消费：
/// Bash 形态（"Bash command" 标题 + 命令折行 dim + 危险警告 warning 色 + ctrl+e Esc 变体）/
/// File 形态（"Read|Write|Edit file" 标题 + 路径行 + patch 摘要 +N Success/-M Error）/
/// Fallback 零变化回归（("Bash","run tests") 行布局与 M7-3 单一形态完全一致 — 既有测试零破坏锚点）。
/// </summary>
public sealed class PermissionRequestItemFormTests
{
    /// <summary>构造挂起请求的 coordinator。</summary>
    private static PermissionCoordinator BeginCoordinator(string tool, string description)
    {
        var coordinator = new PermissionCoordinator();
        coordinator.Begin(new PermissionRequest(tool, description));
        return coordinator;
    }

    /// <summary>渲染到独立 Screen（40 列，行数按需；所有权移交调用方 — using 接收）。</summary>
    private static Screen RenderForm(string tool, string description, int rows)
    {
        var screen = new Screen(cols: 40, rows: rows);
        new PermissionRequestItem(BeginCoordinator(tool, description)).Render(screen, new Rect(0, 0, 40, rows), Theme.Dark);
        return screen;
    }

    // ---- Bash 形态 ----

    /// <summary>Bash 形态全布局 — 标题/命令区/危险警告/问题/选项/Esc 变体按序就位。</summary>
    [Fact]
    public void Render_BashForm_FullLayout()
    {
        using var screen = RenderForm("Bash", "需要确认: rm -rf /tmp/x", rows: 15);

        // row2 标题 — bold permission "Bash command"
        var titleStyle = screen.Styles.GetOrAdd(new TextStyle { Bold = true, Foreground = Theme.Dark.Permission });
        Assert.Equal(" Bash command", RowText.Of(screen, 2, 0, 13));
        Assert.Equal(titleStyle, screen[2, 1].StyleId);

        // row5 描述行 dim（M7-3 既有结构保留）— 3 空格 + 4 中文(8 列) + ": " + 12 字符命令
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("   需要确认: rm -rf /tmp/x", RowText.Of(screen, 5, 0, 26));
        Assert.Equal(dim, screen[5, 3].StyleId);

        // row6 命令区 — 3 缩进 dim 等宽全文
        Assert.Equal("   rm -rf /tmp/x", RowText.Of(screen, 6, 0, 16));
        Assert.Equal(dim, screen[6, 3].StyleId);

        // row7 危险警告 — warning 色中文文案（3 空格 + 9 中文 18 列）
        var warning = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Warning });
        Assert.Equal("   可能递归强制删除文件", RowText.Of(screen, 7, 0, 23));
        Assert.Equal(warning, screen[7, 3].StyleId);

        // row9 问题行下移 + row10 焦点选项 + row14 Esc 变体（ctrl+e 提示）
        Assert.Equal(" Do you want to proceed?", RowText.Of(screen, 9, 0, 24));
        Assert.Equal("❯ 1. Yes", RowText.Of(screen, 10, 0, 8));
        Assert.StartsWith(" Esc to cancel · ctrl+e", RowText.Of(screen, 14, 0, 40));
        Assert.Equal(dim, screen[14, 1].StyleId);
    }

    /// <summary>Bash 形态安全命令 — 无危险命中时警告行折叠（问题行紧随命令区）。</summary>
    [Fact]
    public void Render_BashForm_WithoutDanger_WarningRowHidden()
    {
        using var screen = RenderForm("Bash", "需要确认: git status", rows: 14);

        Assert.Equal("   git status", RowText.Of(screen, 6, 0, 13));
        // row7 直接空行、row8 问题行（无警告行占位）
        Assert.Equal(" Do you want to proceed?", RowText.Of(screen, 8, 0, 24));
        Assert.Equal(14, new PermissionRequestItem(BeginCoordinator("Bash", "需要确认: git status")).MeasureHeight(width: 40));
    }

    /// <summary>Bash 形态 MeasureHeight — 骨架 12 + 描述 1 + 命令行数 + 警告 0/1。</summary>
    [Fact]
    public void MeasureHeight_BashForm_WithCommandAndWarning()
    {
        var item = new PermissionRequestItem(BeginCoordinator("Bash", "需要确认: rm -rf /tmp/x"));

        Assert.Equal(15, item.MeasureHeight(width: 40));
    }

    /// <summary>Bash 工具 + 命令文本无载体标记（无引号/冒号）→ Fallback 降级（描述与命令不可区分时保守）。</summary>
    [Fact]
    public void MeasureHeight_BashForm_CommandWithoutCarrier_Fallback()
    {
        var item = new PermissionRequestItem(BeginCoordinator("Bash", "rm -rf /tmp/x"));

        // 无载体提取（引号/冒号皆无）→ Fallback 形态（骨架 12 + 描述 1）— 提取失败降级锚点
        Assert.Equal(PermissionFormKind.Fallback, ToolPermissionForms.Derive("Bash", "rm -rf /tmp/x", width: 40).Kind);
        Assert.Equal(13, item.MeasureHeight(width: 40));
    }

    // ---- File 形态 ----

    /// <summary>File 形态全布局 — 标题/路径行/patch 摘要（+N Success / -M Error）。</summary>
    [Fact]
    public void Render_FileForm_FullLayout()
    {
        using var screen = RenderForm("Edit", "编辑 'src/a.cs' (+3 -1)", rows: 15);

        // row2 标题 — bold permission "Edit file"
        var titleStyle = screen.Styles.GetOrAdd(new TextStyle { Bold = true, Foreground = Theme.Dark.Permission });
        Assert.Equal(" Edit file", RowText.Of(screen, 2, 0, 10));
        Assert.Equal(titleStyle, screen[2, 1].StyleId);

        // row6 路径行 — 3 缩进默认样式
        Assert.Equal("   src/a.cs", RowText.Of(screen, 6, 0, 11));
        Assert.Equal(0, screen[6, 3].StyleId);

        // row7 patch 摘要 — "+3" Success + " -1" Error
        var success = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Success });
        var error = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        Assert.Equal("   +3 -1", RowText.Of(screen, 7, 0, 8));
        Assert.Equal(success, screen[7, 3].StyleId);
        Assert.Equal(error, screen[7, 6].StyleId);

        // row9 问题行 + row14 基准 Esc 行（File 无 ctrl+e 变体）
        Assert.Equal(" Do you want to proceed?", RowText.Of(screen, 9, 0, 24));
        Assert.Equal(" Esc to cancel", RowText.Of(screen, 14, 0, 14));
    }

    /// <summary>File 形态无 patch 统计 — patch 行折叠（路径行后直接空行+问题行）。</summary>
    [Fact]
    public void Render_FileForm_WithoutPatch_PatchRowHidden()
    {
        using var screen = RenderForm("Read", "读取 'docs/readme.md'", rows: 14);

        Assert.Equal(" Read file", RowText.Of(screen, 2, 0, 10));
        Assert.Equal("   docs/readme.md", RowText.Of(screen, 6, 0, 17));
        // row7 空行、row8 问题行（无 patch 行占位）
        Assert.Equal(" Do you want to proceed?", RowText.Of(screen, 8, 0, 24));
        Assert.Equal(14, new PermissionRequestItem(BeginCoordinator("Read", "读取 'docs/readme.md'")).MeasureHeight(width: 40));
    }

    /// <summary>File 形态新增行统计 — patch 只增不删（-M=0 时仅 +N）。</summary>
    [Fact]
    public void Render_FileForm_AddOnlyPatch_ShowsAddedOnly()
    {
        using var screen = RenderForm("Write", "写入 'out/gen.cs' (+7", rows: 15);

        var success = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Success });
        Assert.Equal(" Write file", RowText.Of(screen, 2, 0, 11));
        Assert.Equal("   +7", RowText.Of(screen, 7, 0, 5));
        Assert.Equal(success, screen[7, 3].StyleId);
    }

    // ---- Fallback 零变化回归 ----

    /// <summary>Fallback 形态 — ("Bash","run tests") 行布局与 M7-3 单一形态完全一致（既有测试零破坏锚点；
    /// 48 列屏对齐既有 Render_UnfocusedOptionDimIndexDefaultLabel 断言条件）。</summary>
    [Fact]
    public void Render_FallbackForm_ZeroBehaviorChange()
    {
        using var screen = new Screen(cols: 48, rows: 13);
        new PermissionRequestItem(BeginCoordinator("Bash", "run tests")).Render(screen, new Rect(0, 0, 48, 13), Theme.Dark);

        Assert.Equal(" Tool use", RowText.Of(screen, 2, 0, 9));
        Assert.Equal("   Bash(run tests)", RowText.Of(screen, 4, 0, 18));
        Assert.Equal("   run tests", RowText.Of(screen, 5, 0, 12));
        Assert.Equal(" Do you want to proceed?", RowText.Of(screen, 7, 0, 24));
        Assert.Equal("❯ 1. Yes", RowText.Of(screen, 8, 0, 8));
        Assert.Equal("  2. Yes, and don't ask again for this tool", RowText.Of(screen, 9, 0, 43));
        Assert.Equal("  3. No", RowText.Of(screen, 10, 0, 7));
        Assert.Equal(" Esc to cancel", RowText.Of(screen, 12, 0, 14));
    }

    /// <summary>Fallback MeasureHeight — 与 M7-3 公式一致（13 含描述 / 12 无描述）。</summary>
    [Fact]
    public void MeasureHeight_FallbackForm_Unchanged()
    {
        Assert.Equal(13, new PermissionRequestItem(BeginCoordinator("Bash", "run tests")).MeasureHeight(width: 40));
        Assert.Equal(12, new PermissionRequestItem(BeginCoordinator("Bash", "")).MeasureHeight(width: 40));
    }

    /// <summary>挂起清除后 — Request null 时渲染 no-op（既有语义保留）。</summary>
    [Fact]
    public void Render_RequestCleared_NoOp()
    {
        var coordinator = new PermissionCoordinator();
        var item = new PermissionRequestItem(coordinator);

        using var screen = new Screen(cols: 40, rows: 13);
        item.Render(screen, new Rect(0, 0, 40, 13), Theme.Dark);

        Assert.Equal(new string(' ', 40), RowText.Of(screen, 1, 0, 40));
    }
}
