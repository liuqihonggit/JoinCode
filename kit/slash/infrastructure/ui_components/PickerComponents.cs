namespace JoinCode.Cli;

// ─── ProviderPicker ───

/// <summary>
/// 供应商选择器 — CLI 简化版
/// </summary>
public sealed class ProviderPicker {
    /// <summary>
    /// 显示供应商选择列表并等待用户输入
    /// </summary>
    /// <param name="defaultProvider">默认供应商名称，回车时使用</param>
    /// <param name="title">选择标题</param>
    /// <param name="hint">提示文本</param>
    /// <param name="registry">供应商定义注册表</param>
    /// <returns>选中的供应商名称，失败时返回默认供应商</returns>
    public static string? Show(string defaultProvider, string title, string hint, IProviderDefinitionRegistry registry) {
        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{AnsiStyleEnumConstants.Bold}{title}{AnsiStyleEnumConstants.Reset}");
        if (!string.IsNullOrEmpty(hint)) {
            TerminalHelper.WriteLine($"{TerminalColors.Muted}{hint}{AnsiStyleEnumConstants.Reset}");
        }
        TerminalHelper.NewLine();

        var providers = registry.GetRegisteredProviders()
            .Select(p => registry.TryGet(p))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
        for (var i = 0; i < providers.Count; i++) {
            var p = providers[i];
            var marker = p.ProviderName == defaultProvider ? " (默认)" : "";
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleEnumConstants.Reset} {p.DisplayName}{marker}");
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteRaw($"请选择供应商 (1-{providers.Count}, 直接回车使用默认): ");

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) return defaultProvider;

        try {
            var input = TerminalHelper.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) return defaultProvider;

            if (int.TryParse(input.Trim(), out var index) && index >= 1 && index <= providers.Count) {
                return providers[index - 1].ProviderName;
            }

            return defaultProvider;
        } catch {
            return defaultProvider;
        }
    }
}



// ─── ModelPicker ───

/// <summary>
/// 模型选择器 — CLI 简化版
/// </summary>
public sealed class ModelPicker {
    /// <summary>
    /// 渲染模型选择列表为带选中标记和操作提示的文本
    /// </summary>
    /// <param name="models">可选模型数组</param>
    /// <param name="selectedIndex">当前选中索引</param>
    /// <param name="currentModelId">当前模型 ID</param>
    /// <param name="providerName">供应商名称</param>
    /// <param name="effortLevel">推理努力级别</param>
    /// <param name="isFastModeActive">是否启用快速模式</param>
    /// <returns>渲染后的文本</returns>
    public string Render(ModelEntry[] models, int selectedIndex, string currentModelId, string providerName, EffortLevel effortLevel, bool isFastModeActive) {
        var sb = new StringBuilder();
        sb.AppendLine($"{AnsiStyleEnumConstants.Bold}模型选择 ({providerName}){AnsiStyleEnumConstants.Reset}");
        sb.AppendLine();

        for (var i = 0; i < models.Length; i++) {
            var model = models[i];
            var marker = model.Id.Equals(currentModelId, StringComparison.OrdinalIgnoreCase) ? " *" : "";
            var selector = i == selectedIndex ? ">" : " ";
            sb.AppendLine($"  {selector} {model.DisplayName}{marker}");
        }

        sb.AppendLine();
        sb.AppendLine($"  Effort: {effortLevel.ToValue()} | Fast mode: {(isFastModeActive ? "ON" : "OFF")}");
        sb.AppendLine($"{TerminalColors.Muted}  ↑↓ 选择 | ←→ Effort | Enter 确认 | Esc 取消{AnsiStyleEnumConstants.Reset}");

        return sb.ToString();
    }

    /// <summary>
    /// 在 Low、Medium、High、Max 之间循环切换推理努力级别
    /// </summary>
    /// <param name="current">当前努力级别</param>
    /// <param name="forward">true 向前循环，false 向后循环</param>
    /// <returns>切换后的努力级别</returns>
    public static EffortLevel CycleEffort(EffortLevel current, bool forward) {
        var values = new[] { EffortLevel.Low, EffortLevel.Medium, EffortLevel.High, EffortLevel.Max };
        var idx = Array.IndexOf(values, current);
        if (idx < 0) idx = 1; // default to Medium

        if (forward) {
            idx = idx < values.Length - 1 ? idx + 1 : 0;
        } else {
            idx = idx > 0 ? idx - 1 : values.Length - 1;
        }

        return values[idx];
    }

    /// <summary>
    /// 异步显示模型选择列表并等待用户输入
    /// </summary>
    /// <param name="currentModel">当前模型 ID</param>
    /// <param name="catalog">模型目录</param>
    /// <param name="provider">供应商名称</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>选中的模型 ID，失败时返回当前模型</returns>
    public static async Task<string?> ShowAsync(string currentModel, IModelCatalog catalog, string provider, CancellationToken ct = default) {
        await Task.CompletedTask.ConfigureAwait(false);

        var models = catalog.GetModelsForProvider(provider);
        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{AnsiStyleEnumConstants.Bold}选择模型{AnsiStyleEnumConstants.Reset}");
        TerminalHelper.NewLine();

        for (var i = 0; i < models.Length; i++) {
            var marker = models[i].Id == currentModel ? " *" : "";
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleEnumConstants.Reset} {models[i].DisplayName}{marker}");
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteRaw($"请选择 (1-{models.Length}, 回车保持当前): ");

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) return currentModel;

        try {
            var input = TerminalHelper.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) return currentModel;

            if (int.TryParse(input.Trim(), out var index) && index >= 1 && index <= models.Length) {
                return models[index - 1].Id;
            }

            return currentModel;
        } catch {
            return currentModel;
        }
    }
}