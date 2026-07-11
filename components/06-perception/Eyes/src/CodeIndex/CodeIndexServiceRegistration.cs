namespace Services.CodeIndex;

public static class CodeIndexServiceRegistration
{
    public static IServiceCollection AddCodeIndexServices(
        this IServiceCollection services,
        string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        // CodeIndexOptions — [Register] 自动注册（默认 WorkspaceRoot = Environment.CurrentDirectory）
        // InMemoryIndexStore — [Register] 自动注册（内存索引存储，无持久化）
        // FileWatcherIntegration — [Register] 自动注册（DI 构造函数从 CodeIndexOptions 获取 workspaceRoot）
        // CodeIndexService 的 IHostedService 注册由 [Register(typeof(IHostedService))] + 生成器自动处理

        return services;
    }
}
