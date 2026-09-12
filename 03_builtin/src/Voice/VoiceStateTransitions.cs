namespace Services.Voice;

/// <summary>
/// 语音录制状态转换规则 — 集中定义 VoiceRecordingState 所有合法转换
/// <para>原 VoiceService.SetState 无转换校验,现统一提取为转换表</para>
/// <para>Idle→Recording, Recording→Processing/Error/Idle, Processing→Idle/Error, Error→Idle</para>
/// </summary>
public static class VoiceStateTransitions
{
    /// <summary>
    /// 状态转换位掩码表 — 索引为 (int)VoiceRecordingState，值为目标状态位掩码。
    /// 替代 FrozenDictionary&lt;VoiceRecordingState, FrozenSet&lt;VoiceRecordingState&gt;&gt;，O(1) 数组索引 + 位运算无哈希查找。
    /// </summary>
    private static readonly int[] Transitions =
    [
        /* Idle=0 */ BitMask.Of(VoiceRecordingState.Recording),
        /* Recording=1 */ BitMask.Of(VoiceRecordingState.Processing, VoiceRecordingState.Error, VoiceRecordingState.Idle),
        /* Processing=2 */ BitMask.Of(VoiceRecordingState.Idle, VoiceRecordingState.Error),
        /* Error=3 */ BitMask.Of(VoiceRecordingState.Idle)
    ];

    /// <summary>
    /// 是否可从 current 转换到 target — 自环合法
    /// </summary>
    public static bool CanTransitionTo(VoiceRecordingState current, VoiceRecordingState target)
    {
        if (current == target)
        {
            return true;
        }

        return BitMask.Contains(Transitions[(int)current], target);
    }

    /// <summary>
    /// 是否为终态 — Error 为可恢复终态（可转 Idle 重置），无不可恢复终态
    /// </summary>
    public static bool IsTerminal(VoiceRecordingState state) => false;
}
