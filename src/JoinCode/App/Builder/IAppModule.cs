namespace JoinCode.App.Builder;

/// <summary>
/// 应用模块接口 — 每个模块负责一组相关的服务注册和启动后初始化
/// </summary>
public interface IAppModule
{
    /// <summary>
    /// 注册服务到 DI 容器 — 在 Host 构建阶段调用
    /// </summary>
    void ConfigureServices(IServiceCollection services, AppModuleContext context);

    /// <summary>
    /// 启动后初始化 — 在 Host 构建完成后调用（如 MCP 初始化、事件桥接）
    /// </summary>
    Task ConfigureAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>
    /// 模块执行优先级 — 数值越小越先执行（ConfigureServices 和 ConfigureAsync 均按此排序）
    /// </summary>
    int Order => 100;
}
