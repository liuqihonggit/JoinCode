namespace Core.DependencyInjection;

/// <summary>
/// 共享 HttpClient 实例 — 桥接 [Register] 自动注册与原始实例注册
/// <para>多个服务（RemoteAgentTaskExecutor 等）依赖 HttpClient</para>
/// </summary>
[Register(typeof(System.Net.Http.HttpClient))]
public sealed class SharedHttpClient : System.Net.Http.HttpClient;
