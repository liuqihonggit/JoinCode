namespace Core.Tests.Hooks.Configuration;

/// <summary>
/// HookConditionEvaluator 内部纯函数确定性测试 — TryParseToolPattern/GetNestedValue/EvaluateInputCondition 等。
/// </summary>
public class HookConditionEvaluatorInternalTests {
    private readonly HookConditionEvaluator _evaluator = new();

    #region TryParseToolPattern

    [Fact]
    public void TryParseToolPattern_SimplePattern_Should_Parse() {
        var success = _evaluator.TryParseToolPattern("Bash(git *)", out var toolName, out var pattern);
        success.Should().BeTrue();
        toolName.Should().Be("Bash");
        pattern.Should().Be("git *");
    }

    [Fact]
    public void TryParseToolPattern_NoPattern_Should_Return_False() {
        var success = _evaluator.TryParseToolPattern("Bash", out var toolName, out var pattern);
        success.Should().BeFalse();
        toolName.Should().Be("Bash");
        pattern.Should().BeNull();
    }

    [Fact]
    public void TryParseToolPattern_EmptyPattern_Should_Parse() {
        var success = _evaluator.TryParseToolPattern("Bash()", out var toolName, out var pattern);
        success.Should().BeTrue();
        toolName.Should().Be("Bash");
        pattern.Should().BeEmpty();
    }

    [Fact]
    public void TryParseToolPattern_WithSpaces_Should_Trim_Pattern() {
        var success = _evaluator.TryParseToolPattern("Bash(  git *  )", out var toolName, out var pattern);
        success.Should().BeTrue();
        toolName.Should().Be("Bash");
        pattern.Should().Be("git *");
    }

    #endregion

    #region GetNestedValue

    [Fact]
    public void GetNestedValue_SimpleString_Should_Return_Value() {
        using var doc = JsonDocument.Parse("{\"name\":\"hello\"}");
        var value = _evaluator.GetNestedValue(doc.RootElement, "name");
        value.Should().Be("hello");
    }

    [Fact]
    public void GetNestedValue_NestedString_Should_Return_Value() {
        using var doc = JsonDocument.Parse("{\"input\":{\"command\":\"git status\"}}");
        var value = _evaluator.GetNestedValue(doc.RootElement, "input.command");
        value.Should().Be("git status");
    }

    [Fact]
    public void GetNestedValue_Number_Should_Return_RawText() {
        using var doc = JsonDocument.Parse("{\"count\":42}");
        var value = _evaluator.GetNestedValue(doc.RootElement, "count");
        value.Should().Be("42");
    }

    [Fact]
    public void GetNestedValue_Boolean_Should_Return_String() {
        using var doc = JsonDocument.Parse("{\"flag\":true}");
        var value = _evaluator.GetNestedValue(doc.RootElement, "flag");
        value.Should().Be("true");
    }

    [Fact]
    public void GetNestedValue_Null_Should_Return_Null() {
        using var doc = JsonDocument.Parse("{\"value\":null}");
        var value = _evaluator.GetNestedValue(doc.RootElement, "value");
        value.Should().BeNull();
    }

    [Fact]
    public void GetNestedValue_MissingKey_Should_Return_Null() {
        using var doc = JsonDocument.Parse("{\"name\":\"hello\"}");
        var value = _evaluator.GetNestedValue(doc.RootElement, "missing");
        value.Should().BeNull();
    }

    [Fact]
    public void GetNestedValue_DeepNested_Missing_Should_Return_Null() {
        using var doc = JsonDocument.Parse("{\"a\":{\"b\":\"c\"}}");
        var value = _evaluator.GetNestedValue(doc.RootElement, "a.x");
        value.Should().BeNull();
    }

    #endregion

    #region MatchesPattern

    [Theory]
    [InlineData("git status", "git status", true)]     // 精确匹配
    [InlineData("git status", "*", true)]               // 通配符
    [InlineData("git status", "git *", true)]           // 前缀通配
    [InlineData("git status", "* status", true)]        // 后缀通配
    [InlineData("git status", "* stat*", true)]         // 子串通配
    [InlineData("git status", "ls *", false)]           // 不匹配
    public void MatchesPattern_VariousInputs(string value, string pattern, bool expected) {
        _evaluator.MatchesPattern(value, pattern).Should().Be(expected);
    }

    [Fact]
    public void MatchesPattern_Regex_Should_Match() {
        _evaluator.MatchesPattern("test123", "^test\\d+$").Should().BeTrue();
        _evaluator.MatchesPattern("test", "^test\\d+$").Should().BeFalse();
    }

    #endregion

    #region EvaluateCondition

    [Fact]
    public void EvaluateCondition_SimpleToolName_Should_Match() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash);
        _evaluator.EvaluateCondition("Bash", input).Should().BeTrue();
        _evaluator.EvaluateCondition("Git", input).Should().BeFalse();
    }

    [Fact]
    public void EvaluateCondition_AndOperator_Should_Work() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash);
        _evaluator.EvaluateCondition("Bash && Bash", input).Should().BeTrue();
        _evaluator.EvaluateCondition("Bash && Git", input).Should().BeFalse();
    }

    [Fact]
    public void EvaluateCondition_OrOperator_Should_Work() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash);
        _evaluator.EvaluateCondition("Git || Bash", input).Should().BeTrue();
        _evaluator.EvaluateCondition("Git || Python", input).Should().BeFalse();
    }

    [Fact]
    public void EvaluateCondition_NotOperator_Should_Work() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash);
        _evaluator.EvaluateCondition("!Git", input).Should().BeTrue();
        _evaluator.EvaluateCondition("!Bash", input).Should().BeFalse();
    }

    [Fact]
    public void EvaluateCondition_Parentheses_Should_Work() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash);
        _evaluator.EvaluateCondition("(Bash)", input).Should().BeTrue();
        _evaluator.EvaluateCondition("(Bash && Git) || Bash", input).Should().BeTrue();
    }

    #endregion

    #region EvaluateSingleCondition

    [Fact]
    public void EvaluateSingleCondition_EventMatch_Should_Work() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash);
        _evaluator.EvaluateSingleCondition("event:PreToolUse", input).Should().BeTrue();
        _evaluator.EvaluateSingleCondition("event:PostToolUse", input).Should().BeFalse();
    }

    [Fact]
    public void EvaluateSingleCondition_MatcherMatch_Should_Work() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash, matcher: "Bash");
        _evaluator.EvaluateSingleCondition("matcher:Bash", input).Should().BeTrue();
        _evaluator.EvaluateSingleCondition("matcher:Git", input).Should().BeFalse();
    }

    [Fact]
    public void EvaluateSingleCondition_ToolName_Should_Work() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash);
        _evaluator.EvaluateSingleCondition("Bash", input).Should().BeTrue();
        _evaluator.EvaluateSingleCondition("Git", input).Should().BeFalse();
    }

    #endregion

    #region EvaluateToolPattern

    [Fact]
    public void EvaluateToolPattern_ExactToolMatch_NoPattern_Should_Be_True() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash);
        _evaluator.EvaluateToolPattern("Bash", null, input).Should().BeTrue();
    }

    [Fact]
    public void EvaluateToolPattern_WildcardPattern_Should_Be_True() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash, "git status");
        _evaluator.EvaluateToolPattern("Bash", "*", input).Should().BeTrue();
    }

    [Fact]
    public void EvaluateToolPattern_CommandMatch_Should_Work() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash, "git status");
        _evaluator.EvaluateToolPattern("Bash", "git *", input).Should().BeTrue();
        _evaluator.EvaluateToolPattern("Bash", "ls *", input).Should().BeFalse();
    }

    [Fact]
    public void EvaluateToolPattern_ToolMismatch_Should_Be_False() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash);
        _evaluator.EvaluateToolPattern("Git", "*", input).Should().BeFalse();
    }

    #endregion

    #region EvaluateInputCondition

    [Fact]
    public void EvaluateInputCondition_CommandMatch_Should_Work() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash, "git status");
        _evaluator.EvaluateInputCondition("command:git *", input).Should().BeTrue();
        _evaluator.EvaluateInputCondition("command:ls *", input).Should().BeFalse();
    }

    [Fact]
    public void EvaluateInputCondition_NoColon_Should_Return_False() {
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameEnumConstants.Bash);
        _evaluator.EvaluateInputCondition("noformat", input).Should().BeFalse();
    }

    [Fact]
    public void EvaluateInputCondition_NoInputPayload_Should_Return_False() {
        var input = new HookInput {
            Event = HookEvent.PreToolUse,
            ToolName = ShellToolNameEnumConstants.Bash,
            Matcher = ShellToolNameEnumConstants.Bash,
            Payload = new Dictionary<string, JsonElement>()
        };
        _evaluator.EvaluateInputCondition("command:git *", input).Should().BeFalse();
    }

    #endregion

    private static HookInput CreateHookInput(HookEvent hookEvent, string toolName, string? command = null, string? matcher = null) {
        var payload = new Dictionary<string, JsonElement>();
        if (command != null) {
            payload["input"] = CreateInputObject(command);
        }
        return new HookInput {
            Event = hookEvent,
            ToolName = toolName,
            Matcher = matcher ?? toolName,
            Payload = payload
        };
    }

    private static JsonElement CreateInputObject(string command) {
        using var doc = JsonDocument.Parse($"{{\"command\":\"{JsonEncodedText.Encode(command)}\"}}");
        return doc.RootElement.Clone();
    }
}
