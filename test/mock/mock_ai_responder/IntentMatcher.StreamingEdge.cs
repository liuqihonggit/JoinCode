namespace MockAiResponder;

/// <summary>D1-D20: 流式响应边缘场景匹配 — 从 IntentMatcher 提取的 partial 方法</summary>
internal static partial class IntentMatcher {
    /// <summary>D1-D20: 流式响应边缘场景匹配</summary>
    private static (InjectDto Response, string Description)? MatchStreamingEdgeIntent(string userMessage) {
        var lower = userMessage.ToLowerInvariant();
        var id = $"chatcmpl-{Guid.NewGuid():N}"[..30];

        if (lower.Contains("d1\n") || lower.Contains("role only")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D1: 只有 role chunk 无 content");
        }

        if (lower.Contains("d2\n") || lower.Contains("empty content between")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = "hello" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = "" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = "world" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D2: 中间穿插空 content chunk");
        }

        if (lower.Contains("d3\n") || lower.Contains("usage first")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant" } }], Usage = new OpenAiUsage { PromptTokens = 10, CompletionTokens = 5 } }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = "d3 response" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D3: usage 在首个 chunk 而非末尾");
        }

        if (lower.Contains("d4\n") || lower.Contains("empty choices")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = "d4 after empty" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D4: 中间穿插空 choices 数组");
        }

        if (lower.Contains("d5\n") || lower.Contains("null delta")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = null }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant", Content = "d5 response" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D5: 首个 chunk delta 为 null");
        }

        if (lower.Contains("d6\n") || lower.Contains("char by char")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant" } }] })
            };
            foreach (var c in "d6_char_by_char_streaming_test") {
                chunks.Add(SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = c.ToString() } }] }));
            }
            chunks.Add(SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }));
            chunks.Add("data: [DONE]\n\n");
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D6: 逐字符流式（28+ chunk）");
        }

        if (lower.Contains("d7\n") || lower.Contains("interleaved")) {
            var tcId = $"call_{Guid.NewGuid():N}"[..20];
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant", Content = "Starting " } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { ToolCalls = [new StreamToolCallDelta { Index = 0, Id = tcId, Type = "function", Function = new StreamToolCallFunctionDelta { Name = "bash", Arguments = "" } }] } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = "analysis " } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { ToolCalls = [new StreamToolCallDelta { Index = 0, Function = new StreamToolCallFunctionDelta { Arguments = """{"command":"echo d7"}""" } }] } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = "done" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "tool_calls" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D7: content 和 tool_call 交错 chunk");
        }

        if (lower.Contains("d8\n") || lower.Contains("length finish")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant", Content = "d8 partial response truncated" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "length" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D8: finish_reason=length 部分内容");
        }

        if (lower.Contains("d9\n") || lower.Contains("content filter")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "content_filter" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D9: finish_reason=content_filter 无内容");
        }

        if (lower.Contains("d10\n") || lower.Contains("done then extra")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant", Content = "d10 response" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]\n\n",
                "data: {\"extra\":\"should be ignored\"}\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D10: [DONE] 后有额外数据行");
        }

        if (lower.Contains("d11\n") || lower.Contains("done whitespace")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant", Content = "d11 response" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]   \n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D11: [DONE] 带尾随空格");
        }

        if (lower.Contains("d12\n") || lower.Contains("no done marker")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant", Content = "d12 no done" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] })
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D12: 缺少 [DONE] 标记");
        }

        if (lower.Contains("d13\n") || lower.Contains("sse comment")) {
            var chunks = new List<string> {
                ": ping\n\n",
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant", Content = "d13 response" } }] }),
                ": keepalive\n\n",
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D13: SSE 注释行混合数据");
        }

        if (lower.Contains("d14\n") || lower.Contains("extra fields")) {
            var chunks = new List<string> {
                $"data: {JsonSerializer.Serialize(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant", Content = "d14 response" } }] }, JsonOpts).Insert(1, "\"extra_field\":\"should_ignore\",")}\n\n",
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D14: chunk 含额外字段");
        }

        if (lower.Contains("d15\n") || lower.Contains("model change")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Model = "model-a", Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant", Content = "d15 " } }] }),
                SseLine(new StreamChunk { Id = id, Model = "model-b", Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = "model changed" } }] }),
                SseLine(new StreamChunk { Id = id, Model = "model-b", Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D15: 流中 model 名变化");
        }

        if (lower.Contains("d16\n") || lower.Contains("empty content chunks")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant", Content = "" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = "" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = "d16 only this" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D16: 多个空 content chunk");
        }

        if (lower.Contains("d17\n") || lower.Contains("whitespace only")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant", Content = "   " } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = "\n\t\n" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = "d17 actual content" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D17: 纯空白 content chunk");
        }

        if (lower.Contains("d18\n") || lower.Contains("reasoning content")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { ReasoningContent = "thinking about d18" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Content = "d18 response" } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D18: 含 reasoning_content 字段");
        }

        if (lower.Contains("d19\n") || lower.Contains("multiple choices")) {
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [
                    new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant", Content = "d19 choice0" } },
                    new StreamChoice { Index = 1, Delta = new StreamDelta { Role = "assistant", Content = "choice1 should be ignored" } }
                ] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "stop" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D19: 多 choices（n>1）");
        }

        if (lower.Contains("d20\n") || lower.Contains("index gap")) {
            var tcId1 = $"call_{Guid.NewGuid():N}"[..20];
            var tcId2 = $"call_{Guid.NewGuid():N}"[..20];
            var chunks = new List<string> {
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { Role = "assistant", ToolCalls = [new StreamToolCallDelta { Index = 0, Id = tcId1, Type = "function", Function = new StreamToolCallFunctionDelta { Name = "bash", Arguments = "" } }] } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { ToolCalls = [new StreamToolCallDelta { Index = 0, Function = new StreamToolCallFunctionDelta { Arguments = """{"command":"echo d20_a"}""" } }] } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { ToolCalls = [new StreamToolCallDelta { Index = 2, Id = tcId2, Type = "function", Function = new StreamToolCallFunctionDelta { Name = "bash", Arguments = "" } }] } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta { ToolCalls = [new StreamToolCallDelta { Index = 2, Function = new StreamToolCallFunctionDelta { Arguments = """{"command":"echo d20_b"}""" } }] } }] }),
                SseLine(new StreamChunk { Id = id, Choices = [new StreamChoice { Index = 0, Delta = new StreamDelta(), FinishReason = "tool_calls" }] }),
                "data: [DONE]\n\n"
            };
            return (new InjectDto { IsStream = true, ContentType = "text/event-stream", StreamChunks = chunks }, "D20: tool_call index 跳跃（0,2 缺 1）");
        }

        return null;
    }
}
