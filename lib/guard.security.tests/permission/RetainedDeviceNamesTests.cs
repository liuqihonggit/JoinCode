namespace Guard.Security.Tests;

/// <summary>
/// RetainedDeviceNames 唯一数据源测试 — 验证全部22个设备名检测
/// </summary>
public class RetainedDeviceNamesTests
{
    #region IsMatch — 全部22个设备名

    [Theory]
    [InlineData("nul")]
    [InlineData("con")]
    [InlineData("prn")]
    [InlineData("aux")]
    [InlineData("com1")]
    [InlineData("com2")]
    [InlineData("com3")]
    [InlineData("com4")]
    [InlineData("com5")]
    [InlineData("com6")]
    [InlineData("com7")]
    [InlineData("com8")]
    [InlineData("com9")]
    [InlineData("lpt1")]
    [InlineData("lpt2")]
    [InlineData("lpt3")]
    [InlineData("lpt4")]
    [InlineData("lpt5")]
    [InlineData("lpt6")]
    [InlineData("lpt7")]
    [InlineData("lpt8")]
    [InlineData("lpt9")]
    public void IsMatch_All_Device_Names(string name)
    {
        RetainedDeviceNames.IsMatch(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("NUL")]
    [InlineData("Con")]
    [InlineData("PRN")]
    [InlineData("Aux")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void IsMatch_Case_Insensitive(string name)
    {
        RetainedDeviceNames.IsMatch(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("console")]
    [InlineData("print")]
    [InlineData("audio")]
    [InlineData("company1")]
    [InlineData("loop1")]
    [InlineData("com0")]
    [InlineData("lpt0")]
    [InlineData("com10")]
    [InlineData("lpt10")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsMatch_Non_Device_Names(string name)
    {
        RetainedDeviceNames.IsMatch(name).Should().BeFalse();
    }

    #endregion

    #region FindInPath — 路径组件检查

    [Theory]
    [InlineData("path/nul")]
    [InlineData("path\\nul")]
    [InlineData("nul")]
    [InlineData("dir/con/file.txt")]
    [InlineData("prn.txt")]
    [InlineData("path/aux.txt")]
    public void FindInPath_With_Device_Name(string path)
    {
        RetainedDeviceNames.FindInPath(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("path/normal.txt")]
    [InlineData("normal.txt")]
    [InlineData("path/to/file.cs")]
    [InlineData("foo.CON.bar")]
    public void FindInPath_Without_Device_Name(string path)
    {
        RetainedDeviceNames.FindInPath(path).Should().BeFalse();
    }

    #endregion

    #region FindInPathOrExtension — 路径+扩展名检查

    [Theory]
    [InlineData("path/nul")]
    [InlineData("foo.CON")]
    [InlineData("nul.txt")]
    [InlineData("settings.json.PRN")]
    [InlineData(".bashrc.AUX")]
    [InlineData("path/com1.txt")]
    public void FindInPathOrExtension_With_Device_Name(string path)
    {
        RetainedDeviceNames.FindInPathOrExtension(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("normal.txt")]
    [InlineData("path/to/file.cs")]
    [InlineData("foo.bar.baz")]
    public void FindInPathOrExtension_Without_Device_Name(string path)
    {
        RetainedDeviceNames.FindInPathOrExtension(path).Should().BeFalse();
    }

    #endregion

    #region FindAfterRedirect — 重定向后设备名提取

    [Theory]
    [InlineData("echo test >nul", "nul")]
    [InlineData("echo test 2>nul", "nul")]
    [InlineData("echo test >>nul", "nul")]
    [InlineData("sort <nul", "nul")]
    [InlineData("echo test >con", "con")]
    [InlineData("echo test >prn", "prn")]
    [InlineData("echo test >aux", "aux")]
    [InlineData("echo test >com1", "com1")]
    [InlineData("echo test >lpt1", "lpt1")]
    [InlineData("echo test 2>>con", "con")]
    [InlineData("echo test &>nul", "nul")]
    public void FindAfterRedirect_Extracts_Device_Name(string command, string expected)
    {
        RetainedDeviceNames.FindAfterRedirect(command).Should().Be(expected);
    }

    [Theory]
    [InlineData("echo hello")]
    [InlineData("git status")]
    [InlineData("echo test > file.txt")]
    [InlineData("echo test 2> error.log")]
    [InlineData("")]
    public void FindAfterRedirect_No_Match(string command)
    {
        RetainedDeviceNames.FindAfterRedirect(command).Should().BeNull();
    }

    #endregion

    #region Names 集合 — 唯一数据源验证

    [Fact]
    public void Names_Contains_Exactly_22_Device_Names()
    {
        RetainedDeviceNames.Names.Should().HaveCount(22);
    }

    [Fact]
    public void Pattern_Matches_Documentation()
    {
        RetainedDeviceNames.Pattern.Should().Be("nul|con|prn|aux|com[1-9]|lpt[1-9]");
    }

    #endregion
}
