
namespace Core.Tests.Hooks.Configuration;

/// <summary>
/// HookConditionEvaluator 测试
/// </summary>
public class HookConditionEvaluatorTests
{
    private readonly HookConditionEvaluator _evaluator = new();

    [Fact]
    public async Task EvaluateAsync_NullCondition_ShouldReturnTrue()
    {
        // Arrange
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameConstants.ShellExecute);

        // Act
        var result = await _evaluator.EvaluateAsync(null, input).ConfigureAwait(true);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_EmptyCondition_ShouldReturnTrue()
    {
        // Arrange
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameConstants.ShellExecute);

        // Act
        var result = await _evaluator.EvaluateAsync("", input).ConfigureAwait(true);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WhitespaceCondition_ShouldReturnTrue()
    {
        // Arrange
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameConstants.ShellExecute);

        // Act
        var result = await _evaluator.EvaluateAsync("   ", input).ConfigureAwait(true);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(ShellToolNameConstants.ShellExecute, ShellToolNameConstants.ShellExecute, true)]
    [InlineData(ShellToolNameConstants.ShellExecute, "Git", false)]
    public async Task EvaluateAsync_ToolNameMatch_ShouldWork(string condition, string toolName, bool expected)
    {
        // Arrange
        var input = CreateHookInput(HookEvent.PreToolUse, toolName);

        // Act
        var result = await _evaluator.EvaluateAsync(condition, input).ConfigureAwait(true);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Bash(git *)", ShellToolNameConstants.ShellExecute, "git status", true)]
    [InlineData("Bash(git *)", ShellToolNameConstants.ShellExecute, "git log", true)]
    [InlineData("Bash(git *)", ShellToolNameConstants.ShellExecute, "ls -la", false)]
    [InlineData("Bash(git *)", "Git", "git status", false)] // 工具名不匹配
    public async Task EvaluateAsync_ToolPatternMatch_ShouldWork(string condition, string toolName, string command, bool expected)
    {
        // Arrange
        var input = CreateHookInput(HookEvent.PreToolUse, toolName, command);

        // Act
        var result = await _evaluator.EvaluateAsync(condition, input).ConfigureAwait(true);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("event:PreToolUse", HookEvent.PreToolUse, true)]
    [InlineData("event:PostToolUse", HookEvent.PreToolUse, false)]
    [InlineData("event:SESSIONSTART", HookEvent.SessionStart, true)] // 大小写不敏感
    public async Task EvaluateAsync_EventMatch_ShouldWork(string condition, HookEvent eventType, bool expected)
    {
        // Arrange
        var input = CreateHookInput(eventType, ShellToolNameConstants.ShellExecute);

        // Act
        var result = await _evaluator.EvaluateAsync(condition, input).ConfigureAwait(true);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("matcher:Bash", ShellToolNameConstants.ShellExecute, true)]
    [InlineData("matcher:Git", ShellToolNameConstants.ShellExecute, false)]
    public async Task EvaluateAsync_MatcherMatch_ShouldWork(string condition, string matcher, bool expected)
    {
        // Arrange
        var input = CreateHookInput(HookEvent.PreToolUse, ShellToolNameConstants.ShellExecute, matcher: matcher);

        // Act
        var result = await _evaluator.EvaluateAsync(condition, input).ConfigureAwait(true);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Bash && Git", ShellToolNameConstants.ShellExecute, false)]
    [InlineData("Bash && Git", "Git", false)]
    public async Task EvaluateAsync_AndOperator_ShouldWork(string condition, string toolName, bool expected)
    {
        // Arrange
        var input = CreateHookInput(HookEvent.PreToolUse, toolName);

        // Act
        var result = await _evaluator.EvaluateAsync(condition, input).ConfigureAwait(true);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Bash || Git", ShellToolNameConstants.ShellExecute, true)]
    [InlineData("Bash || Git", "Git", true)]
    [InlineData("Bash || Git", "Python", false)]
    public async Task EvaluateAsync_OrOperator_ShouldWork(string condition, string toolName, bool expected)
    {
        // Arrange
        var input = CreateHookInput(HookEvent.PreToolUse, toolName);

        // Act
        var result = await _evaluator.EvaluateAsync(condition, input).ConfigureAwait(true);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("!Bash", "Git", true)]
    [InlineData("!Bash", ShellToolNameConstants.ShellExecute, false)]
    public async Task EvaluateAsync_NotOperator_ShouldWork(string condition, string toolName, bool expected)
    {
        // Arrange
        var input = CreateHookInput(HookEvent.PreToolUse, toolName);

        // Act
        var result = await _evaluator.EvaluateAsync(condition, input).ConfigureAwait(true);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("(Bash)", ShellToolNameConstants.ShellExecute, true)]
    [InlineData("(Bash && Git) || Python", "Python", true)]
    public async Task EvaluateAsync_Parentheses_ShouldWork(string condition, string toolName, bool expected)
    {
        // Arrange
        var input = CreateHookInput(HookEvent.PreToolUse, toolName);

        // Act
        var result = await _evaluator.EvaluateAsync(condition, input).ConfigureAwait(true);

        // Assert
        result.Should().Be(expected);
    }

    private static HookInput CreateHookInput(HookEvent hookEvent, string toolName, string? command = null, string? matcher = null)
    {
        var payload = new Dictionary<string, JsonElement>();

        if (command != null)
        {
            payload["input"] = CreateInputObject(command);
        }

        return new HookInput
        {
            Event = hookEvent,
            ToolName = toolName,
            Matcher = matcher ?? toolName,
            Payload = payload
        };
    }

    /// <summary>
    /// 创建 input JSON 对象 {"command": "value"} 的 JsonElement
    /// </summary>
    private static JsonElement CreateInputObject(string command)
    {
        var escaped = JsonEncodedText.Encode(command);
        using var doc = JsonDocument.Parse($"{{\"command\":\"{escaped}\"}}");
        return doc.RootElement.Clone();
    }
}
