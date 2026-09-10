namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 插件错误消息辅助 — 人性化报错+排错引导(ADR 0098)
/// <para>用户可能是人也可能是AI,报错需引导而非误导</para>
/// <para>格式: [错误码] 消息 + 排错建议</para>
/// </summary>
public static class PluginErrors
{
    /// <summary>插件已加载 — 引导先卸载再加载</summary>
    public static string AlreadyLoaded(string pluginName) =>
        $"[INF031] 插件 '{pluginName}' 已经加载。" + Environment.NewLine +
        $"  排错: 先调用 UnloadPluginAsync(\"{pluginName}\") 卸载,再重新加载。" + Environment.NewLine +
        $"  检查: IsPluginLoaded(\"{pluginName}\") 确认当前状态。";

    /// <summary>插件在黑名单中 — 引导检查资源泄漏</summary>
    public static string Blacklisted(string pluginName) =>
        $"[INF-PLUGIN-BL] 插件 '{pluginName}' 已被加入黑名单(此前卸载泄漏),拒绝加载。" + Environment.NewLine +
        $"  排错: 检查上次卸载的诊断记录 GetDiagnostics(),找 AlcLeak/RevertFailed 事件。" + Environment.NewLine +
        $"  常见原因:" + Environment.NewLine +
        $"    · 资源未释放 → 确保所有 ObjectId 资源在 OnUnload 中释放" + Environment.NewLine +
        $"    · 后台任务未退出 → 用 ctx.RunBackgroundTask 而非裸 Task.Run" + Environment.NewLine +
        $"    · 事件未注销 → 用 ctx.WeakSubscribe 而非原生 +=";

    /// <summary>LoadAsync 失败 — 引导检查返回值</summary>
    public static string LoadFailed(string pluginName, string error) =>
        $"[INF032] 插件 '{pluginName}' LoadAsync 返回失败: {error}" + Environment.NewLine +
        $"  排错: 检查 LoadAsync(PluginContext ctx) 的返回值,确保返回 OperationResult.Ok()。" + Environment.NewLine +
        $"  常见原因:" + Environment.NewLine +
        $"    · 服务注册失败 → 检查 ctx.RegisterService 的泛型参数" + Environment.NewLine +
        $"    · 配置缺失 → 检查插件依赖的配置文件是否存在";

    /// <summary>InitializeAsync 失败 — 引导检查服务依赖</summary>
    public static string InitializeFailed(string pluginName, string error) =>
        $"[INF033] 插件 '{pluginName}' InitializeAsync 返回失败: {error}" + Environment.NewLine +
        $"  排错: 检查 InitializeAsync(IServiceProvider sp) 中 GetService<T>() 是否返回 null。" + Environment.NewLine +
        $"  常见原因:" + Environment.NewLine +
        $"    · 依赖服务未注册 → 检查 LoadAsync 中 ctx.RegisterService 是否注册了所需服务" + Environment.NewLine +
        $"    · 服务初始化顺序 → 声明 IPluginDependencies 确保依赖插件先加载";

    /// <summary>卸载契约失败 — 引导检查契约</summary>
    public static string ContractViolation(string pluginName, string reason) =>
        $"[INF-PLUGIN-CONTRACT] 插件 '{pluginName}' 卸载契约校验失败: {reason}" + Environment.NewLine +
        $"  排错: 检查 ValidateUnloadContract() 返回值,确保所有副作用都有对应撤销。" + Environment.NewLine +
        $"  原则: 有注册必有撤销 — ctx.RegisterService → ServiceProvider.Dispose, ctx.Effect → IDisposable.Dispose";

    /// <summary>后台任务超时 — 引导检查 CancellationToken 响应</summary>
    public static string BackgroundTaskTimeout(string pluginName, double seconds) =>
        $"[INF-PLUGIN-BG-TIMEOUT] 插件 '{pluginName}' 后台任务在 {seconds:0.##}s 内未退出。" + Environment.NewLine +
        $"  排错: 确保后台任务正确响应 CancellationToken。" + Environment.NewLine +
        $"  示例: ctx.RunBackgroundTask(async ct => {{ await someWork(ct); }}, waitOnUnload: TimeSpan.FromSeconds(5));" + Environment.NewLine +
        $"  检查: 任务中是否有 await Task.Delay(...) 未传 ct, 或 while 循环未检查 ct.IsCancellationRequested";

    /// <summary>ALC 未回收 — 引导检查强引用</summary>
    public static string AlcLeak(string pluginName) =>
        $"[INF-PLUGIN-ALC-LEAK] 插件 '{pluginName}' 的 ALC 在 10 次 GC 后仍未被回收。" + Environment.NewLine +
        $"  排错: 检查是否有强引用阻止 ALC 回收:" + Environment.NewLine +
        $"    · 跨插件强引用 → 使用服务代理(ctx.RegisterService)而非原始对象" + Environment.NewLine +
        $"    · 静态事件未注销 → 使用 ctx.WeakSubscribe 而非原生 +=" + Environment.NewLine +
        $"    · 裸 Thread/Timer/Task.Run → 改用 ctx.RunBackgroundTask" + Environment.NewLine +
        $"    · 闭包捕获 ALC 内类型 → 检查后台任务是否长期持有插件类型实例";

    /// <summary>ALC 不可回收 — 引导检查 isCollectible</summary>
    public static string AlcNotCollectible(string pluginName) =>
        $"[INF-PLUGIN-ALC-NOT-COLLECTIBLE] 插件 '{pluginName}' 的 ALC 不可回收,已跳过 Unload。" + Environment.NewLine +
        $"  排错: 该 ALC 创建时未开启 isCollectible,属于宿主配置问题。" + Environment.NewLine +
        $"  修复: 用 PluginAlc(name) 创建可收集 ALC(内部 isCollectible: true)。";

    /// <summary>外部插件可执行文件不存在 — 引导检查路径</summary>
    public static string ExternalExeNotFound(string exePath) =>
        $"[INF036] 外部插件可执行文件不存在: {exePath}" + Environment.NewLine +
        $"  排错: 检查路径是否正确,文件是否存在且有执行权限。" + Environment.NewLine +
        $"  检查: 用 Path.GetFullPath 解析相对路径,确认工作目录。";

    /// <summary>外部插件进程启动失败 — 引导检查权限/依赖</summary>
    public static string ExternalProcessStartFailed(string exePath) =>
        $"[INF037] 无法启动外部插件进程: {exePath}" + Environment.NewLine +
        $"  排错:" + Environment.NewLine +
        $"    · 权限不足 → 检查文件是否有执行权限(chmod +x)" + Environment.NewLine +
        $"    · 依赖缺失 → 检查运行时依赖是否安装(如 .NET runtime)" + Environment.NewLine +
        $"    · 路径错误 → 确认 exePath 是绝对路径且指向可执行文件";
}
