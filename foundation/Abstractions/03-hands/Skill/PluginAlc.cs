namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 可收集的插件程序集加载上下文(ADR 0098 维度10)
/// <para>isCollectible=true 允许 Unload 后被 GC 回收</para>
/// <para>NativeAOT 下 AssemblyLoadContext.IsCollectible 可能受限,降级为诊断警告</para>
/// </summary>
public sealed class PluginAlc : AssemblyLoadContext
{
    /// <summary>创建可收集的插件 ALC</summary>
    public PluginAlc(string name) : base(name, isCollectible: true) { }
}

/// <summary>
/// ALC 卸载验证器 — Unload + GC×10 + WeakReference 回收验证(ADR 0098)
/// <para>IsCollectible=false → 返回 AlcNotCollectible 诊断</para>
/// <para>GC 后 alcRef.IsAlive → 返回 AlcLeak 诊断</para>
/// <para>成功回收 → 返回 null</para>
/// <para>注意:调用方应在创建 alcRef 后释放对 ALC 的所有强引用,否则 GC 无法回收</para>
/// </summary>
public static class AlcUnloadVerifier
{
    /// <summary>
    /// 验证 ALC 卸载并回收 — 接收 WeakReference 避免方法内强引用阻止 GC
    /// </summary>
    /// <param name="alcRef">ALC 的弱引用(null 时直接返回 null)</param>
    /// <param name="pluginId">插件标识(用于诊断)</param>
    /// <returns>null=成功,非 null=诊断事件</returns>
    public static PluginDiagnostic? VerifyUnload(WeakReference<AssemblyLoadContext>? alcRef, string pluginId)
    {
        if (alcRef is null) return null;
        if (!alcRef.TryGetTarget(out var alc)) return null;

        if (!alc.IsCollectible)
        {
            return new PluginDiagnostic
            {
                PluginId = pluginId,
                Kind = PluginDiagnosticKind.AlcNotCollectible,
                Message = $"插件 {pluginId} 的 ALC 不可回收，已跳过 Unload。",
                Suggestion = "该 ALC 创建时未开启 isCollectible，属于宿主配置问题，不是插件问题。"
            };
        }

        alc.Unload();
        alc = null!;

        for (int i = 0; i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (alcRef.TryGetTarget(out _))
        {
            return new PluginDiagnostic
            {
                PluginId = pluginId,
                Kind = PluginDiagnosticKind.AlcLeak,
                Message = $"插件 {pluginId} 的 ALC 在 10 次 GC 后仍未被回收。",
                Suggestion =
                    "常见原因与建议：" + Environment.NewLine +
                    "  · 跨插件强引用 → 使用服务代理而不是原始对象" + Environment.NewLine +
                    "  · 静态事件未注销 → 使用 WeakSubscribe 而不是原生 +=" + Environment.NewLine +
                    "  · 裸 Thread/Timer/Task.Run 未退出 → 改用 RunBackgroundTask" + Environment.NewLine +
                    "  · 闭包捕获 ALC 内类型 → 检查后台任务是否长期持有插件类型实例"
            };
        }

        return null;
    }
}
