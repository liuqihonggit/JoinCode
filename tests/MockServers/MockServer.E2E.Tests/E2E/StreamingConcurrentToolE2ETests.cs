namespace MockServer.E2E.Tests;

[Trait("Category", "Integration")]
public sealed class StreamingConcurrentToolE2ETests : CoverageTestBase
{
    public StreamingConcurrentToolE2ETests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ConcurrentSafeTools_ReadAndGrep_ShouldExecuteInParallel()
    {
        var script = new ConversationScript
        {
            Name = "流式并发: Read + Grep 并行执行",
            Mode = ConversationMode.NonInteractive,
            Turns =
            [
                new ConversationTurn
                {
                    UserInput = "Read the README and search for JoinCode in it at the same time",
                    AiResponse = new MockResponseScript
                    {
                        Type = MockResponseType.WithToolCalls,
                        TextResponse = "Reading and searching concurrently.",
                        ToolCalls =
                        [
                            new MockToolCallScript
                            {
                                ToolName = "Read",
                                Arguments = "{\"file_path\":\"D:\\3\\\\JoinCode\\\\README.md\",\"limit\":5}",
                                ToolResult = "# JoinCode\nAI coding assistant..."
                            },
                            new MockToolCallScript
                            {
                                ToolName = "Grep",
                                Arguments = "{\"pattern\":\"JoinCode\",\"path\":\"D:\\\\JoinCode\\\\README.md\",\"output_mode\":\"content\"}",
                                ToolResult = "1:# JoinCode"
                            }
                        ],
                        FollowUpText = "Both Read and Grep completed successfully in parallel."
                    },
                    Asserts =
                    [
                        new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "应调用 Read 工具" },
                        new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Grep", Description = "应调用 Grep 工具" },
                        new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                    ]
                }
            ]
        };
        await RunScriptAsync(script);
    }

    [Fact]
    public async Task NonSafeTools_TwoWrites_ShouldExecuteSequentially()
    {
        var script = new ConversationScript
        {
            Name = "流式并发: 两个 Write 应顺序执行",
            Mode = ConversationMode.NonInteractive,
            Turns =
            [
                new ConversationTurn
                {
                    UserInput = "Write two temp files",
                    AiResponse = new MockResponseScript
                    {
                        Type = MockResponseType.WithToolCalls,
                        TextResponse = "Writing two files sequentially.",
                        ToolCalls =
                        [
                            new MockToolCallScript
                            {
                                ToolName = "Write",
                                Arguments = "{\"file_path\":\"D:\\\\JoinCode\\\\.x\\\\e2e_streaming_1.txt\",\"content\":\"test1\"}",
                                ToolResult = "File written successfully"
                            },
                            new MockToolCallScript
                            {
                                ToolName = "Write",
                                Arguments = "{\"file_path\":\"D:\\\\JoinCode\\\\.x\\\\e2e_streaming_2.txt\",\"content\":\"test2\"}",
                                ToolResult = "File written successfully"
                            }
                        ],
                        FollowUpText = "Both writes completed sequentially."
                    },
                    Asserts =
                    [
                        new OutputAssert { Type = AssertType.ContainsToolCall, Expected = "Write:Write", Description = "应调用 Write 工具" },
                        new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                    ]
                }
            ]
        };
        await RunScriptAsync(script);
    }
}
