namespace Core.Utils;

/// <summary>
/// 水位线监控 — 防抖触发告警:到达水位触发一次,回落到 Normal 后才允许再次触发,消除告警风暴(P0 修复)。
/// <para>原 ActorBase.CheckInputWatermark 每次调用都触发事件,高水位下每条消息都告警 → 告警风暴。</para>
/// <para>防抖语义:Critical 首次触发后标记 _criticalFired;High 首次触发后标记 _highFired;回落 Normal 重置标记。</para>
/// <para>线程安全:防抖状态为 bool(x64 读写原子),无锁多线程并发访问;竞态后果轻微(偶发多触发一次告警),</para>
/// <para>且无锁保证 Tell 路径零额外开销,不改变 Consumer 调度时序(避免水位堆积测试 flaky)。</para>
/// <para>职责边界:只做水位状态判断与防抖,不持有 Channel,不触发事件(由 ActorBase 用 RaiseEvent 多播隔离触发)。</para>
/// </summary>
internal sealed class WatermarkMonitor {
    private readonly ActorBackpressure _backpressure;
    private bool _highFired;
    private bool _criticalFired;

    /// <summary>初始化水位监控</summary>
    /// <param name="backpressure">背压配置(提供高/危险水位线与容量)</param>
    public WatermarkMonitor(ActorBackpressure backpressure) {
        _backpressure = backpressure;
    }

    /// <summary>
    /// 根据当前队列总数检查水位变化,仅在状态变化时返回事件参数(防抖)。
    /// <para>到达 Critical:首次触发 Critical(同时标记 High 已触发,避免降级重复告警)。</para>
    /// <para>到达 High(未到 Critical):首次触发 High。</para>
    /// <para>回落到 Normal:重置防抖标记,允许下次再次触发。</para>
    /// <para>已触发后维持在高位不重复触发(防抖核心)。</para>
    /// </summary>
    /// <param name="currentCount">当前消息总数(输入通道+重试队列)</param>
    /// <param name="actorTypeName">Actor 类型名(用于事件参数,与原 CheckInputWatermark 行为一致)</param>
    /// <param name="args">触发时的事件参数,null 表示未触发(防抖或正常水位)</param>
    /// <returns>true=触发了水位事件(调用方应 RaiseEvent);false=未触发</returns>
    public bool Check(int currentCount, string actorTypeName, out BackpressureEventArgs? args) {
        args = null;
        var level = currentCount >= _backpressure.EffectiveCriticalWatermark ? WatermarkLevel.Critical
                  : currentCount >= _backpressure.EffectiveHighWatermark ? WatermarkLevel.High
                  : WatermarkLevel.Normal;
        switch (level) {
            case WatermarkLevel.Critical when !_criticalFired:
                _criticalFired = true;
                _highFired = true;
                args = new BackpressureEventArgs(actorTypeName, currentCount, _backpressure.Capacity, level);
                return true;
            case WatermarkLevel.High when !_highFired:
                _highFired = true;
                args = new BackpressureEventArgs(actorTypeName, currentCount, _backpressure.Capacity, level);
                return true;
            case WatermarkLevel.Normal:
                _highFired = false;
                _criticalFired = false;
                return false;
            default:
                return false;
        }
    }

    /// <summary>
    /// 重置防抖状态至初始(测试与恢复场景使用)。
    /// <para>重置后下次 Check 到 High/Critical 将重新触发。</para>
    /// </summary>
    public void Reset() {
        _highFired = false;
        _criticalFired = false;
    }
}
