namespace JoinCode.Hands.Scenarios;

[Scenario("build", "构建管理场景：查询构建队列状态、取消构建。",
    Tools = new[] { "build_queue_status", "build_cancel" },
    SuggestedFlow = "build_queue_status → build_cancel",
    Tips = "build_queue_status 查 pending/current/recent，build_cancel 取消指定构建。")]
internal static class BuildScenarioRegistration { }
