namespace NativeTui.Core.Terminal;

/// <summary>
/// 帧组装器 — 对齐基准 ink 的终端帧输出：CellDiffer 差分内容以 DEC 2026 同步输出原子包裹，
/// 无变化时零输出（空串）。CellDiffer 产出的序列即最终字节流（其注释声明的上层组帧职责在此落地）。
/// 终端不支持 DEC 2026（tmux/未知终端 — 见 <see cref="TerminalCapabilities"/>）时，
/// 构造传 false 跳过 BSU/ESU 包裹直出差分（对齐基准 writeDiffToTerminal 的 skipSyncMarkers 分支：
/// 透传分块已破坏原子性，包裹徒耗 16 字节/帧 + 解析开销）。
/// 接线（未决问题，ReplShell 非本域）：生产构造应传
/// <c>new FrameComposer(TerminalCapabilities.IsSynchronizedOutputSupported())</c>。
/// </summary>
public sealed class FrameComposer
{
    private readonly CellDiffer _differ = new();

    private readonly bool _synchronizedOutputSupported;

    private readonly bool _hyperlinksSupported;

    /// <summary>默认构造 — DEC 2026 包裹启用（视口模式现行为，既有测试与 ReplShell 零破坏）。</summary>
    public FrameComposer() : this(synchronizedOutputSupported: true)
    {
    }

    /// <summary>条件构造 — 不支持 DEC 2026 的终端跳过 BSU/ESU 包裹（超链接零注入 — 既有调用方兼容）。</summary>
    /// <param name="synchronizedOutputSupported">终端能力探测结果（见 <see cref="TerminalCapabilities.IsSynchronizedOutputSupported()"/>）。</param>
    public FrameComposer(bool synchronizedOutputSupported)
        : this(synchronizedOutputSupported, hyperlinksSupported: false)
    {
    }

    /// <summary>全参构造（DSG043 P3）— DEC 2026 包裹 + OSC 8 超链接注入支持
    ///（false = 零注入纯文本降级，HyperlinkEmitter 终端白名单探测结果）。</summary>
    public FrameComposer(bool synchronizedOutputSupported, bool hyperlinksSupported)
    {
        _synchronizedOutputSupported = synchronizedOutputSupported;
        _hyperlinksSupported = hyperlinksSupported;
    }

    /// <summary>组装从 prev 到 next 的帧输出。prev 为 null 表示首帧；无变化返回空串。</summary>
    public string Compose(Screen? prev, Screen next)
    {
        var diff = _differ.RenderFrame(prev, next, _hyperlinksSupported);
        if (diff.Length == 0)
        {
            return string.Empty;
        }

        return _synchronizedOutputSupported
            ? DecPrivateMode.Enable(DecPrivateModeKind.SynchronizedOutput)
                + diff
                + DecPrivateMode.Disable(DecPrivateModeKind.SynchronizedOutput)
            : diff;
    }
}
