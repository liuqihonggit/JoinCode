namespace Core.Tests.Query.Gate;

/// <summary>
/// TASK132 轮次闸门状态机 — 策略单源测试：选项集、类别、出口判定、verdict 解析、调用解析、开关与上限。
/// </summary>
public sealed class RoundGatePolicyTests {
    [Theory]
    [InlineData(0, RoundGateChoice.BlockedReport)]
    [InlineData(1, RoundGateChoice.Continue)]
    [InlineData(2, RoundGateChoice.NextTodo)]
    [InlineData(3, RoundGateChoice.SelfAccept)]
    [InlineData(4, RoundGateChoice.UserAccept)]
    [InlineData(5, RoundGateChoice.Done)]
    [InlineData(6, RoundGateChoice.AwaitBackground)]
    [InlineData(7, RoundGateChoice.Replan)]
    [InlineData(8, RoundGateChoice.Rework)]
    [InlineData(9, RoundGateChoice.Degrade)]
    [InlineData(10, RoundGateChoice.Escalate)]
    [InlineData(11, RoundGateChoice.Abandon)]
    public void TryFromNumber_AcceptsFullOptionSet(int number, RoundGateChoice expected) {
        RoundGatePolicy.TryFromNumber(number, out var choice).Should().BeTrue();
        choice.Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(12)]
    [InlineData(42)]
    public void TryFromNumber_RejectsOutOfRange(int number) {
        RoundGatePolicy.TryFromNumber(number, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("0", RoundGateChoice.BlockedReport)]
    [InlineData("10", RoundGateChoice.Escalate)]
    [InlineData("11", RoundGateChoice.Abandon)]
    [InlineData("1", RoundGateChoice.Continue)]
    [InlineData("5", RoundGateChoice.Done)]
    [InlineData("continue", RoundGateChoice.Continue)]
    [InlineData("next_todo", RoundGateChoice.NextTodo)]
    [InlineData("self_accept", RoundGateChoice.SelfAccept)]
    [InlineData("user_accept", RoundGateChoice.UserAccept)]
    [InlineData("replan", RoundGateChoice.Replan)]
    [InlineData("done", RoundGateChoice.Done)]
    [InlineData(" DONE ", RoundGateChoice.Done)]
    [InlineData("blocked", RoundGateChoice.BlockedReport)]
    [InlineData("escalate", RoundGateChoice.Escalate)]
    [InlineData("abandon", RoundGateChoice.Abandon)]
    [InlineData(" ABANDON ", RoundGateChoice.Abandon)]
    public void TryParseChoice_AcceptsNumbersAndCanonicalEnumValues(string raw, RoundGateChoice expected) {
        RoundGatePolicy.TryParseChoice(raw, out var choice).Should().BeTrue();
        choice.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown")]
    [InlineData("plan")]
    [InlineData("acceptance")]
    [InlineData("verify")]
    public void TryParseChoice_RejectsInvalidAndLegacyAliases(string raw) {
        RoundGatePolicy.TryParseChoice(raw, out _).Should().BeFalse("只认编号与 [EnumValue] 规范值，旧编号语义不做别名");
    }

    [Theory]
    [InlineData(RoundGateChoice.Continue, RoundGateCategory.Execute)]
    [InlineData(RoundGateChoice.NextTodo, RoundGateCategory.Execute)]
    [InlineData(RoundGateChoice.SelfAccept, RoundGateCategory.Verify)]
    [InlineData(RoundGateChoice.UserAccept, RoundGateCategory.Verify)]
    [InlineData(RoundGateChoice.Replan, RoundGateCategory.Meta)]
    [InlineData(RoundGateChoice.Rework, RoundGateCategory.Meta)]
    [InlineData(RoundGateChoice.Degrade, RoundGateCategory.Converge)]
    [InlineData(RoundGateChoice.Escalate, RoundGateCategory.Converge)]
    [InlineData(RoundGateChoice.Abandon, RoundGateCategory.Converge)]
    [InlineData(RoundGateChoice.AwaitBackground, RoundGateCategory.Suspend)]
    [InlineData(RoundGateChoice.Done, RoundGateCategory.Exit)]
    [InlineData(RoundGateChoice.BlockedReport, RoundGateCategory.Exit)]
    public void CategoryOf_MapsFourExitFamilies(RoundGateChoice choice, RoundGateCategory expected) {
        RoundGatePolicy.CategoryOf(choice).Should().Be(expected);
    }

    [Theory]
    [InlineData(RoundGateChoice.Continue, false)]
    [InlineData(RoundGateChoice.NextTodo, false)]
    [InlineData(RoundGateChoice.SelfAccept, false)]
    [InlineData(RoundGateChoice.Rework, false)]
    [InlineData(RoundGateChoice.Replan, false)]
    [InlineData(RoundGateChoice.UserAccept, true)]
    [InlineData(RoundGateChoice.Done, true)]
    [InlineData(RoundGateChoice.AwaitBackground, true)]
    [InlineData(RoundGateChoice.Degrade, true)]
    [InlineData(RoundGateChoice.Escalate, true)]
    [InlineData(RoundGateChoice.Abandon, true)]
    [InlineData(RoundGateChoice.BlockedReport, true)]
    public void IsExitChoice_OnlyExitFamilyCanStop(RoundGateChoice choice, bool expected) {
        RoundGatePolicy.IsExitChoice(choice).Should().Be(expected);
    }

    [Theory]
    [InlineData("pass", RoundGateVerdict.Pass)]
    [InlineData("fail", RoundGateVerdict.Fail)]
    [InlineData("通过", RoundGateVerdict.Pass)]
    [InlineData("不通过", RoundGateVerdict.Fail)]
    [InlineData("", RoundGateVerdict.None)]
    public void TryParseVerdict_ParsesPassFailAndEmpty(string raw, RoundGateVerdict expected) {
        RoundGatePolicy.TryParseVerdict(raw, out var verdict).Should().BeTrue();
        verdict.Should().Be(expected);
    }

    [Fact]
    public void TryParseVerdict_RejectsUnknownText() {
        RoundGatePolicy.TryParseVerdict("maybe", out _).Should().BeFalse();
    }

    [Fact]
    public void TryReadInvocation_ReadsChoiceEvidenceSubjectAndVerdict() {
        using var document = JsonDocument.Parse(
            """{"choice":3,"evidence":"dotnet test 全绿","subject":"RoundGate","verdict":"pass","note":"自验"}""");
        var arguments = new Dictionary<string, JsonElement>();
        foreach (var property in document.RootElement.EnumerateObject())
            arguments[property.Name] = property.Value.Clone();

        RoundGatePolicy.TryReadInvocation("ROUND_GATE", arguments, rawArguments: null, out var invocation).Should().BeTrue();
        invocation.Choice.Should().Be(RoundGateChoice.SelfAccept);
        invocation.Evidence.Should().Be("dotnet test 全绿");
        invocation.Subject.Should().Be("RoundGate");
        invocation.Verdict.Should().Be(RoundGateVerdict.Pass);
    }

    [Fact]
    public void TryReadChoice_FallsBackToRawArgumentsWhenParsedDictionaryMissing() {
        RoundGatePolicy.TryReadChoice("round_gate", null, """{"choice":"user_accept"}""", out var choice).Should().BeTrue();
        choice.Should().Be(RoundGateChoice.UserAccept);
    }

    [Fact]
    public void TryReadChoice_IgnoresNonGateTools() {
        RoundGatePolicy.TryReadChoice("bash", null, """{"choice":5}""", out _).Should().BeFalse();
        RoundGatePolicy.TryReadChoiceFromJson("bash", """{"choice":5}""", out _).Should().BeFalse();
    }

    [Fact]
    public void TryReadChoiceFromJson_ParsesChatLoopFormat() {
        RoundGatePolicy.TryReadChoiceFromJson("round_gate", """{"choice":7}""", out var choice).Should().BeTrue();
        choice.Should().Be(RoundGateChoice.Replan);

        RoundGatePolicy.TryReadChoiceFromJson("round_gate", "not-json", out _).Should().BeFalse();
        RoundGatePolicy.TryReadChoiceFromJson("round_gate", """{"choice":12}""", out _).Should().BeFalse();
        RoundGatePolicy.TryReadChoiceFromJson("round_gate", """{"choice":11}""", out var abandon).Should().BeTrue();
        abandon.Should().Be(RoundGateChoice.Abandon);
    }

    [Fact]
    public void SelfAcceptPrompt_RequiresPerRequirementVerification() {
        var text = RoundGatePrompts.ForChoice(RoundGateChoice.SelfAccept);

        text.Should().Contain("逐条");
        text.Should().Contain("verdict=pass");
        text.Should().Contain("verdict=fail");
        text.Should().Contain("禁止用宽松自验");
    }

    [Fact]
    public void UserAcceptPrompt_RequiresCircleMarkedHandoff() {
        var text = RoundGatePrompts.ForChoice(RoundGateChoice.UserAccept);

        text.Should().Contain("○");
        text.Should().Contain("待用户验收");
    }

    [Fact]
    public void DonePrompt_RequiresThreeLayerTerminalVerification() {
        var text = RoundGatePrompts.ForChoice(RoundGateChoice.Done);

        text.Should().Contain("终验");
        text.Should().Contain("3 自验");
        text.Should().Contain("○");
        text.Should().Contain("完成");
    }

    [Fact]
    public void DonePrompt_RequiresPullRequestAndCredentialFallback() {
        var text = RoundGatePrompts.ForChoice(RoundGateChoice.Done);

        text.Should().Contain("PR", "完成流程必须要求创建 PR");
        text.Should().Contain("auto-merge");
        text.Should().Contain("凭证", "缺少 git 凭证时必须放弃 PR");
        text.Should().Contain("放弃创建 PR");
        text.Should().Contain("未创建 PR", "放弃后必须在总结里报告原因");
    }

    [Fact]
    public void AbandonPrompt_RequiresFailureReportAndRecordedFailure() {
        var text = RoundGatePrompts.ForChoice(RoundGateChoice.Abandon);

        text.Should().Contain("失败报告");
        text.Should().Contain("已尝试");
        text.Should().Contain("残留影响");
        text.Should().Contain("verdict=fail", "放弃只允许在有失败记录后使用");
    }

    [Fact]
    public void AbandonWithoutNote_ErrorExplainsRequiredReport() {
        var text = RoundGatePrompts.BuildAbandonNoteRequiredMessage();

        text.Should().Contain("note");
        text.Should().Contain("失败证据");
        text.Should().Contain("正确写法");
    }

    [Fact]
    public void EscapeHatchPrompts_DeclareTheirGuards() {
        RoundGatePrompts.ForChoice(RoundGateChoice.Replan).Should().Contain("超过 2 次");
        RoundGatePrompts.ForChoice(RoundGateChoice.Rework).Should().Contain("超过 2 次");
        RoundGatePrompts.ForChoice(RoundGateChoice.Degrade).Should().Contain("可用子集");
        RoundGatePrompts.ForChoice(RoundGateChoice.Escalate).Should().Contain("摆烂");
    }

    [Fact]
    public void StopBlockedMessage_ExplainsReasonRecommendationAndLimit() {
        var recommendation = new RoundGateRecommendation {
            Choice = RoundGateChoice.SelfAccept,
            Reason = "本轮已有产出",
            Alternatives = [RoundGateChoice.Continue, RoundGateChoice.Rework],
        };
        var text = RoundGatePrompts.BuildStopBlockedMessage(2, 6, "还没有通过的自验", recommendation);

        text.Should().Contain("第 2 次");
        text.Should().Contain("最多 6 次");
        text.Should().Contain("还没有通过的自验");
        text.Should().Contain("3 自验");
        text.Should().Contain("本轮已有产出");
    }

    [Fact]
    public void StopBlockedMessage_UnlimitedLimitText() {
        var text = RoundGatePrompts.BuildStopBlockedMessage(
            10, 0, "未过闸门", new RoundGateRecommendation { Choice = RoundGateChoice.Continue });

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

    [Fact]
    public void ReadStrictMode_ParsesTruthyValuesOnly() {
        var envName = JccEnvVar.RoundGateStrict.ToValue();

        using (EnvVarScope.Set(envName, null)) {
            RoundGatePolicy.ReadStrictMode().Should().BeFalse();
        }

        using (EnvVarScope.Set(envName, "1")) {
            RoundGatePolicy.ReadStrictMode().Should().BeTrue();
        }

        using (EnvVarScope.Set(envName, "on")) {
            RoundGatePolicy.ReadStrictMode().Should().BeTrue();
        }

        using (EnvVarScope.Set(envName, "0")) {
            RoundGatePolicy.ReadStrictMode().Should().BeFalse();
        }
    }
}
