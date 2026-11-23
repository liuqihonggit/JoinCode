namespace Core.Context.Tests;

/// <summary>
/// 发送提醒注入器测试 — 用户发送消息时以 &lt;system-reminder&gt; 形态把系统提示词追加到消息末尾，
/// 冷却期内不重复注入（对齐 ClaudeCode system-reminder 注入语义）。
/// 冷却行为用 1ms 短冷却 + Thread.Sleep 验证恢复，避免长等待。
/// </summary>
public class SendReminderInjectorTests {
    /// <summary>独立实例 + 毫秒级冷却 — 测试间零共享状态。</summary>
    private static SendReminderInjector CreateSut(TimeSpan? cooldown = null) =>
        new(cooldown ?? TimeSpan.FromMilliseconds(1));

    [Fact]
    public void Append_FirstSend_AppendsSystemReminderSuffix() {
        var sut = CreateSut(TimeSpan.FromMinutes(10));

        var result = sut.Append("帮我修个bug");

        result.Should().StartWith("帮我修个bug\n\n<system-reminder>");
        result.Should().EndWith("</system-reminder>");
        result.Should().Contain("洞悉用户诉求");
        result.Should().Contain("服务指令背后的诉求");
    }

    [Fact]
    public void Append_SecondSendWithinCooldown_ReturnsInputUnchanged() {
        var sut = CreateSut(TimeSpan.FromMinutes(10));

        sut.Append("第一条消息");
        var result = sut.Append("第二条消息");

        result.Should().Be("第二条消息");
    }

    [Fact]
    public async Task Append_AfterCooldownExpires_InjectsAgain() {
        var sut = CreateSut(TimeSpan.FromMilliseconds(1));

        var first = sut.Append("第一条");
        await Task.Delay(15);
        var second = sut.Append("第二条");

        first.Should().Contain("<system-reminder>");
        second.Should().Contain("<system-reminder>");
    }

    [Theory]
    [InlineData("/model")]
    [InlineData("  /reset  ")]
    [InlineData("")]
    [InlineData("   ")]
    public void Append_SlashCommandOrWhitespace_ReturnsInputUnchanged(string input) {
        var sut = CreateSut(TimeSpan.FromMinutes(10));

        sut.Append("预热：先消耗一次注入额度，验证后续判定不受冷却状态影响");
        var result = sut.Append(input);

        result.Should().Be(input);
    }

    [Fact]
    public void Append_SlashCommand_DoesNotConsumeCooldown() {
        var sut = CreateSut(TimeSpan.FromMinutes(10));

        var slash = sut.Append("/model");
        var normal = sut.Append("正常消息");

        slash.Should().Be("/model");
        normal.Should().Contain("<system-reminder>");
    }

    [Fact]
    public void Instance_SharedAcrossCalls_CooldownAppliesGlobally() {
        SendReminderInjector.ResetForTest();
        try {
            var first = SendReminderInjector.Instance.Append("进程内共享状态第一条");
            var second = SendReminderInjector.Instance.Append("进程内共享状态第二条");

            first.Should().Contain("<system-reminder>");
            second.Should().NotContain("<system-reminder>");
        } finally {
            SendReminderInjector.ResetForTest();
        }
    }
}
