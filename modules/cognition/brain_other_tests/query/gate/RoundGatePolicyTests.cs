namespace Core.Tests.Query.Gate;

/// <summary>
/// TASK102 轮次闸门策略测试 — 选项解析、开关三态、拦截上限与注入提示词内容的唯一数据源。
/// </summary>
public sealed class RoundGatePolicyTests {
    [Theory]
    [InlineData(1, RoundGateChoice.Continue)]
    [InlineData(2, RoundGateChoice.NextTodo)]
    [InlineData(3, RoundGateChoice.Plan)]
    [InlineData(4, RoundGateChoice.Acceptance)]
    [InlineData(5, RoundGateChoice.End)]
    public void TryFromNumber_AcceptsValidChoices(int number, RoundGateChoice expected) {
        RoundGatePolicy.TryFromNumber(number, out var choice).Should().BeTrue();
        choice.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void TryFromNumber_RejectsOutOfRange(int number) {
        RoundGatePolicy.TryFromNumber(number, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("1", RoundGateChoice.Continue)]
    [InlineData("5", RoundGateChoice.End)]
    [InlineData("continue", RoundGateChoice.Continue)]
    [InlineData("next_todo", RoundGateChoice.NextTodo)]
    [InlineData("todo", RoundGateChoice.NextTodo)]
    [InlineData("plan", RoundGateChoice.Plan)]
    [InlineData("acceptance", RoundGateChoice.Acceptance)]
    [InlineData("verify", RoundGateChoice.Acceptance)]
    [InlineData("end", RoundGateChoice.End)]
    [InlineData("finish", RoundGateChoice.End)]
    [InlineData(" END ", RoundGateChoice.End)]
    public void TryParseChoice_AcceptsNumbersCanonicalValuesAndAliases(string raw, RoundGateChoice expected) {
        RoundGatePolicy.TryParseChoice(raw, out var choice).Should().BeTrue();
        choice.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("6")]
    [InlineData("unknown")]
    public void TryParseChoice_RejectsInvalid(string raw) {
        RoundGatePolicy.TryParseChoice(raw, out _).Should().BeFalse();
    }

    [Fact]
    public void TryReadChoice_UsesParsedArgumentsAndIsCaseInsensitiveOnToolName() {
        using var document = JsonDocument.Parse("""{"choice":5}""");
        var arguments = new Dictionary<string, JsonElement> {
            ["choice"] = document.RootElement.GetProperty("choice").Clone()
        };

        RoundGatePolicy.TryReadChoice("ROUND_GATE", arguments, rawArguments: null, out var choice).Should().BeTrue();
        choice.Should().Be(RoundGateChoice.End);
    }

    [Fact]
    public void TryReadChoice_FallsBackToRawArgumentsWhenParsedDictionaryMissing() {
        RoundGatePolicy.TryReadChoice("round_gate", null, """{"choice":"acceptance"}""", out var choice).Should().BeTrue();
        choice.Should().Be(RoundGateChoice.Acceptance);
    }

    [Fact]
    public void TryReadChoice_IgnoresNonGateTools() {
        RoundGatePolicy.TryReadChoice("bash", null, """{"choice":5}""", out _).Should().BeFalse();
        RoundGatePolicy.TryReadChoiceFromJson("bash", """{"choice":5}""", out _).Should().BeFalse();
    }

    [Fact]
    public void TryReadChoiceFromJson_ParsesChatLoopFormat() {
        RoundGatePolicy.TryReadChoiceFromJson("round_gate", """{"choice":3}""", out var choice).Should().BeTrue();
        choice.Should().Be(RoundGateChoice.Plan);

        RoundGatePolicy.TryReadChoiceFromJson("round_gate", "not-json", out _).Should().BeFalse();
        RoundGatePolicy.TryReadChoiceFromJson("round_gate", """{"choice":9}""", out _).Should().BeFalse();
    }

    [Fact]
    public void AcceptancePrompt_EnforcesRealExeAndUserVerificationMarking() {
        var text = RoundGatePrompts.ForChoice(RoundGateChoice.Acceptance);

        text.Should().Contain("exe", "验收必须启动真实 exe");
        text.Should().Contain("ADR 0080");
        text.Should().Contain("禁止用", "不得以单元测试作为验收依据");
        text.Should().Contain("○", "需要用户亲自验收的项必须用圆圈标记");
        text.Should().Contain("待用户验收");
    }

    [Fact]
    public void EndPrompt_RequiresCompletionAndAcceptance() {
        var text = RoundGatePrompts.ForChoice(RoundGateChoice.End);

        text.Should().Contain("任务全部完成");
        text.Should().Contain("○");
    }

    [Fact]
    public void StopBlockedMessage_ExplainsToolNameOptionsAndLimit() {
        var text = RoundGatePrompts.BuildStopBlockedMessage(rejection: 2, maxRejections: 6);

        text.Should().Contain("round_gate");
        text.Should().Contain("第 2 次");
        text.Should().Contain("最多 6 次");
        text.Should().Contain("5 结束");
    }

    [Fact]
    public void StopBlockedMessage_UnlimitedLimitText() {
        var text = RoundGatePrompts.BuildStopBlockedMessage(rejection: 10, maxRejections: 0);

        text.Should().Contain("不限制拦截次数");
    }

    [Theory]
    [InlineData(0, 6, true)]
    [InlineData(5, 6, true)]
    [InlineData(6, 6, false)]
    [InlineData(99, 0, true)]
    public void CanRejectStop_RespectsLimitAndUnlimitedZero(int rejections, int max, bool expected) {
        RoundGatePolicy.CanRejectStop(rejections, max).Should().Be(expected);
    }

    [Fact]
    public void ReadMode_ParsesTriStateAndFallsBackToAuto() {
        var envName = JccEnvVar.RoundGate.ToValue();

        using (EnvVarScope.Set(envName, null)) {
            RoundGatePolicy.ReadMode().Should().Be(RoundGateMode.Auto);
        }

        using (EnvVarScope.Set(envName, "on")) {
            RoundGatePolicy.ReadMode().Should().Be(RoundGateMode.On);
        }

        using (EnvVarScope.Set(envName, "true")) {
            RoundGatePolicy.ReadMode().Should().Be(RoundGateMode.On);
        }

        using (EnvVarScope.Set(envName, "off")) {
            RoundGatePolicy.ReadMode().Should().Be(RoundGateMode.Off);
        }

        using (EnvVarScope.Set(envName, "0")) {
            RoundGatePolicy.ReadMode().Should().Be(RoundGateMode.Off);
        }

        using (EnvVarScope.Set(envName, "garbage")) {
            RoundGatePolicy.ReadMode().Should().Be(RoundGateMode.Auto);
        }
    }

    [Fact]
    public void ReadMaxStopRejections_ParsesAndFallsBackToDefault() {
        var envName = JccEnvVar.RoundGateMaxReject.ToValue();

        using (EnvVarScope.Set(envName, null)) {
            RoundGatePolicy.ReadMaxStopRejections().Should().Be(RoundGatePolicy.DefaultMaxStopRejections);
        }

        using (EnvVarScope.Set(envName, "0")) {
            RoundGatePolicy.ReadMaxStopRejections().Should().Be(0, "0 表示不限制");
        }

        using (EnvVarScope.Set(envName, "3")) {
            RoundGatePolicy.ReadMaxStopRejections().Should().Be(3);
        }

        using (EnvVarScope.Set(envName, "-1")) {
            RoundGatePolicy.ReadMaxStopRejections().Should().Be(RoundGatePolicy.DefaultMaxStopRejections);
        }

        using (EnvVarScope.Set(envName, "abc")) {
            RoundGatePolicy.ReadMaxStopRejections().Should().Be(RoundGatePolicy.DefaultMaxStopRejections);
        }
    }
}
