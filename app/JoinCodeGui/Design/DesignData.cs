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

        // 主会话1：快速排序实现（2 个子会话：1 完成 + 1 运行中）
        var s1 = new SessionItem { Id = "design-1", Title = "快速排序实现", IsExpanded = true };
        s1.Children.Add(new SessionItem
        {
            Id = "design-1-1",
            Title = "单元测试生成",
            ParentId = "design-1",
            SubSessionState = "Completed",
            HasWorktree = true,
            WorktreePath = @"D:\project\w4\.worktrees\design-1-1"
        });
        s1.Children.Add(new SessionItem
        {
            Id = "design-1-2",
            Title = "性能基准测试",
            ParentId = "design-1",
            SubSessionState = "Running",
            HasWorktree = true,
            WorktreePath = @"D:\project\w4\.worktrees\design-1-2"
        });
        vm.Sessions.Add(s1);

        // 主会话2：API 设计讨论（3 个子会话：1 完成 + 1 失败 + 1 运行中）
        var s2 = new SessionItem { Id = "design-2", Title = "API 设计讨论", IsExpanded = true };
        s2.Children.Add(new SessionItem
        {
            Id = "design-2-1",
            Title = "OpenAPI 规范生成",
            ParentId = "design-2",
            SubSessionState = "Completed",
            HasWorktree = true,
            WorktreePath = @"D:\project\w4\.worktrees\design-2-1"
        });
        s2.Children.Add(new SessionItem
        {
            Id = "design-2-2",
            Title = "Mock 服务器搭建",
            ParentId = "design-2",
            SubSessionState = "Failed",
            HasWorktree = false
        });
        s2.Children.Add(new SessionItem
        {
            Id = "design-2-3",
            Title = "集成测试编写",
            ParentId = "design-2",
            SubSessionState = "Running",
            HasWorktree = true,
            WorktreePath = @"D:\project\w4\.worktrees\design-2-3"
        });
        vm.Sessions.Add(s2);

        // 主会话3：Bug 修复（无子会话）
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

        // 子代理运行组卡片（3 个子代理：完成 + 运行中 + 失败，含对话活动行）
        var run1 = new SubAgentRun
        {
            AgentId = "design-agent-1",
            Name = "explore",
            Description = "搜索快速排序实现",
            State = SubAgentRunState.Completed,
            IsSuccess = true,
            ToolUseCount = 3,
            ExecutionTimeMs = 4200,
            FinalOutput = "找到 QuickSort 实现在 MainViewModel.cs:1520"
        };
        run1._visibleActivities.Add("🔍 Grep 搜索 'QuickSort'");
        run1._visibleActivities.Add("📖 Read MainViewModel.cs");
        run1._visibleActivities.Add("✓ 分析完成");
        var run2 = new SubAgentRun
        {
            AgentId = "design-agent-2",
            Name = "test-gen",
            Description = "生成单元测试",
            State = SubAgentRunState.Running,
            ToolUseCount = 1
        };
        run2._visibleActivities.Add("⚙ 生成测试用例…");
        var run3 = new SubAgentRun
        {
            AgentId = "design-agent-3",
            Name = "refactor",
            Description = "重构方法签名",
            State = SubAgentRunState.Failed,
            IsSuccess = false,
            ToolUseCount = 2,
            ExecutionTimeMs = 1800,
            FinalOutput = "编译失败: CS0103 未找到 stopReason"
        };
        run3._visibleActivities.Add("⚙ 重构方法签名");
        run3._visibleActivities.Add("✗ 编译失败: CS0103");
        vm.Messages.Add(new ChatUiMessage
        {
            Role = MessageRole.Assistant,
            Content = string.Empty,
            Timestamp = DateTime.Now,
            Kind = ChatUiMessageKind.AgentRunGroup,
            AgentRuns = [new AgentRunVm(run1), new AgentRunVm(run2), new AgentRunVm(run3)]
        });

        vm.RunStatus.LatestActivity = "QuickSort 示例";
        vm.StatusText = "就绪";
    }
}
