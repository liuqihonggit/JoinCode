namespace Core.Tests.Services.Voice;

/// <summary>
/// VoiceStateTransitions 单元测试 — 验证语音录制状态转换规则正确性
/// </summary>
public sealed class VoiceStateTransitionsTests
{
    [Fact]
    public void CanTransitionTo_ShouldAllowIdleToRecording()
    {
        VoiceStateTransitions.CanTransitionTo(VoiceRecordingState.Idle, VoiceRecordingState.Recording).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowRecordingToProcessingErrorIdle()
    {
        VoiceStateTransitions.CanTransitionTo(VoiceRecordingState.Recording, VoiceRecordingState.Processing).Should().BeTrue();
        VoiceStateTransitions.CanTransitionTo(VoiceRecordingState.Recording, VoiceRecordingState.Error).Should().BeTrue();
        VoiceStateTransitions.CanTransitionTo(VoiceRecordingState.Recording, VoiceRecordingState.Idle).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowProcessingToIdleError()
    {
        VoiceStateTransitions.CanTransitionTo(VoiceRecordingState.Processing, VoiceRecordingState.Idle).Should().BeTrue();
        VoiceStateTransitions.CanTransitionTo(VoiceRecordingState.Processing, VoiceRecordingState.Error).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowErrorToIdle()
    {
        VoiceStateTransitions.CanTransitionTo(VoiceRecordingState.Error, VoiceRecordingState.Idle).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyIdleToProcessingOrError()
    {
        VoiceStateTransitions.CanTransitionTo(VoiceRecordingState.Idle, VoiceRecordingState.Processing).Should().BeFalse();
        VoiceStateTransitions.CanTransitionTo(VoiceRecordingState.Idle, VoiceRecordingState.Error).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyErrorToRecordingOrProcessing()
    {
        VoiceStateTransitions.CanTransitionTo(VoiceRecordingState.Error, VoiceRecordingState.Recording).Should().BeFalse();
        VoiceStateTransitions.CanTransitionTo(VoiceRecordingState.Error, VoiceRecordingState.Processing).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldDenyProcessingToRecording()
    {
        VoiceStateTransitions.CanTransitionTo(VoiceRecordingState.Processing, VoiceRecordingState.Recording).Should().BeFalse();
    }

    [Fact]
    public void CanTransitionTo_ShouldAllowSelfLoop()
    {
        foreach (var state in Enum.GetValues<VoiceRecordingState>())
        {
            VoiceStateTransitions.CanTransitionTo(state, state).Should().BeTrue();
        }
    }
}
