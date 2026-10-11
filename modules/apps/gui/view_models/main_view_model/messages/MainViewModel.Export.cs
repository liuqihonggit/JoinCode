namespace JoinCode.Gui.ViewModels;

/// <summary>会话 Markdown 导出，保留消息顺序和代码块，排除系统提示词注入。</summary>
public sealed partial class MainViewModel {
    /// <summary>当前可见会话的 Markdown 快照，不受正文搜索过滤影响。</summary>
    public string ExportSessionMarkdown {
        get {
            var title = (_activeSession?.Title ?? "JoinCode 会话").Replace('\r', ' ').Replace('\n', ' ');
            var builder = new System.Text.StringBuilder().Append("# ").AppendLine(title).AppendLine();
            foreach (var message in Messages.Where(IsExportableMessage)) {
                builder.Append("## ").Append(message.RoleLabel).Append(" · ")
                    .AppendLine(message.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                builder.AppendLine().AppendLine(message.Content).AppendLine();
            }
            return builder.ToString();
        }
    }

    /// <summary>导出对话内容；内部系统提示词不出现在分享文件中。</summary>
    private static bool IsExportableMessage(ChatUiMessage message) =>
        message.Role != MessageRole.System && message.Kind != ChatUiMessageKind.SystemPromptInjection
        && !string.IsNullOrWhiteSpace(message.Content);
}
