namespace Infra.Tests.LLM.OpenAI;

using Api.LLM;
using JoinCode.Abstractions.Utils;

/// <summary>
/// OpenAI 流式 usage 解析测试
///
/// 背景: 真实 OpenAI API 在 stream_options.include_usage=true 时,
/// 最后一个 chunk 包含 usage 字段(含 prompt_tokens_details.cached_tokens)。
/// 旧实现:
/// - OpenAIChatChunk 没有 Usage 字段 → 无法解析流式 usage
/// - OpenAIChatRequest 没有 StreamOptions 字段 → 无法发送 include_usage: true
/// - GetStreamEventContentsAsync 跳过 choices.Count==0 的 chunk → 丢弃最终 usage chunk
///
/// 修复目标:
/// 1. OpenAIChatChunk 添加 Usage 字段
/// 2. OpenAIChatRequest 添加 StreamOptions 字段 + OpenAIStreamOptions 类
/// 3. CreateRequest 在 stream=true 时设置 StreamOptions.IncludeUsage = true
/// 4. 流式循环解析最终 usage chunk, 通过 metadata["Usage"] 传递给消费者
/// </summary>
public sealed class OpenAIStreamingUsageTests
{
    // ============== 数据类型: OpenAIChatChunk.Usage ==============

    [Fact]
    public void OpenAIChatChunk_UsageField_DeserializesFromJson()
    {
        var json = """{"id":"chatcmpl-1","object":"chat.completion.chunk","created":1,"model":"gpt-4o","choices":[],"usage":{"prompt_tokens":100,"completion_tokens":50,"total_tokens":150,"prompt_tokens_details":{"cached_tokens":80}}}""";

        var chunk = JsonSerializer.Deserialize(json, NativeJsonContext.Default.OpenAIChatChunk);

        chunk.Should().NotBeNull();
        chunk!.Usage.Should().NotBeNull();
        chunk.Usage!.PromptTokens.Should().Be(100);
        chunk.Usage.CompletionTokens.Should().Be(50);
        chunk.Usage.PromptTokensDetails!.CachedTokens.Should().Be(80);
    }

    [Fact]
    public void OpenAIChatChunk_WithoutUsage_UsageFieldIsNull()
    {
        var json = """{"id":"chatcmpl-1","object":"chat.completion.chunk","created":1,"model":"gpt-4o","choices":[{"index":0,"delta":{"content":"hello"},"finish_reason":null}]}""";

        var chunk = JsonSerializer.Deserialize(json, NativeJsonContext.Default.OpenAIChatChunk);

        chunk.Should().NotBeNull();
        chunk!.Usage.Should().BeNull();
    }

    // ============== 数据类型: OpenAIChatRequest.StreamOptions ==============

    [Fact]
    public void OpenAIChatRequest_StreamOptions_SerializesIncludeUsageTrue()
    {
        var request = new OpenAIChatRequest
        {
            Model = "gpt-4o",
            Stream = true,
            StreamOptions = new OpenAIStreamOptions { IncludeUsage = true }
        };

        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.OpenAIChatRequest);

        // 必须包含 stream_options.include_usage: true
        json.Should().Contain("\"stream_options\"");
        json.Should().Contain("\"include_usage\":true");
    }

    [Fact]
    public void OpenAIChatRequest_WithoutStreamOptions_DoesNotSerializeStreamOptions()
    {
        var request = new OpenAIChatRequest
        {
            Model = "gpt-4o",
            Stream = false
        };

        var json = JsonSerializer.Serialize(request, NativeJsonContext.Default.OpenAIChatRequest);

        // stream_options 应被忽略 (WhenWritingNull)
        json.Should().NotContain("\"stream_options\"");
    }

    // ============== CreateRequest 行为 ==============

    /// <summary>
    /// CreateRequest 是 internal, 通过反射测试无法访问。
    /// 改为通过 GetApiMessageContentsAsync (非流式) 验证 OpenAIChatRequest 构建。
    /// 但 GetApiMessageContentsAsync 不暴露 request 对象。
    ///
    /// 替代方案: 直接验证 OpenAIChatRequest 的 StreamOptions 序列化行为
    /// (已在 OpenAIChatRequest_StreamOptions_SerializesIncludeUsageTrue 中验证)
    ///
    /// CreateRequest 内部行为通过 E2E 测试验证 (见 E2E 阶段)。
    /// </summary>
    [Fact]
    public void OpenAIStreamOptions_Default_HasIncludeUsageFalse()
    {
        var opts = new OpenAIStreamOptions();
        opts.IncludeUsage.Should().BeFalse();
    }

    [Fact]
    public void OpenAIStreamOptions_WithIncludeUsageTrue_SerializesCorrectly()
    {
        var opts = new OpenAIStreamOptions { IncludeUsage = true };

        var json = JsonSerializer.Serialize(opts, NativeJsonContext.Default.OpenAIStreamOptions);

        json.Should().Be("""{"include_usage":true}""");
    }
}
