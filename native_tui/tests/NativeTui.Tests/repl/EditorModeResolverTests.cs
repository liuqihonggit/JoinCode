namespace NativeTui.Tests.Repl;

/// <summary>
/// editorMode 键接线测试 — settings.current.editorMode="vim" 持久启用 vim 编辑模式
/// （对齐 TS GlobalConfig.editorMode: 'normal'|'vim' 默认 'normal'）；
/// JCC_TUI_VIM 环境变量灰度保留（TASK066 先例，OR 语义任一启用即启用）。
/// </summary>
public sealed class EditorModeResolverTests
{
    /// <summary>settings editorMode="vim" → 启用。</summary>
    [Theory]
    [InlineData("vim", null, true)]
    [InlineData("vim", "0", true)]
    [InlineData("vim", "", true)]
    public void Resolve_SettingsVim_EnablesVim(string? setting, string? env, bool expected)
    {
        Assert.Equal(expected, EditorModeResolver.ResolveVimMode(setting, env));
    }

    /// <summary>环境变量灰度（无 settings）→ 启用 — JCC_TUI_VIM 先例保留。</summary>
    [Theory]
    [InlineData(null, "1", true)]
    [InlineData(null, "true", true)]
    [InlineData("normal", "1", true)]
    public void Resolve_EnvGrey_EnablesVim(string? setting, string? env, bool expected)
    {
        Assert.Equal(expected, EditorModeResolver.ResolveVimMode(setting, env));
    }

    /// <summary>默认 normal：settings 缺省/normal + 无灰度 → 关闭（对齐 TS 默认 'normal'）。</summary>
    [Theory]
    [InlineData(null, null, false)]
    [InlineData(null, "0", false)]
    [InlineData(null, "false", false)]
    [InlineData("normal", null, false)]
    [InlineData("", null, false)]
    [InlineData("emacs", null, false)]
    public void Resolve_DefaultNormal_DisablesVim(string? setting, string? env, bool expected)
    {
        Assert.Equal(expected, EditorModeResolver.ResolveVimMode(setting, env));
    }
}
