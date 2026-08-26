using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Design;

/// <summary>设计时数据 — 填充示例消息/会话/走马灯，供 XAML 设计器预览和 --design 截图模式（需求6）</summary>
public static class DesignData
{
    private static MainViewModel? _sample;

    /// <summary>设计时 MainViewModel 单例 — 供 d:DataContext 绑定（XAML 设计器预览）</summary>
    public static MainViewModel Sample
    {
        get
        {
            if (_sample is null)
            {
                _sample = new MainViewModel();
                Populate(_sample);
            }
            return _sample;
        }
    }

    /// <summary>填充 MainViewModel 的设计时示例数据（不覆盖已有数据）</summary>
    public static void Populate(MainViewModel vm)
    {
        if (vm.Messages.Count > 0)
            return;

        vm.Sessions.Add(new SessionItem { Id = "design-1", Title = "快速排序实现" });
        vm.Sessions.Add(new SessionItem { Id = "design-2", Title = "API 设计讨论" });
        vm.Sessions.Add(new SessionItem { Id = "design-3", Title = "Bug 修复: 内存泄漏" });

        vm.Messages.Add(new ChatUiMessage
        {
            Role = MessageRole.User,
            Content = "帮我写一个快速排序算法",
            Timestamp = DateTime.Now
        });
        vm.Messages.Add(new ChatUiMessage
        {
            Role = MessageRole.Assistant,
            Content = "好的，这是快速排序的 C# 实现：\n\n```csharp\nvoid QuickSort(int[] a, int lo, int hi)\n{\n    if (lo >= hi) return;\n    var p = a[(lo + hi) / 2];\n    int i = lo, j = hi;\n    while (i <= j)\n    {\n        while (a[i] < p) i++;\n        while (a[j] > p) j--;\n        if (i <= j) (a[i++], a[j--]) = (a[j], a[i]);\n    }\n    QuickSort(a, lo, j);\n    QuickSort(a, i, hi);\n}\n```\n\n时间复杂度 O(n log n)，空间 O(log n)。",
            Timestamp = DateTime.Now
        });
        vm.Messages.Add(new ChatUiMessage
        {
            Role = MessageRole.System,
            Content = "你是 JoinCode 助手，请用简洁清晰的中文回答。使用现代 C# 语法。",
            Timestamp = DateTime.Now,
            Kind = ChatUiMessageKind.SystemPromptInjection
        });
        vm.Messages.Add(new ChatUiMessage
        {
            Role = MessageRole.Assistant,
            Content = "用户要求快速排序，需要提供清晰高效的实现，考虑边界条件和性能。",
            Timestamp = DateTime.Now,
            Kind = ChatUiMessageKind.Thinking
        });

        vm.RunStatus.LatestActivity = "QuickSort 示例";
        vm.StatusText = "就绪";
    }
}
