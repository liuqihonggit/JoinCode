
namespace Core.DependencyInjection;

public static partial class ServiceRegistration
{
    public static void WirePluginSkillBridge(this IServiceProvider serviceProvider)
    {
        Console.Error.WriteLine("[WIRE] resolving IPluginManager...");
        var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();
        Console.Error.WriteLine("[WIRE] IPluginManager OK, resolving IPluginSkillBridge...");

        // 细粒度诊断 — 逐步解析 PluginSkillBridge 的依赖项以定位卡死点
        // 依赖链: IPluginSkillBridge → PluginSkillBridge → (IPluginManager, ISkillService, ILogger)
        //   ISkillService → SkillService → (SkillOptions, IFileOperationService, MiddlewarePipeline<SkillContext>, ...)
        //   MiddlewarePipeline<SkillContext> → IEnumerable<IMiddleware<SkillContext>> → SkillExecutionMiddleware → (IQueryEngine, IToolRegistry)
        //   IQueryEngine → QueryEngine → (IChatClient, IToolRegistry, IOptions<QueryEngineConfig>, IEnumerable<IQueryMiddleware>)
        //   IChatClient → ChatClient → IQueryService → QueryService
        Console.Error.WriteLine("[WIRE] resolving ISkillService (deep dependency chain)...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var skillService = serviceProvider.GetRequiredService<JoinCode.Abstractions.Interfaces.ISkillService>();
            Console.Error.WriteLine($"[WIRE] ISkillService OK ({sw.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WIRE] ISkillService FAILED after {sw.ElapsedMilliseconds}ms: {ex.Message}");
            throw;
        }

        var skillBridge = serviceProvider.GetRequiredService<Core.Skills.Plugin.IPluginSkillBridge>();
        Console.Error.WriteLine("[WIRE] IPluginSkillBridge OK");

        if (pluginManager is Core.Plugins.PluginManager pm)
        {
            pm.PluginLoaded += async (_, pluginName) =>
            {
                try
                {
                    await skillBridge.RegisterPluginSkillsAsync(pluginName).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(L.T(StringKey.RegisterPluginSkillFailedLog, pluginName, ex.Message));
                }
            };

            pm.PluginUnloading += async (_, pluginName) =>
            {
                try
                {
                    await skillBridge.UnregisterPluginSkillsAsync(pluginName).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"[PluginSkillBridge] UnregisterPluginSkills failed for {pluginName}: {ex.Message}");
                }
            };
        }
    }
}
