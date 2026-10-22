namespace NativeTui.Tests.Ansi;

/// <summary>
/// ANSI 转义序列构建器的 golden 测试（M1.2）。
/// 期望值提取自基准源码 src/ink/termio/（csi/osc/sgr/dec）与 screen.ts 的 SGR 常量。
/// </summary>
public sealed class SgrTests
{
    [Fact]
    public void Reset_EmitsZero()
    {
        Assert.Equal("\x1b[0m", Sgr.Reset);
    }

    [Fact]
    public void Bold_EmitsOne()
    {
        Assert.Equal("\x1b[1m", Sgr.Bold);
    }

    [Fact]
    public void Italic_EmitsThree()
    {
        Assert.Equal("\x1b[3m", Sgr.Italic);
    }

    [Fact]
    public void Underline_EmitsFour()
    {
        Assert.Equal("\x1b[4m", Sgr.Underline);
    }

    [Fact]
    public void Dim_EmitsTwo()
    {
        Assert.Equal("\x1b[2m", Sgr.Dim);
    }

    [Fact]
    public void Inverse_EmitsSeven()
    {
        Assert.Equal("\x1b[7m", Sgr.Inverse);
    }

    [Fact]
    public void Strikethrough_EmitsNine()
    {
        Assert.Equal("\x1b[9m", Sgr.Strikethrough);
    }

    [Fact]
    public void ResetBold_EmitsTwentyTwo_BaselineUses22Not21()
    {
        // 基准 screen.ts: BOLD_CODE 关闭用 22（同时关 bold+dim），不用 21
        Assert.Equal("\x1b[22m", Sgr.ResetBold);
    }

    [Fact]
    public void ResetUnderline_EmitsTwentyFour()
    {
        Assert.Equal("\x1b[24m", Sgr.ResetUnderline);
    }

    [Fact]
    public void ResetInverse_EmitsTwentySeven()
    {
        Assert.Equal("\x1b[27m", Sgr.ResetInverse);
    }

    [Fact]
    public void DefaultForeground_EmitsThirtyNine()
    {
        Assert.Equal("\x1b[39m", Sgr.DefaultForeground);
    }

    [Fact]
    public void DefaultBackground_EmitsFortyNine()
    {
        Assert.Equal("\x1b[49m", Sgr.DefaultBackground);
    }

    [Fact]
    public void Foreground_TrueColor_Emits38_2_RGB()
    {
        // Claude 橙 rgb(215,119,87)
        Assert.Equal("\x1b[38;2;215;119;87m", Sgr.Foreground(215, 119, 87));
    }

    [Fact]
    public void Background_TrueColor_Emits48_2_RGB()
    {
        // 用户消息灰底 dark rgb(55,55,55)
        Assert.Equal("\x1b[48;2;55;55;55m", Sgr.Background(55, 55, 55));
    }

    [Theory]
    [InlineData(0, "\x1b[38;5;0m")]
    [InlineData(174, "\x1b[38;5;174m")]
    [InlineData(255, "\x1b[38;5;255m")]
    public void Foreground_Ansi256_Emits38_5_Index(int index, string expected)
    {
        Assert.Equal(expected, Sgr.ForegroundAnsi256(index));
    }

    [Theory]
    [InlineData(231, "\x1b[48;5;231m")]
    public void Background_Ansi256_Emits48_5_Index(int index, string expected)
    {
        Assert.Equal(expected, Sgr.BackgroundAnsi256(index));
    }
}

/// <summary>
/// CSI（Control Sequence Introducer）序列测试 — 光标/擦除/滚动/DECSTBM。
/// </summary>
public sealed class CsiTests
{
    [Fact]
    public void CursorPosition_OneBased_EmitsH()
    {
        Assert.Equal("\x1b[5;10H", Csi.CursorPosition(row: 5, col: 10));
    }

    [Fact]
    public void CursorPosition_Origin_EmitsPlainH()
    {
        Assert.Equal("\x1b[H", Csi.CursorPosition(row: 1, col: 1));
    }

    [Fact]
    public void CursorUp_EmitsA()
    {
        Assert.Equal("\x1b[3A", Csi.CursorUp(3));
    }

    [Fact]
    public void CursorUp_One_OmitsCount()
    {
        Assert.Equal("\x1b[A", Csi.CursorUp(1));
    }

    [Fact]
    public void CursorDown_EmitsB()
    {
        Assert.Equal("\x1b[2B", Csi.CursorDown(2));
    }

    [Fact]
    public void CursorForward_EmitsC()
    {
        Assert.Equal("\x1b[7C", Csi.CursorForward(7));
    }

    [Fact]
    public void CursorBackward_EmitsD()
    {
        Assert.Equal("\x1b[4D", Csi.CursorBackward(4));
    }

    [Fact]
    public void EraseScreen_Full_Emits2J()
    {
        Assert.Equal("\x1b[2J", Csi.EraseScreen());
    }

    [Fact]
    public void EraseLineToEnd_Emits0K()
    {
        Assert.Equal("\x1b[0K", Csi.EraseLineToEnd());
    }

    [Fact]
    public void SetScrollRegion_EmitsDecstm_R()
    {
        // 基准 log-update.ts: DECSTBM CSI top;bot r 用于硬件滚动
        Assert.Equal("\x1b[5;20r", Csi.SetScrollRegion(top: 5, bottom: 20));
    }

    [Fact]
    public void ScrollUp_EmitsS()
    {
        Assert.Equal("\x1b[2S", Csi.ScrollUp(2));
    }

    [Fact]
    public void ScrollDown_EmitsT()
    {
        Assert.Equal("\x1b[3T", Csi.ScrollDown(3));
    }

    [Fact]
    public void SynchronizedOutputBegin_EmitsDec2026()
    {
        Assert.Equal("\x1b[?2026h", Csi.SynchronizedOutputBegin());
    }

    [Fact]
    public void SynchronizedOutputEnd_EmitsDec2026Reset()
    {
        Assert.Equal("\x1b[?2026l", Csi.SynchronizedOutputEnd());
    }
}

/// <summary>
/// DEC 私有模式开关测试 — alt-screen/鼠标/bracketed paste（对齐基准 AlternateScreen.tsx 生命周期）。
/// </summary>
public sealed class DecPrivateModeTests
{
    [Fact]
    public void AltScreen_Enable_Emits1049h_ThenClear()
    {
        // 基准 AlternateScreen: 进入 alt-screen 用 1049h（1049 = 1047+清屏语义）
        Assert.Equal("\x1b[?1049h", DecPrivateMode.Enable(DecPrivateModeKind.AlternateScreen));
    }

    [Fact]
    public void AltScreen_Disable_Emits1049l()
    {
        Assert.Equal("\x1b[?1049l", DecPrivateMode.Disable(DecPrivateModeKind.AlternateScreen));
    }

    [Theory]
    [InlineData(DecPrivateModeKind.MouseClickTracking, "\x1b[?1000h")]
    [InlineData(DecPrivateModeKind.MouseEventTracking, "\x1b[?1002h")]
    [InlineData(DecPrivateModeKind.MouseAnyEventTracking, "\x1b[?1003h")]
    [InlineData(DecPrivateModeKind.MouseSgrEncoding, "\x1b[?1006h")]
    [InlineData(DecPrivateModeKind.BracketedPaste, "\x1b[?2004h")]
    [InlineData(DecPrivateModeKind.CursorVisibility, "\x1b[?25h")]
    [InlineData(DecPrivateModeKind.FocusReporting, "\x1b[?1004h")]
    public void Enable_EmitsQuestionMarkCodeH(DecPrivateModeKind kind, string expected)
    {
        Assert.Equal(expected, DecPrivateMode.Enable(kind));
    }

    [Fact]
    public void Disable_EmitsQuestionMarkCodeL()
    {
        Assert.Equal("\x1b[?1006l", DecPrivateMode.Disable(DecPrivateModeKind.MouseSgrEncoding));
    }

    [Fact]
    public void FocusReporting_Disable_Emits1004l()
    {
        // 退出对称禁用 — 基准 ink 焦点追踪生命周期
        Assert.Equal("\x1b[?1004l", DecPrivateMode.Disable(DecPrivateModeKind.FocusReporting));
    }
}

/// <summary>
/// OSC（Operating System Command）测试 — 标题/超链接/进度条。
/// </summary>
public sealed class OscTests
{
    [Fact]
    public void SetTitle_EmitsBelTerminated()
    {
        Assert.Equal("\x1b]0;jcctui\x07", Osc.SetTitle("jcctui"));
    }

    [Fact]
    public void Hyperlink_EmitsOsc8WithStTerminator()
    {
        // 基准 HyperlinkPool: OSC 8 ; params ; uri ST
        Assert.Equal("\x1b]8;;https://example.com\x1b\\", Osc.Hyperlink("https://example.com"));
    }

    [Fact]
    public void Hyperlink_Close_EmitsEmptyUri()
    {
        Assert.Equal("\x1b]8;;\x1b\\", Osc.HyperlinkEnd());
    }

    [Fact]
    public void Progress_EmitsNineFour()
    {
        // 基准 isProgressReportingAvailable: OSC 9;4;state;progress（Windows Terminal 排除）
        Assert.Equal("\x1b]9;4;1;50\x07", Osc.Progress(state: 1, progress: 50));
    }

    [Fact]
    public void TabStatus_BusyPreset_EmitsOsc21337ThreeFields()
    {
        // 基准 use-tab-status.ts busy preset: indicator #FF9500 / "Working…" / status-color #FF9500
        // rgb 输出小写十六进制零填充（termio/osc.ts rgb() padStart(2,'0')）
        Assert.Equal(
            "\x1b]21337;indicator=#ff9500;status=Working…;status-color=#ff9500\x07",
            Osc.TabStatus(indicatorRgb: 0xFF9500, status: "Working…", statusRgb: 0xFF9500));
    }

    [Fact]
    public void TabStatus_IdlePreset_EmitsGrayStatusColor()
    {
        // 基准 idle preset: #00D75F / "Idle" / #888888
        Assert.Equal(
            "\x1b]21337;indicator=#00d75f;status=Idle;status-color=#888888\x07",
            Osc.TabStatus(indicatorRgb: 0x00D75F, status: "Idle", statusRgb: 0x888888));
    }

    [Fact]
    public void TabStatus_WaitingPreset_EmitsBlueIndicator()
    {
        // 基准 waiting preset: #5F87FF / "Waiting" / #5F87FF
        Assert.Equal(
            "\x1b]21337;indicator=#5f87ff;status=Waiting;status-color=#5f87ff\x07",
            Osc.TabStatus(indicatorRgb: 0x5F87FF, status: "Waiting", statusRgb: 0x5F87FF));
    }

    [Fact]
    public void TabStatus_OmittedFields_AreSkipped()
    {
        // 基准 tabStatus(): 'indicator' in fields 才输出该字段 — null = 不发送（终端保持原值）
        Assert.Equal(
            "\x1b]21337;status=Idle\x07",
            Osc.TabStatus(status: "Idle"));
    }

    [Fact]
    public void TabStatus_StatusText_EscapesSemicolonThenBackslash()
    {
        // 基准转义序：先 `\`→`\\` 再 `;`→`\;`（osc.ts replaceAll 顺序）— "a;b\c" → "a\;b\\c"
        Assert.Equal(
            "\x1b]21337;status=a\\;b\\\\c\x07",
            Osc.TabStatus(status: "a;b\\c"));
    }

    [Fact]
    public void ClearTabStatus_EmitsEmptyThreeFields()
    {
        // 基准 CLEAR_TAB_STATUS = osc(21337, 'indicator=;status=;status-color=') — 退出清残留点
        Assert.Equal("\x1b]21337;indicator=;status=;status-color=\x07", Osc.ClearTabStatus());
    }
}
