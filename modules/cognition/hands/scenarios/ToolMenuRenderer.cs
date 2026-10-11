namespace JoinCode.Hands.Scenarios;

/// <summary>
/// 工具菜单统一渲染器 — 所有情景模式菜单输出经此渲染，保证格式一致。
/// <para>ToJson: 生成 AI 消费的 JSON 菜单（工具返回值）。</para>
/// <para>ToText: 生成人类可读的多行文本（终端 -h 输出）。</para>
/// </summary>
public static class ToolMenuRenderer {
    /// <summary>
    /// 将情景模式渲染为 JSON 菜单字符串 — 供工具返回值（AI 消费）。
    /// </summary>
    public static string ToJson(ScenarioInfo scenario) {
        var dto = new ScenarioMenuDto([
            new ScenarioMenuSceneDto(scenario.Name, scenario.Description, scenario.Tools, scenario.SuggestedFlow, scenario.Tips)
        ]);
        return JsonSerializer.Serialize(dto, ScenarioMenuJsonContext.Relaxed.ScenarioMenuDto);
    }

    /// <summary>
    /// 将情景模式渲染为多行文本 — 供终端输出（人类可读）。
    /// </summary>
    public static string ToText(ScenarioInfo scenario) {
        var sb = new StringBuilder();
        sb.AppendLine($"情景模式: {scenario.Name}");
        sb.AppendLine();
        sb.AppendLine(scenario.Description);
        sb.AppendLine();
        sb.AppendLine("工具集:");
        foreach (var tool in scenario.Tools)
            sb.AppendLine($"  - {tool}");
        sb.AppendLine();
        sb.AppendLine($"建议流程: {scenario.SuggestedFlow}");
        if (!string.IsNullOrEmpty(scenario.Tips)) {
            sb.AppendLine();
            sb.AppendLine($"提示: {scenario.Tips}");
        }
        return sb.ToString();
    }
}
