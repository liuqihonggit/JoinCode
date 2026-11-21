namespace JoinCode.Abstractions.Interfaces;

/// <summary>后台通知队列扩展 — 统一排空并格式化通知文本，供各宿主注入模型上下文。</summary>
public static class AgentNotificationQueueExtensions {
    /// <summary>
    /// 排空待处理通知并格式化为可注入模型的文本（shell 任务原样 XML，子代理通知加完成前缀）。
    /// </summary>
    /// <param name="queue">通知队列</param>
    /// <returns>格式化后的通知文本（无通知时为空列表）</returns>
    public static IReadOnlyList<string> DrainFormatted(this IAgentNotificationQueue queue) {
        ArgumentNullException.ThrowIfNull(queue);

        var pending = queue.DequeueAll();
        var texts = new List<string>(pending.Count);
        foreach (var notification in pending) {
            var isShellNotification = notification.Xml.Contains("<task-notification>", StringComparison.OrdinalIgnoreCase);
            texts.Add(isShellNotification
                ? notification.Xml
                : $"A background agent completed a task:\n{notification.Xml}");
        }

        return texts;
    }
}
