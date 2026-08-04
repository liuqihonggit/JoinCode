namespace MockServer.E2E.Tests.Scripts;

public static class ClusterConversationScripts
{
    public static ConversationScript ClusterNonInteractive => new()
    {
        Name = "集群非交互模式",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "并行编写三个模块的文档",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "我已经分析了任务，可以分解为3个并行子任务。现在开始执行集群流程。"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有集群流程回复" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ],
        ExtraEnvVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JCC_CLUSTER_DECOMPOSITION_OVERRIDE"] = "3",
        },
        AdditionalArgs = "--await 30",
        MockServerExtraTextResponses =
        [
            "子任务1已完成：模块A的文档已编写。",
            "子任务2已完成：模块B的文档已编写。",
            "子任务3已完成：模块C的文档已编写。",
            "所有子任务结果已合并，没有冲突。集群执行成功。",
            "审查通过：3个子任务全部完成，合并结果正确，无回归问题。",
        ]
    };
}
