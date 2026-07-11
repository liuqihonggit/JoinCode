namespace McpToolRegistry;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 远程同步中间件标记接口
/// </summary>
public interface IRemoteSyncMiddleware : IMiddleware<RemoteSyncContext> { }
