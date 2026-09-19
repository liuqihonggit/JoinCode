
namespace Core.DependencyInjection;

public static partial class ServiceRegistration {
    /// <summary>
    /// 连接插件-技能桥：订阅 <see cref="PluginManager"/> 的 PluginLoaded/PluginUnloading 事件，
    /// 在插件加载时注册其技能、卸载时取消注册。
    /// <para>含细粒度诊断：逐步解析依赖链以定位卡死点。</para>
    /// </summary>
    /// <param name="serviceProvider">已构建的 DI 服务提供者。</param>
    public static void WirePluginSkillBridge(this IServiceProvider serviceProvider) {
        var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("ServiceRegistration.Skills");
        Diag.WriteLine("[WIRE] resolving IPluginManager...");
        var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();
        Diag.WriteLine("[WIRE] IPluginManager OK, resolving IPluginSkillBridge...");

        // 细粒度诊断 — 逐步解析 PluginSkillBridge 的依赖项以定位卡死点
        // 依赖链: IPluginSkillBridge → PluginSkillBridge → (IPluginManager, ISkillService, ILogger)
        //   ISkillService → SkillService → (SkillOptions, IFileOperationService, MiddlewarePipeline<SkillContext>, ...)
        //   MiddlewarePipeline<SkillContext> → IEnumerable<IMiddleware<SkillContext>> → SkillExecutionMiddleware → (IQueryEngine, IToolRegistry)
        //   IQueryEngine → QueryEngine → (IChatClient, IToolRegistry, IOptions<QueryEngineConfig>, IEnumerable<IQueryMiddleware>)
        //   IChatClient → ChatClient → IQueryService → QueryService
        Diag.WriteLine("[WIRE] resolving ISkillService (deep dependency chain)...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try {
            var skillService = serviceProvider.GetRequiredService<JoinCode.Abstractions.Interfaces.ISkillService>();
            Diag.WriteLine($"[WIRE] ISkillService OK ({sw.ElapsedMilliseconds}ms)");
        } catch (Exception ex) {
            Console.Error.WriteLine($"[WIRE] ISkillService FAILED after {sw.ElapsedMilliseconds}ms: {ex.Message}");
            throw;
        }

        var skillBridge = serviceProvider.GetRequiredService<Core.Skills.Plugin.IPluginSkillBridge>();
        Diag.WriteLine("[WIRE] IPluginSkillBridge OK");

        if (pluginManager is Core.Plugins.PluginManager pm) {
            pm.PluginLoaded += async (_, pluginName) => {
                try {
                    await skillBridge.RegisterPluginSkillsAsync(pluginName).ConfigureAwait(false);
                } catch (Exception ex) {
                    logger?.LogWarning(ex, L.T(StringKey.RegisterPluginSkillFailedLog, pluginName, ex.Message));
                }
            };

            pm.PluginUnloading += async (_, pluginName) => {
                try {
                    await skillBridge.UnregisterPluginSkillsAsync(pluginName).ConfigureAwait(false);
                } catch (Exception ex) {
                    logger?.LogWarning(ex, "[PluginSkillBridge] UnregisterPluginSkills failed for {PluginName}: {Error}", pluginName, ex.Message);
                }
            };
        }
    }
}