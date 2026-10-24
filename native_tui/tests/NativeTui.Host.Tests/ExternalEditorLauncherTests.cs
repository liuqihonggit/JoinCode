// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace NativeTui.Host.Tests;

/// <summary>
/// ExternalEditorLauncher.ParseEditorCommand 纯函数测试（M1 — $EDITOR 引号路径解析）：
/// 常规形态（空输入兜底/无参数/带参数/前后空白）+ 引号形态（含空格路径+参数/只有引号路径/
/// 残缺引号到串尾/多参数原样/参数内嵌引号原样透传）。纯函数无进程依赖 —
/// StartEditor 进程启动面不在单测覆盖（对齐 Core 侧 ExternalEditorTests 假编辑器委托分层）。
/// </summary>
public sealed class ExternalEditorLauncherTests
{
    /// <summary>常规形态 — 无引号无反斜杠（InlineData 源串可读直书）。</summary>
    [Theory]
    [InlineData("", "", "")]
    [InlineData("   ", "", "")]
    [InlineData("code", "code", "")]
    [InlineData("code -w", "code", "-w")]
    [InlineData("  code  -w  ", "code", "-w")]
    public void ParseEditorCommand_PlainForms_ResolvesExeAndArgs(string editor, string expectedExe, string expectedArgs)
    {
        var (exe, args) = ExternalEditorLauncher.ParseEditorCommand(editor);

        Assert.Equal(expectedExe, exe);
        Assert.Equal(expectedArgs, args);
    }

    /// <summary>引号路径+参数 — 含空格路径剥离引号完整作 exe（旧空白 Split 拆坏形态的修复目标）。</summary>
    [Fact]
    public void ParseEditorCommand_QuotedPathWithArgs_ResolvesFullPath()
    {
        var (exe, args) = ExternalEditorLauncher.ParseEditorCommand("\"C:\\Program Files\\editor\\e.exe\" -w");

        Assert.Equal("C:\\Program Files\\editor\\e.exe", exe);
        Assert.Equal("-w", args);
    }

    /// <summary>只有引号路径 — 完整路径作 exe，无参数。</summary>
    [Fact]
    public void ParseEditorCommand_QuotedPathOnly_ResolvesFullPathWithoutArgs()
    {
        var (exe, args) = ExternalEditorLauncher.ParseEditorCommand("\"C:\\Program Files\\editor\\e.exe\"");

        Assert.Equal("C:\\Program Files\\editor\\e.exe", exe);
        Assert.Equal(string.Empty, args);
    }

    /// <summary>残缺引号到串尾 — 引号后整段当 exe（防御不炸解析；含空格启动失败由 StartEditor 容错降级）。</summary>
    [Fact]
    public void ParseEditorCommand_UnclosedQuote_TreatsRemainderAsExe()
    {
        var (exe, args) = ExternalEditorLauncher.ParseEditorCommand("\"C:\\Program Files\\e.exe -w");

        Assert.Equal("C:\\Program Files\\e.exe -w", exe);
        Assert.Equal(string.Empty, args);
    }

    /// <summary>引号路径 + 多参数 — 余段原样作 args（中间空白保留）。</summary>
    [Fact]
    public void ParseEditorCommand_QuotedPathWithMultipleArgs_PassesArgsVerbatim()
    {
        var (exe, args) = ExternalEditorLauncher.ParseEditorCommand("\"e.exe\" -w --goto file.md");

        Assert.Equal("e.exe", exe);
        Assert.Equal("-w --goto file.md", args);
    }

    /// <summary>参数内嵌引号 — 原样透传（ProcessStartInfo.Arguments 自行处理参数引用）。</summary>
    [Fact]
    public void ParseEditorCommand_ArgsWithEmbeddedQuotes_PassesVerbatim()
    {
        var (exe, args) = ExternalEditorLauncher.ParseEditorCommand("\"e.exe\" -c \"set ts=4\"");

        Assert.Equal("e.exe", exe);
        Assert.Equal("-c \"set ts=4\"", args);
    }

    /// <summary>前导空白 + 引号路径 — TrimStart 后走引号分支正常解析。</summary>
    [Fact]
    public void ParseEditorCommand_LeadingSpacesBeforeQuotedPath_SkipsSpaces()
    {
        var (exe, args) = ExternalEditorLauncher.ParseEditorCommand("   \"C:\\Program Files\\e.exe\"  -w");

        Assert.Equal("C:\\Program Files\\e.exe", exe);
        Assert.Equal("-w", args);
    }
}
