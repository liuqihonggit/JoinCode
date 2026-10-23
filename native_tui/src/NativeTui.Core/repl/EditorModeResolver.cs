namespace NativeTui.Core.Repl;

/// <summary>
/// editorMode 键解析器 — settings.current.editorMode（"vim"/"normal"，对齐 TS GlobalConfig.editorMode
/// 默认 'normal'）持久启用 vim 编辑模式；JCC_TUI_VIM 环境变量灰度保留（TASK066 先例，OR 语义
/// 任一启用即启用 — settings 键为主、灰度为无 settings 时的运行时开关）。
/// <para>纯函数（注入 setting/env 字符串，测试友好）；消费点 Program.cs 装配 ReplShell vimMode 参数。</para>
/// </summary>
public static class EditorModeResolver
{
    /// <summary>JCC_TUI_VIM 灰度环境变量名 — TASK066 板块E 先例。</summary>
    public const string VimEnvVar = "JCC_TUI_VIM";

    /// <summary>editorMode 键的 vim 值 — 对齐 TS 'vim'。</summary>
    public const string VimMode = "vim";

    /// <summary>
    /// 判定是否启用 vim 编辑模式 — settings="vim" 或灰度 env（"1"/"true"）任一命中即启用。
    /// </summary>
    /// <param name="editorModeSetting">settings.current.editorMode 值（null/空/normal/emacs → 不启用）。</param>
    /// <param name="vimEnvValue">JCC_TUI_VIM 环境变量值（null/0/false → 不启用）。</param>
    /// <returns>true 表示装配 VimEngine。</returns>
    public static bool ResolveVimMode(string? editorModeSetting, string? vimEnvValue) =>
        IsVim(editorModeSetting) || IsTruthyGrey(vimEnvValue);

    private static bool IsVim(string? setting) =>
        string.Equals(setting, VimMode, StringComparison.OrdinalIgnoreCase);

    private static bool IsTruthyGrey(string? env) =>
        env is "1" or "true";
}
