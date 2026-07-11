namespace Infra.Tests.LLM;

using Api.LLM;
using Api.LLM.QueryServices.OpenAI;
using JoinCode.Abstractions.Utils;
using ChatApiMessage = JoinCode.Abstractions.LLM.Chat.ApiMessage;
using ChatMessageRole = JoinCode.Abstractions.LLM.Chat.MessageRole;

/// <summary>
/// OpenAIQueryService.ConvertToOpenAIMessage 角色转换测试
/// 验证 CacheBreak metadata 不应改变消息角色 — 修复前缀缓存破坏 Bug
/// </summary>
public sealed class QueryServiceRoleConversionTests
{
    /// <summary>
    /// 带 CacheBreak=true 的 System 消息必须保持 System 角色发送
    /// 否则 LLM API 前缀缓存被破坏（system 变 user 导致前缀不一致）
    /// </summary>
    [Fact]
    public void SystemMessage_WithCacheBreakMetadata_PreservesSystemRole()
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            ["CacheBreak"] = JsonElementHelper.FromBoolean(true)
        };
        var msg = new ChatApiMessage(ChatMessageRole.System, "# 使用Agent工具", metadata);

        var result = OpenAIQueryService.ConvertToOpenAIMessage(msg);

        result.Role.Should().Be("system");
        result.Content.Should().Be("# 使用Agent工具");
    }

    /// <summary>
    /// 无 CacheBreak metadata 的 System 消息保持 System 角色（回归保护）
    /// </summary>
    [Fact]
    public void SystemMessage_WithoutCacheBreakMetadata_PreservesSystemRole()
    {
        var msg = new ChatApiMessage(ChatMessageRole.System, "静态系统提示词");

        var result = OpenAIQueryService.ConvertToOpenAIMessage(msg);

        result.Role.Should().Be("system");
        result.Content.Should().Be("静态系统提示词");
    }

    /// <summary>
    /// CacheBreak=false 的 System 消息保持 System 角色
    /// </summary>
    [Fact]
    public void SystemMessage_WithCacheBreakFalse_PreservesSystemRole()
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            ["CacheBreak"] = JsonElementHelper.FromBoolean(false)
        };
        var msg = new ChatApiMessage(ChatMessageRole.System, "动态系统消息", metadata);

        var result = OpenAIQueryService.ConvertToOpenAIMessage(msg);

        result.Role.Should().Be("system");
    }

    /// <summary>
    /// User 消息不受 CacheBreak 影响（回归保护）
    /// </summary>
    [Fact]
    public void UserMessage_WithCacheBreakMetadata_PreservesUserRole()
    {
        var metadata = new Dictionary<string, JsonElement>
        {
            ["CacheBreak"] = JsonElementHelper.FromBoolean(true)
        };
        var msg = new ChatApiMessage(ChatMessageRole.User, "用户输入", metadata);

        var result = OpenAIQueryService.ConvertToOpenAIMessage(msg);

        result.Role.Should().Be("user");
        result.Content.Should().Be("用户输入");
    }

    /// <summary>
    /// 端到端验证：静态 System + 动态 System(CacheBreak) + User 三段消息
    /// 全部保持原有角色，模拟真实 AssembleMessages 输出
    /// </summary>
    [Fact]
    public void ThreePartMessages_StaticDynamicUser_AllRolesPreserved()
    {
        var staticMsg = new ChatApiMessage(ChatMessageRole.System, "静态前缀");
        var dynamicMsg = new ChatApiMessage(
            ChatMessageRole.System,
            "# 使用Agent工具",
            new Dictionary<string, JsonElement> { ["CacheBreak"] = JsonElementHelper.FromBoolean(true) });
        var userMsg = new ChatApiMessage(ChatMessageRole.User, "读取 README 文件");

        var results = new[] { staticMsg, dynamicMsg, userMsg }
            .Select(OpenAIQueryService.ConvertToOpenAIMessage)
            .ToList();

        results.Should().HaveCount(3);
        results[0].Role.Should().Be("system");
        results[1].Role.Should().Be("system");
        results[2].Role.Should().Be("user");
    }
}
