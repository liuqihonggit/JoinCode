namespace JoinCode.Eyes.DependencyInjection;

/// <summary>
/// 服务注册静态类 — 提供 Eyes 模块的依赖注入扩展方法
/// </summary>
public static partial class ServiceRegistration {
    /// <summary>
    /// 注册代码索引相关服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="workspaceRoot">工作区根路径</param>
    /// <returns>服务集合（支持链式调用）</returns>
    public static IServiceCollection AddCodeIndexServices(
        this IServiceCollection services,
        string workspaceRoot) {
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        return services;
    }
}