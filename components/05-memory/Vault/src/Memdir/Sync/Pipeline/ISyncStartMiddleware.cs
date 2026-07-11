namespace Memdir.Sync;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 同步启动中间件标记接口
/// </summary>
public interface ISyncStartMiddleware : IMiddleware<SyncStartContext> { }
