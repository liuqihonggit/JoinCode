namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// 终端协议 DTO（TASK066 C3）：DSH 前端用 error !== undefined 判断是否渲染错误条，
/// 运行中终端的 error 若序列化为 JSON null，界面会显示“终端错误：null”。
/// </summary>
public sealed class DshTerminalProtocolTests {
    /// <summary>无错误时不输出 error 字段</summary>
    [Fact]
    public void Info_Serialization_OmitsNullError() {
        var info = new DshTerminalInfo { Id = "t1", State = "running" };

        var json = JsonSerializer.Serialize(info, DshJsonContext.Default.DshTerminalInfo);

        json.Should().NotContain("\"error\"");
        json.Should().Contain("\"state\":\"running\"");
    }
}
