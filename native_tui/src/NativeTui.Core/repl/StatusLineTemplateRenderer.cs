namespace NativeTui.Core.Repl;

/// <summary>
/// statusline 模板渲染器 — 激活 settings 键 statusline.format（/statusline format 写入，
/// StatuslineCommand 声明变量集）：{model}/{tokens}/{mode}/{time}/{cost} 替换
///（未知变量原样保留、null 变量替换为空串 — 模板作者可见缺失源而非占位符堆积）。
/// <para>纯函数（注入变量值，测试友好）；消费点 ReplShell footer 投影（Row2 自定义状态行）。</para>
/// </summary>
public static class StatusLineTemplateRenderer
{
    /// <summary>渲染模板 — 变量替换（大小写不敏感，未知变量原样保留）。</summary>
    /// <param name="template">模板文本（如 "{model} · {tokens} · {time}"）。</param>
    /// <param name="model">当前模型 ID（null = 空）。</param>
    /// <param name="tokens">token 用量文本（null = 空）。</param>
    /// <param name="mode">权限模式名（null = 空）。</param>
    /// <param name="time">当前时间文本（null = 空）。</param>
    /// <param name="cost">成本文本（null = 空）。</param>
    /// <returns>替换后的状态行文本。</returns>
    public static string Render(string template, string? model, string? tokens, string? mode, string? time, string? cost = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        return template
            .Replace("{model}", model ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{tokens}", tokens ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{mode}", mode ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", time ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{cost}", cost ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
