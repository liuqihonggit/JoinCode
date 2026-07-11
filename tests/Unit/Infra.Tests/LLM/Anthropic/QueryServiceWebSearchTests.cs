namespace Infra.Tests.LLM;

using Api.LLM;
using Api.LLM.QueryServices.Anthropic;
using ChatApiMessage = JoinCode.Abstractions.LLM.Chat.ApiMessage;
using ChatMessageRole = JoinCode.Abstractions.LLM.Chat.MessageRole;

/// <summary>
/// AnthropicQueryService.ConvertAnthropicResponseToApiMessages 的 web_search 链路测试
/// 验证 server_tool_use / web_search_tool_result 内容块的解析
/// </summary>
public sealed class QueryServiceWebSearchTests
{
    #region server_tool_use 处理

    [Fact]
    public void ServerToolUse_SkipsBlock_DoesNotAppearInContent()
    {
        var response = new AnthropicMessagesResponse
        {
            Id = "msg_test",
            Model = "claude-sonnet-4-6",
            Role = "assistant",
            StopReason = AnthropicStopReason.EndTurn,
            Content =
            [
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.Text,
                    Text = "Let me search for that."
                },
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.ServerToolUse,
                    Id = "srv_tool_1",
                    Name = "web_search"
                },
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.Text,
                    Text = " Based on the results..."
                }
            ]
        };

        var messages = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        messages.Should().HaveCount(1);
        var content = messages[0].Content;
        content.Should().Contain("Let me search for that.");
        content.Should().Contain("Based on the results");
        content.Should().NotContain("web_search");
    }

    #endregion

    #region web_search_tool_result 处理

    [Fact]
    public void WebSearchToolResult_ExtractsLinksAsMarkdown()
    {
        var searchContent = /*lang=json,strict*/ """[{"title":"Example Site","url":"https://example.com"},{"title":"Another Site","url":"https://another.com"}]""";
        var response = new AnthropicMessagesResponse
        {
            Id = "msg_test",
            Model = "claude-sonnet-4-6",
            Role = "assistant",
            StopReason = AnthropicStopReason.EndTurn,
            Content =
            [
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.ServerToolUse,
                    Id = "srv_tool_1",
                    Name = "web_search"
                },
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.WebSearchToolResult,
                    Id = "wsr_1",
                    Content = JsonDocument.Parse(searchContent).RootElement.Clone()
                },
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.Text,
                    Text = "Here are the search results."
                }
            ]
        };

        var messages = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        messages.Should().HaveCount(1);
        messages[0].Content.Should().Contain("[Example Site](https://example.com)");
        messages[0].Content.Should().Contain("[Another Site](https://another.com)");
        messages[0].Content.Should().Contain("Here are the search results.");
        messages[0].Metadata.Should().ContainKey("web_search_results");
    }

    [Fact]
    public void WebSearchToolResult_ErrorCase_ExtractsErrorCode()
    {
        var errorContent = /*lang=json,strict*/ """{"error_code":"rate_limit_error"}""";
        var response = new AnthropicMessagesResponse
        {
            Id = "msg_test",
            Model = "claude-sonnet-4-6",
            Role = "assistant",
            StopReason = AnthropicStopReason.EndTurn,
            Content =
            [
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.ServerToolUse,
                    Id = "srv_tool_1",
                    Name = "web_search"
                },
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.WebSearchToolResult,
                    Id = "wsr_1",
                    Content = JsonDocument.Parse(errorContent).RootElement.Clone()
                }
            ]
        };

        var messages = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        messages.Should().HaveCount(1);
        messages[0].Content.Should().Contain("rate_limit_error");
        messages[0].Metadata.Should().NotContainKey("web_search_results");
    }

    [Fact]
    public void WebSearchToolResult_EmptyArray_NoLinksInContent()
    {
        var emptyContent = /*lang=json,strict*/ """[]""";
        var response = new AnthropicMessagesResponse
        {
            Id = "msg_test",
            Model = "claude-sonnet-4-6",
            Role = "assistant",
            StopReason = AnthropicStopReason.EndTurn,
            Content =
            [
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.ServerToolUse,
                    Id = "srv_tool_1",
                    Name = "web_search"
                },
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.WebSearchToolResult,
                    Id = "wsr_1",
                    Content = JsonDocument.Parse(emptyContent).RootElement.Clone()
                }
            ]
        };

        var messages = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        messages.Should().HaveCount(1);
        messages[0].Content.Should().NotContain("](");
    }

    [Fact]
    public void WebSearchToolResult_MultipleSearches_AllLinksExtracted()
    {
        var search1 = /*lang=json,strict*/ """[{"title":"Result 1","url":"https://r1.com"}]""";
        var search2 = /*lang=json,strict*/ """[{"title":"Result 2","url":"https://r2.com"}]""";
        var response = new AnthropicMessagesResponse
        {
            Id = "msg_test",
            Model = "claude-sonnet-4-6",
            Role = "assistant",
            StopReason = AnthropicStopReason.EndTurn,
            Content =
            [
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.ServerToolUse,
                    Id = "srv_1",
                    Name = "web_search"
                },
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.WebSearchToolResult,
                    Id = "wsr_1",
                    Content = JsonDocument.Parse(search1).RootElement.Clone()
                },
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.Text,
                    Text = "First search done."
                },
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.ServerToolUse,
                    Id = "srv_2",
                    Name = "web_search"
                },
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.WebSearchToolResult,
                    Id = "wsr_2",
                    Content = JsonDocument.Parse(search2).RootElement.Clone()
                }
            ]
        };

        var messages = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        messages.Should().HaveCount(1);
        messages[0].Content.Should().Contain("[Result 1](https://r1.com)");
        messages[0].Content.Should().Contain("[Result 2](https://r2.com)");
        messages[0].Content.Should().Contain("First search done.");
    }

    [Fact]
    public void WebSearchToolResult_MissingTitleOrUrl_SkipsInvalidEntries()
    {
        var searchContent = /*lang=json,strict*/ """[{"title":"Valid","url":"https://valid.com"},{"title":"No URL"},{"url":"https://no-title.com"}]""";
        var response = new AnthropicMessagesResponse
        {
            Id = "msg_test",
            Model = "claude-sonnet-4-6",
            Role = "assistant",
            StopReason = AnthropicStopReason.EndTurn,
            Content =
            [
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.ServerToolUse,
                    Id = "srv_1",
                    Name = "web_search"
                },
                new AnthropicResponseContentBlock
                {
                    Type = AnthropicContentBlockType.WebSearchToolResult,
                    Id = "wsr_1",
                    Content = JsonDocument.Parse(searchContent).RootElement.Clone()
                }
            ]
        };

        var messages = AnthropicQueryService.ConvertAnthropicResponseToApiMessages(response);

        messages.Should().HaveCount(1);
        messages[0].Content.Should().Contain("[Valid](https://valid.com)");
        messages[0].Content.Should().NotContain("No URL");
        messages[0].Content.Should().NotContain("no-title.com");
    }

    #endregion
}
