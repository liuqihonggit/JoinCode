namespace Services.Voice;

/// <summary>
/// 语音录制状态转换规则 — 集中定义 VoiceRecordingState 所有合法转换
/// <para>原 VoiceService.SetState 无转换校验,现统一提取为转换表</para>
/// <para>Idle→Recording, Recording→Processing/Error/Idle, Processing→Idle/Error, Error→Idle</para>
/// </summary>
public static class VoiceStateTransitions
{
    private static readonly FrozenDictionary<VoiceRecordingState, FrozenSet<VoiceRecordingState>> Transitions =
        new Dictionary<VoiceRecordingState, FrozenSet<VoiceRecordingState>>
        {
            [VoiceRecordingState.Idle] = new HashSet<VoiceRecordingState>
            {
                VoiceRecordingState.Recording
            }.ToFrozenSet(),

            [VoiceRecordingState.Recording] = new HashSet<VoiceRecordingState>
            {
                VoiceRecordingState.Processing,
                VoiceRecordingState.Error,
                VoiceRecordingState.Idle
            }.ToFrozenSet(),

            [VoiceRecordingState.Processing] = new HashSet<VoiceRecordingState>
            {
                VoiceRecordingState.Idle,
                VoiceRecordingState.Error
            }.ToFrozenSet(),

            [VoiceRecordingState.Error] = new HashSet<VoiceRecordingState>
            {
                VoiceRecordingState.Idle
            }.ToFrozenSet()
        }.ToFrozenDictionary();

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法
    /// </summary>
    public static bool CanTransitionTo(VoiceRecordingState current, VoiceRecordingState target)
    {
        if (current == target)
        {
            return true;
        }

        return Transitions.TryGetValue(current, out var targets) && targets.Contains(target);
    }

    /// <summary>
    /// 是否为终态 — Error 为可恢复终态（可转 Idle 重置），无不可恢复终态
    /// </summary>
    public static bool IsTerminal(VoiceRecordingState state) => false;
}
