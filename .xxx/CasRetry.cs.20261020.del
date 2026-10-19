namespace JoinCode.CodeIndex.Threading;

/// <summary>
/// CAS 重试循环工具 — 单点实现乐观并发写入,内置活锁防御。
/// </summary>
/// <remarks>
/// <para>
/// 防御原理: 用 <see cref="SpinWait.Count"/> 做重试上限(框架内部计数,不依赖用户自维护计数器,
/// 自增变异无法破坏),超过 <see cref="MaxSpinCount"/> 判定活锁并抛异常快速失败。
/// </para>
/// <para>
/// 变异测试防御: 任何使循环退出条件失效的变异(如 CAS 比较反转、break 移除)都会导致
/// 无限自旋 → spin.Count 持续增长 → 撞上限抛 <see cref="InvalidOperationException"/>,
/// 将"testhost 无限挂死"转换为"测试快速失败",变异体被 Killed 而非拖垮整个测试会话。
/// </para>
/// <para>
/// 上限值依据: SpinOnce 在 Count 达到约 10 次后进入让步阶段(Thread.Yield/Sleep),
/// 真实并发场景下连续 100 次让步后仍 CAS 失败的概率趋近于零(意味着持续有人成功推进)。
/// </para>
/// </remarks>
internal static class CasRetry {
    /// <summary>最大自旋次数 — 超过判定为活锁,抛异常快速失败</summary>
    internal const int MaxSpinCount = 100;

    /// <summary>
    /// 对 <paramref name="location"/> 执行乐观并发更新:
    /// 读快照 → <paramref name="update"/> 构造新值 → CAS 提交,失败自旋重试。
    /// updater 返回原引用时视为无变化,直接短路返回(不产生 CAS 写)。
    /// </summary>
    /// <typeparam name="T">引用类型(不可变数据结构快照)</typeparam>
    /// <param name="location">CAS 目标字段(必须是 readonly 字段之外的普通字段)</param>
    /// <param name="update">纯函数: 基于当前快照构造新快照,须无副作用(失败重试会再次调用)</param>
    /// <exception cref="InvalidOperationException">自旋超过 <see cref="MaxSpinCount"/> 次,判定为活锁</exception>
    internal static void Spin<T>(ref T location, Func<T, T> update) where T : class {
        var spin = new SpinWait();
        while (true) {
            var current = Volatile.Read(ref location);
            var updated = update(current);
            if (ReferenceEquals(updated, current)) return;
            if (Interlocked.CompareExchange(ref location, updated, current) == current) return;
            if (spin.Count >= MaxSpinCount) ThrowCasLivelock(spin.Count);
            spin.SpinOnce();
        }
    }

    [DoesNotReturn]
    private static void ThrowCasLivelock(int spinCount) =>
        throw new InvalidOperationException(
            "CAS 重试自旋 " + spinCount + " 次仍未成功,判定为活锁。请检查更新逻辑是否与并发写入方存在互斥冲突。");
}
