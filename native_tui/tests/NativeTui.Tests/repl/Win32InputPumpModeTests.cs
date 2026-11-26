namespace NativeTui.Tests.Repl;

/// <summary>
/// Win32InputPump 控制台模式计算（TASK106 真机缺陷回归）：
/// 鼠标追踪开启时必须清 ENABLE_QUICK_EDIT_MODE — QuickEdit 开着时客户区内一次拖拽会被 conhost
/// 当作选区：应用收不到 MOUSE_EVENT，选区期间输出被阻塞（真机表现为 resize 拖拽后整屏黑、按键才恢复）。
/// 关闭鼠标追踪时（JCC_NO_MOUSE_TRACKING）则保留 QuickEdit 且不接管鼠标，交还终端原生选区。
/// </summary>
public sealed class Win32InputPumpModeTests
{
    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableLineInput = 0x0002;
    private const uint EnableEchoInput = 0x0004;
    private const uint EnableWindowInput = 0x0008;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableQuickEditMode = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;

    /// <summary>鼠标追踪开启：清 QuickEdit、置 Extended（否则清位不生效）与鼠标输入位。</summary>
    [Fact]
    public void MouseTrackingEnabled_ClearsQuickEdit_EnablesExtendedAndMouse()
    {
        var mode = Win32InputPump.ComputeInputMode(
            EnableProcessedInput | EnableLineInput | EnableEchoInput | EnableQuickEditMode,
            mouseTracking: true);

        Assert.Equal(0u, mode & EnableQuickEditMode);
        Assert.NotEqual(0u, mode & EnableExtendedFlags);
        Assert.NotEqual(0u, mode & EnableMouseInput);
        Assert.NotEqual(0u, mode & EnableWindowInput);
    }

    /// <summary>鼠标追踪关闭：保留 QuickEdit、不接管鼠标输入（终端原生选区可继续用）。</summary>
    [Fact]
    public void MouseTrackingDisabled_PreservesQuickEdit_NoMouseInput()
    {
        var mode = Win32InputPump.ComputeInputMode(EnableQuickEditMode, mouseTracking: false);

        Assert.NotEqual(0u, mode & EnableQuickEditMode);
        Assert.Equal(0u, mode & EnableMouseInput);
    }

    /// <summary>原有输入模式位不得被覆盖（processed/line/echo 等保持原值）。</summary>
    [Fact]
    public void OriginalModeBits_ArePreserved()
    {
        var original = EnableProcessedInput | EnableLineInput | EnableEchoInput | EnableQuickEditMode;
        const uint addedByPump = EnableMouseInput | EnableWindowInput | EnableExtendedFlags;

        var mode = Win32InputPump.ComputeInputMode(original, mouseTracking: true);

        Assert.Equal(original & ~EnableQuickEditMode, mode & ~addedByPump);
    }
}
