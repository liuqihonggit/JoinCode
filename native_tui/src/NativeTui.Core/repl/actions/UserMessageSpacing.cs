namespace NativeTui.Core.Repl;

/// <summary>
/// 连续用户消息紧凑判定（消息操作域）— 对齐基准 Message.tsx isUserContinuation：连续用户消息间
/// margin 压缩为 0（排队消息连续上屏等场景 — 基准仅 image 分支消费，本项目语义推广到用户消息间距）。
/// 用户消息类项判定 = UserMessageItem 或 IUserContinuationAware 实现（S2 接线后同一项两判皆中；
/// 判定并集保持未接线期 fake/新用户消息类项可测）。
/// </summary>
public static class UserMessageSpacing
{
    /// <summary>判定索引处用户消息是否为连续消息（严格前驱亦为用户消息类项 — banner/divider 等装饰项介入即断开；越界索引防御返回 false）。</summary>
    public static bool IsUserContinuation(IReadOnlyList<IScrollItem> items, int index) =>
        index > 0 && index < items.Count && IsUserItem(items[index]) && IsUserItem(items[index - 1]);

    /// <summary>用户消息类项判定（UserMessageItem 或紧凑感知接口实现）。</summary>
    private static bool IsUserItem(IScrollItem item) => item is UserMessageItem or IUserContinuationAware;
}
