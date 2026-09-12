namespace JoinCode.Vision.DependencyInjection;

/// <summary>
/// Vision 服务注册入口 — 多模态隐喻显露工具 DI 装配
/// </summary>
public static partial class ServiceRegistration
{
    /// <summary>注册 Vision 子系统服务（四叉树标注器/渲染器/测量器等由 [Register] 自动注册）</summary>
    public static IServiceCollection AddVisionServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
