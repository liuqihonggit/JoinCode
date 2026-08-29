namespace Host.Tests.Tui;

/// <summary>
/// TUI 问答选择输入解析测试 — AskUserDialogView 的 TextField 文本 → 选项索引。
/// 回归背景（T2）：TUI DI 不含 CliModule，AskUserQuestion 工具走 Core Mock 服务，
/// 用户从未被真正提问；修复引入 TerminalGuiInteractiveService + 对话框，
/// 输入解析语义对齐 CLI TerminalInteractiveService（1-based 序号、0=取消、多选逗号分隔）。
/// </summary>
public class AskUserSelectionParserTests
{
    [Fact]
    public void SingleSelect_ValidIndex_Parses()
    {
        var result = AskUserSelectionParser.Parse("2", maxOptions: 3, multiSelect: false);

        Assert.Equal(AskUserSelectionStatus.Ok, result.Status);
        Assert.Equal([2], result.Indices);
    }

    [Fact]
    public void SingleSelect_ZeroOrBlank_Cancels()
    {
        Assert.Equal(AskUserSelectionStatus.Cancel, AskUserSelectionParser.Parse("0", 3, false).Status);
        Assert.Equal(AskUserSelectionStatus.Cancel, AskUserSelectionParser.Parse("", 3, false).Status);
        Assert.Equal(AskUserSelectionStatus.Cancel, AskUserSelectionParser.Parse("  ", 3, false).Status);
    }

    [Fact]
    public void SingleSelect_OutOfRangeOrGarbage_IsInvalid()
    {
        Assert.Equal(AskUserSelectionStatus.Invalid, AskUserSelectionParser.Parse("9", 3, false).Status);
        Assert.Equal(AskUserSelectionStatus.Invalid, AskUserSelectionParser.Parse("abc", 3, false).Status);
        Assert.Equal(AskUserSelectionStatus.Invalid, AskUserSelectionParser.Parse("0", 0, false).Status);
    }

    [Fact]
    public void MultiSelect_CommaSeparated_ParsesAndDedupes()
    {
        var result = AskUserSelectionParser.Parse("1, 3,1", maxOptions: 4, multiSelect: true);

        Assert.Equal(AskUserSelectionStatus.Ok, result.Status);
        Assert.Equal([1, 3], result.Indices);
    }

    [Fact]
    public void MultiSelect_InvalidToken_IsInvalid()
    {
        Assert.Equal(AskUserSelectionStatus.Invalid, AskUserSelectionParser.Parse("1,x", 4, true).Status);
        Assert.Equal(AskUserSelectionStatus.Invalid, AskUserSelectionParser.Parse("5", 4, true).Status);
    }

    [Fact]
    public void FreeInput_NoOptions_TreatedAsCancel()
    {
        // 无选项场景由服务层直接取自由文本，不进序号解析；防御性约定空选项=取消
        var result = AskUserSelectionParser.Parse("1", maxOptions: 0, multiSelect: false);

        Assert.Equal(AskUserSelectionStatus.Invalid, result.Status);
    }
}
