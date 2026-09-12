namespace JoinCode.Abstractions.Shell;

/// <summary>
/// 终端颜色兼容类 — 提供与 CLI TerminalColors 相同的字符串属性，
/// 命令类移到 Hands 后通过 global using 别名 TerminalColors = JoinCode.Abstractions.Shell.CommandTerminalColors 使用。
/// 值为 ANSI 24位前景色转义序列 (\x1b[38;2;R;G;Bm)，与 CLI TerminalColors 对齐。
/// </summary>
public static class CommandTerminalColors
{
    public static string Error => "\x1b[38;2;255;107;128m";
    public static string Success => "\x1b[38;2;78;186;101m";
    public static string Warning => "\x1b[38;2;255;193;7m";
    public static string Primary => "\x1b[38;2;215;119;87m";
    public static string Muted => "\x1b[38;2;153;153;153m";
    public static string Accent => "\x1b[38;2;177;185;249m";
    public static string Secondary => "\x1b[38;2;177;185;249m";
    public static string Info => "\x1b[38;2;72;150;140m";
    public static string Inactive => "\x1b[38;2;153;153;153m";
    public static string Divider => "\x1b[38;2;80;80;80m";
}
