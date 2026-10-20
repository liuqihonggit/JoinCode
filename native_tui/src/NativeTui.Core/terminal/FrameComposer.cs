namespace NativeTui.Core.Terminal;

/// <summary>
/// 帧组装器 — 对齐基准 ink 的终端帧输出：CellDiffer 差分内容以 DEC 2026 同步输出原子包裹，
/// 无变化时零输出（空串）。CellDiffer 产出的序列即最终字节流（其注释声明的上层组帧职责在此落地）。
/// </summary>
public sealed class FrameComposer
{
    private readonly CellDiffer _differ = new();

    /// <summary>组装从 prev 到 next 的帧输出。prev 为 null 表示首帧；无变化返回空串。</summary>
    public string Compose(Screen? prev, Screen next)
    {
        var diff = _differ.RenderFrame(prev, next);
        if (diff.Length == 0)
        {
            return string.Empty;
        }

        return DecPrivateMode.Enable(DecPrivateModeKind.SynchronizedOutput)
            + diff
            + DecPrivateMode.Disable(DecPrivateModeKind.SynchronizedOutput);
    }
}
