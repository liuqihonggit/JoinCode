namespace JoinCode.Abstractions.Utils;

/// <summary>
/// ANSI 终端控制码枚举（光标、清屏、鼠标、焦点、粘贴、同步更新、滚动、超链接、终端大小）
/// </summary>
public enum AnsiControl
{
    [EnumValue("\x1b[H")] CursorHome,
    [EnumValue("\x1b[2J")] ClearScreen,
    [EnumValue("\x1b[0J")] ClearScreenFromCursor,
    [EnumValue("\x1b[1J")] ClearScreenToCursor,
    [EnumValue("\x1b[2K")] ClearLine,
    [EnumValue("\x1b[0K")] ClearLineFromCursor,
    [EnumValue("\x1b[1K")] ClearLineToCursor,
    [EnumValue("\x1b[?25l")] HideCursor,
    [EnumValue("\x1b[?25h")] ShowCursor,
    [EnumValue("\x1b[?1049h")] EnterAlternateScreen,
    [EnumValue("\x1b[?1049l")] ExitAlternateScreen,
    [EnumValue("\x1b[?1000h")] EnableMouseTracking,
    [EnumValue("\x1b[?1000l")] DisableMouseTracking,
    [EnumValue("\x1b[?1002h")] EnableButtonTracking,
    [EnumValue("\x1b[?1002l")] DisableButtonTracking,
    [EnumValue("\x1b[?1006h")] EnableSgrMouse,
    [EnumValue("\x1b[?1006l")] DisableSgrMouse,
    [EnumValue("\x1b[?1004h")] EnableFocusReporting,
    [EnumValue("\x1b[?1004l")] DisableFocusReporting,
    [EnumValue("\x1b[?2004h")] EnableBracketedPaste,
    [EnumValue("\x1b[?2004l")] DisableBracketedPaste,
    [EnumValue("\x1b[?2026h")] BeginSynchronizedUpdate,
    [EnumValue("\x1b[?2026l")] EndSynchronizedUpdate,
    [EnumValue("\x1b[r")] ResetScrollRegion,
    [EnumValue("\x1b]8;;;\x07")] EndHyperlink,
    [EnumValue("\x1b[18t")] RequestTerminalSize,
}
