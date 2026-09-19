namespace Services.Api.Vcr;

/// <summary>
/// VCR 服务 Actor 命令类型 — 对应 cassette 加载/保存操作，由 VcrActor Consumer 串行处理。
/// <para>TASK001: AsyncLock 迁移到 Actor 邮箱管道，消除显式锁。</para>
/// <para>缓存命中快速路径不经 Actor（ConcurrentDictionary 线程安全），仅未命中时投递命令串行化文件 I/O。</para>
/// </summary>
public abstract record VcrCommand;

/// <summary>加载 cassette — 对应 LoadCassetteAsync 锁内逻辑</summary>
public sealed record LoadCassetteCmd(
    string FilePath,
    string Name,
    TaskCompletionSource<VcrCassette> Reply) : VcrCommand;

/// <summary>保存 cassette — 对应 SaveCassetteAsync 锁内逻辑</summary>
public sealed record SaveCassetteCmd(
    string FilePath,
    VcrCassette Cassette,
    TaskCompletionSource Reply) : VcrCommand;
