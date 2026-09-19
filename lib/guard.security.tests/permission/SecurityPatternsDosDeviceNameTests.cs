namespace Guard.Security.Tests;

/// <summary>
/// SecurityPatterns.DosDeviceNameRegex 裸设备名匹配测试 — ADR 0012 阶段2
/// 验证裸 NUL/CON/PRN/AUX/COM1-9/LPT1-9 作为完整文件名被检测（不只是扩展名位置）
/// </summary>
public class SecurityPatternsDosDeviceNameTests {
    #region 裸设备名作为完整文件名 — 应检测

    [Theory]
    [InlineData("nul")]
    [InlineData("NUL")]
    [InlineData("Nul")]
    [InlineData("con")]
    [InlineData("CON")]
    [InlineData("prn")]
    [InlineData("PRN")]
    [InlineData("aux")]
    [InlineData("AUX")]
    [InlineData("com1")]
    [InlineData("COM1")]
    [InlineData("lpt1")]
    [InlineData("LPT9")]
    public void Bare_Device_Name_Should_Be_Suspicious(string path) {
        SecurityPatterns.HasSuspiciousWindowsPathPattern(path).Should().BeTrue();
    }

    #endregion

    #region 路径中含设备名组件 — 应检测

    [Theory]
    [InlineData("D:\\project\\nul")]
    [InlineData("D:\\project\\con")]
    [InlineData("/tmp/nul")]
    [InlineData("path/to/prn")]
    [InlineData("D:\\nul.txt")]
    [InlineData("nul.txt")]
    public void Path_With_Device_Name_Component_Should_Be_Suspicious(string path) {
        SecurityPatterns.HasSuspiciousWindowsPathPattern(path).Should().BeTrue();
    }

    #endregion

    #region 扩展名位置设备名 — 现有行为保持

    [Theory]
    [InlineData("foo.NUL")]
    [InlineData("settings.json.CON")]
    [InlineData(".bashrc.AUX")]
    public void Extension_Position_Device_Name_Should_Be_Suspicious(string path) {
        SecurityPatterns.HasSuspiciousWindowsPathPattern(path).Should().BeTrue();
    }

    #endregion

    #region 非设备名 — 不应误报

    [Theory]
    [InlineData("normal.txt")]
    [InlineData("null")]
    [InlineData("console")]
    [InlineData("print")]
    [InlineData("audio")]
    [InlineData("company1")]
    [InlineData("loop1")]
    public void Non_Device_Name_Should_Not_Be_Suspicious(string path) {
        SecurityPatterns.HasSuspiciousWindowsPathPattern(path).Should().BeFalse();
    }

    #endregion
}