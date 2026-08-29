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
        var sub1_1 = new SessionItem
        {
            Id = "design-1-1", Title = "单元测试生成", ParentId = "design-1",
            SubSessionState = "Completed", HasWorktree = true,
            WorktreePath = @"D:\project\w4\.worktrees\design-1-1"
        };
        sub1_1.SubSessionMessages = CreateSubMessages(
            "为 QuickSort 方法生成单元测试",
            "已生成 5 个测试用例，覆盖正常/边界/空数组场景",
            "Write test/QuickSortTests.cs", "已创建测试文件 (23 行)");
        s1.Children.Add(sub1_1);
        var sub1_2 = new SessionItem
        {
            Id = "design-1-2", Title = "性能基准测试", ParentId = "design-1",
            SubSessionState = "Running", HasWorktree = true,
            WorktreePath = @"D:\project\w4\.worktrees\design-1-2"
        };
        sub1_2.SubSessionMessages = CreateSubMessages(
            "对 QuickSort 做性能基准测试",
            "正在运行基准测试…");
        s1.Children.Add(sub1_2);
        vm.Sessions.Add(s1);

        // 主会话2：API 设计讨论（3 个子会话：1 完成 + 1 失败 + 1 运行中）
        var s2 = new SessionItem { Id = "design-2", Title = "API 设计讨论", IsExpanded = true };
        var sub2_1 = new SessionItem
        {
            Id = "design-2-1", Title = "OpenAPI 规范生成", ParentId = "design-2",
            SubSessionState = "Completed", HasWorktree = true,
            WorktreePath = @"D:\project\w4\.worktrees\design-2-1"
        };
        sub2_1.SubSessionMessages = CreateSubMessages(
            "根据 API 代码生成 OpenAPI 规范",
            "已生成 openapi.yaml，包含 12 个端点定义",
            "Write openapi.yaml", "已创建 OpenAPI 规范文件 (156 行)");
        s2.Children.Add(sub2_1);
        var sub2_2 = new SessionItem
        {
            Id = "design-2-2", Title = "Mock 服务器搭建", ParentId = "design-2",
            SubSessionState = "Failed", HasWorktree = false
        };
        sub2_2.SubSessionMessages = CreateSubMessages(
            "基于 OpenAPI 搭建 Mock 服务器",
            "❌ Mock 服务器启动失败：端口 9901 被占用");
        s2.Children.Add(sub2_2);
        var sub2_3 = new SessionItem
        {
            Id = "design-2-3", Title = "集成测试编写", ParentId = "design-2",
            SubSessionState = "Running", HasWorktree = true,
            WorktreePath = @"D:\project\w4\.worktrees\design-2-3"
        };
        sub2_3.SubSessionMessages = CreateSubMessages(
            "编写 API 集成测试",
            "正在生成集成测试用例…",
            "Read openapi.yaml", "读取规范文件成功");
        s2.Children.Add(sub2_3);
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

    /// <summary>创建子会话模拟消息（指令 + 可选工具调用 + 回复）</summary>
    private static List<ChatUiMessage> CreateSubMessages(string instruction, string response, string? toolName = null, string? toolResult = null)
    {
        var msgs = new List<ChatUiMessage>
        {
            new() { Role = MessageRole.User, Content = instruction, Timestamp = DateTime.Now }
        };
        if (toolName is not null)
        {
            msgs.Add(new ChatUiMessage { Role = MessageRole.Assistant, Content = string.Empty, Timestamp = DateTime.Now, Kind = ChatUiMessageKind.ToolCall, ToolName = toolName });
            msgs.Add(new ChatUiMessage { Role = MessageRole.Assistant, Content = string.Empty, Timestamp = DateTime.Now, Kind = ChatUiMessageKind.ToolResult, ToolName = toolName, ToolResultText = toolResult });
        }
        msgs.Add(new ChatUiMessage { Role = MessageRole.Assistant, Content = response, Timestamp = DateTime.Now });
        return msgs;
    }
}
