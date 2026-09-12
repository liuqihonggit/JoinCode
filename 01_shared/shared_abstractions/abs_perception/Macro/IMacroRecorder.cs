namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 宏定义 — 可回放的操作序列（PRD S-02/S-03）
/// </summary>
public sealed record Macro(string Name, IReadOnlyList<DesktopOperation> Operations, DateTimeOffset CreatedAt);

/// <summary>
/// 宏录制器 — 录制/回放/保存/加载操作序列（PRD S-02/S-03）
/// </summary>
public interface IMacroRecorder
{
    /// <summary>是否正在录制</summary>
    bool IsRecording { get; }

    /// <summary>开始录制（清空之前的录制内容）</summary>
    void StartRecording(string macroName);

    /// <summary>停止录制并返回宏</summary>
    Macro StopRecording();

    /// <summary>记录一个操作（仅在录制状态下有效）</summary>
    void RecordOperation(DesktopOperation operation);

    /// <summary>回放宏 — 按顺序执行操作序列</summary>
    /// <param name="macro">要回放的宏</param>
    /// <param name="speedMultiplier">加速倍数（1=原速, 2=2倍速, 0=最大速度）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>回放结果（成功/失败数）</returns>
    Task<MacroPlaybackResult> PlayAsync(Macro macro, int speedMultiplier = 1, CancellationToken cancellationToken = default);

    /// <summary>保存宏到文件（JSON）</summary>
    void SaveMacro(Macro macro, string filePath);

    /// <summary>从文件加载宏</summary>
    Macro LoadMacro(string filePath);
}

/// <summary>
/// 宏回放结果
/// </summary>
public sealed record MacroPlaybackResult(int TotalSteps, int SucceededSteps, int FailedSteps, TimeSpan Elapsed);
