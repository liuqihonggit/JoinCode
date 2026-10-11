namespace Core.Utils;

/// <summary>
/// 锁诊断配置 — 不可变 record,启动时设置一次,运行期间通过 CAS 整体替换。
/// <para>每个操作开头快照 <c>var cfg = Volatile.Read(ref _config);</c>,整个操作用同一快照,</para>
/// <para>不受其他线程修改影响 — 多线程安全无竞态。</para>
/// <para>配置项:DiagnosticsEnabled(诊断开关)、WaitTimeoutThreshold(等待告警阈值)、</para>
/// <para>HoldTooLongThreshold(持有告警阈值)、DiagnosticSink(输出委托)、ScanInterval(扫描间隔)。</para>
/// </summary>
internal sealed record LockRegistryConfig(
    bool DiagnosticsEnabled,
    TimeSpan WaitTimeoutThreshold,
    TimeSpan HoldTooLongThreshold,
    Action<string>? DiagnosticSink,
    TimeSpan ScanInterval) {
    /// <summary>默认配置 — 诊断关闭,30s 等待告警,5s 持有告警,stderr 输出,5s 扫描间隔</summary>
    public static readonly LockRegistryConfig Default = new(
        DiagnosticsEnabled: false,
        WaitTimeoutThreshold: TimeSpan.FromSeconds(30),
        HoldTooLongThreshold: TimeSpan.FromSeconds(5),
        DiagnosticSink: static msg => AsyncStderrWriter.Enqueue(msg),
        ScanInterval: TimeSpan.FromSeconds(5));
}
