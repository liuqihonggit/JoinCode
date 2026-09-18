
namespace Core.Memdir;

/// <summary>
/// 会话标签服务 Actor 命令类型 — 由 SessionTagActor Consumer 串行处理。
/// <para>AsyncLock+文件 I/O 迁移到 Actor 邮箱管道，消除显式锁。</para>
/// <para>读操作（GetTags/GetAllTags）不经 Actor，直接读 ConcurrentDictionary + 同步 lock（纯内存操作）。</para>
/// <para>写操作（AddTag/RemoveTag）修改内存后通过 TrySend 投递 SaveCmd 触发异步持久化（fire-and-forget）。</para>
/// </summary>
public abstract record SessionTagCommand;

/// <summary>持久化标签到文件 — fire-and-forget 投递，由 Actor Consumer 串行处理</summary>
public sealed record SessionTagSaveCmd : SessionTagCommand;
