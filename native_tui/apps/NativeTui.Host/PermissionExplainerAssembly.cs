namespace NativeTui.Host;

/// <summary>
/// 权限解释装配（TASK069-P4）— 把引擎侧的 <see cref="IPermissionExplainer"/> 适配成 Core 的委托：
/// 契约在 <c>Abstractions.Security.Permission</c>，视图在 <c>NativeTui.Core.Repl</c>，
/// 两者不能互相引用（Core 零依赖），所以映射必须住在宿主层 —— 与
/// <see cref="ModelPickerAssembly"/> / <see cref="VendorPickerAssembly"/> 同一形态。
/// <para>解释器解析不到（宿主无 LLM / 被 profile 裁掉）时 <c>enabled=false</c>：ctrl+e 整体 no-op，
/// 提示行也不提它，避免出现「按了没反应」的假入口。</para>
/// </summary>
internal static class PermissionExplainerAssembly {
    /// <summary>装配到 shell（程序组装期调用一次）。</summary>
    /// <param name="shell">REPL 外壳。</param>
    /// <param name="services">引擎服务容器。</param>
    internal static void Attach(ReplShell shell, IServiceProvider services) {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(services);

        var explainer = services.GetService<IPermissionExplainer>();
        shell.BindPermissionExplainer(
            enabled: explainer?.IsEnabled ?? false,
            loader: explainer is null ? null : (toolName, description, ct) => ExplainAsync(explainer, toolName, description, ct));
    }

    /// <summary>调用引擎解释器并映射为 Core 视图；异常一律吞成 null（解释失败不得影响权限确认）。</summary>
    private static async Task<PermissionExplanationView?> ExplainAsync(
        IPermissionExplainer explainer, string toolName, string description, CancellationToken cancellationToken) {
        try {
            var explanation = await explainer
                .ExplainAsync(new PermissionExplainRequest(toolName, description), cancellationToken)
                .ConfigureAwait(false);
            return explanation is null
                ? null
                : new PermissionExplanationView(
                    explanation.Explanation,
                    explanation.Reasoning,
                    explanation.Risk,
                    MapRiskBand(explanation.RiskLevel));
        } catch (OperationCanceledException) {
            return null;
        } catch (Exception) {
            return null;
        }
    }

    /// <summary>契约风险等级 → Core 配色档位（三档一一对应，不做归并 —— 四级以上会与本地危险灯打架）。</summary>
    internal static PermissionRiskBand MapRiskBand(PermissionRiskLevel level) => level switch {
        PermissionRiskLevel.Low => PermissionRiskBand.Low,
        PermissionRiskLevel.Medium => PermissionRiskBand.Medium,
        _ => PermissionRiskBand.High,
    };
}
