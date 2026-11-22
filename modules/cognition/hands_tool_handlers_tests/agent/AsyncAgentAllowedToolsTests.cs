namespace Tools.Handlers;

public sealed class AsyncAgentAllowedToolsTests {
    [Fact]
    public void AsyncAgentAllowedTools_Contains_FileRead() {
        AsyncAgentAllowedTools.Tools.Should().Contain(FileToolNameEnumConstants.FileRead);
    }

    [Fact]
    public void AsyncAgentAllowedTools_Contains_Bash() {
        AsyncAgentAllowedTools.Tools.Should().Contain(ShellToolNameEnumConstants.Bash);
    }

    [Fact]
    public void AsyncAgentAllowedTools_DoesNotContain_AgentSpawn() {
        AsyncAgentAllowedTools.Tools.Should().NotContain(AgentToolNameEnumConstants.AgentSpawn);
    }

    [Fact]
    public void AsyncAgentAllowedTools_DoesNotContain_AskUserQuestion() {
        AsyncAgentAllowedTools.Tools.Should().NotContain(UserInteractionToolNameEnumConstants.AskUserQuestion);
        AsyncAgentAllowedTools.Tools.Should().NotContain(UserInteractionToolNameEnumConstants.ConfirmAction);
    }

    [Fact]
    public void BackgroundExecutionPolicy_DeniesInteractionTools() {
        BackgroundExecutionPolicy.DeniedTools.Should().Contain(UserInteractionToolNameEnumConstants.AskUserQuestion);
        BackgroundExecutionPolicy.DeniedTools.Should().Contain(UserInteractionToolNameEnumConstants.ConfirmAction);
        BackgroundExecutionPolicy.Mode.Should().Be(PermissionMode.Unattended);
    }

    [Fact]
    public void AsyncAgentAllowedTools_IsNotEmpty() {
        AsyncAgentAllowedTools.Tools.Should().NotBeEmpty();
    }
}