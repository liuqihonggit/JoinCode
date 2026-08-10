using FluentAssertions;

using JoinCode.Gui.Hosting;
using JoinCode.Abstractions.Configuration.Llm;
using JoinCode.Abstractions.LLM.Chat;

namespace JoinCode.Gui.Tests.Hosting;

/// <summary>
/// 占位会话门面测试 — 验证 UI 边界（IJccChatSession）在无真实引擎下可独立工作。
/// 不依赖真实引擎，确保解耦边界成立。
/// </summary>
public class PlaceholderChatSessionTests
{
    [Fact]
    public async Task StreamAsync_YieldsContentThenComplete()
    {
        await using var session = new PlaceholderChatSession();
        var events = new List<ChatStreamEvent>();

        await foreach (var evt in session.StreamAsync("hello").ConfigureAwait(false))
        {
            events.Add(evt);
        }

        events.Count(e => e.Type == ChatStreamEventType.Content).Should().BeGreaterThan(0);
        events.Last().Type.Should().Be(ChatStreamEventType.Complete);
    }

    [Fact]
    public async Task IsReady_DefaultsTrue()
    {
        await using var session = new PlaceholderChatSession();
        session.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task ClearHistory_Completes()
    {
        await using var session = new PlaceholderChatSession();
        await session.Invoking(s => s.ClearHistoryAsync()).Should().NotThrowAsync();
    }

    /// <summary>占位会话默认模型应从 ModelConfigLoader 读取，与真实引擎默认值对齐，避免热切换闪烁</summary>
    [Fact]
    public async Task CurrentModelId_AlignsWithConfigDefault()
    {
        await using var session = new PlaceholderChatSession();
        var configDefault = ModelConfigLoader.GetDefaultModelId("deepseek");

        session.CurrentProvider.Should().Be("deepseek");
        if (!string.IsNullOrEmpty(configDefault))
            session.CurrentModelId.Should().Be(configDefault, "占位会话模型应与配置默认值对齐");
        else
            session.CurrentModelId.Should().Be("deepseek-chat", "配置无默认值时回退硬编码");
    }
}