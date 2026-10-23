namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// ConPTY 子进程启动信息（TASK066 C3）：标准句柄必须显式置为 INVALID_HANDLE_VALUE。
/// 宿主 stdout 被重定向时，Windows 会把宿主的重定向句柄复制给子进程，盖过伪控制台，
/// 表现为提示符写进宿主日志而 xterm 一片空白。
/// </summary>
public sealed class DshConPtyTests {
    /// <summary>启动信息置空三个标准句柄，并保留属性列表与结构尺寸</summary>
    [Fact]
    public void BuildStartupInfo_DetachesStandardHandles() {
        var attributeList = new IntPtr(0x1234);

        var info = DshConPty.BuildStartupInfo(attributeList);

        info.StartupInfo.cb.Should().Be(Marshal.SizeOf<DshConPty.STARTUPINFOEX>());
        info.StartupInfo.dwFlags.Should().Be(DshConPty.StartfUseStdHandles);
        info.StartupInfo.hStdInput.Should().Be(new IntPtr(-1));
        info.StartupInfo.hStdOutput.Should().Be(new IntPtr(-1));
        info.StartupInfo.hStdError.Should().Be(new IntPtr(-1));
        info.lpAttributeList.Should().Be(attributeList);
    }
}
