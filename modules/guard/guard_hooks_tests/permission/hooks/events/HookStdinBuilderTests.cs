namespace Core.Tests.Hooks.Events;

/// <summary>
/// HookStdinBuilder 测试（TASK071-D1+D2+D3）— 验证 stdin JSON 格式：
/// snake_case 字段名 + 基础字段（session_id/transcript_path/cwd/hook_event_name）+ 事件特定字段。
/// </summary>
public sealed class HookStdinBuilderTests {
    /// <summary>TASK071-D3 — 基础字段存在（session_id/transcript_path/cwd/hook_event_name）。</summary>
    [Fact]
    public void BuildStdinJson_ContainsBaseFields() {
        var input = new HookInput {
            Event = HookEvent.PreToolUse,
            SessionId = "test-session",
            Payload = new Dictionary<string, JsonElement>()
        };

        var json = HookStdinBuilder.BuildStdinJson(input, transcriptPath: "/tmp/transcript.jsonl");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("test-session", doc.RootElement.GetProperty("session_id").GetString());
        Assert.Equal("/tmp/transcript.jsonl", doc.RootElement.GetProperty("transcript_path").GetString());
        Assert.Equal("PreToolUse", doc.RootElement.GetProperty("hook_event_name").GetString());
        Assert.True(doc.RootElement.TryGetProperty("cwd", out _), "cwd 字段应存在");
    }

    /// <summary>TASK071-D2 — camelCase Payload 键转 snake_case（toolName → tool_name）。</summary>
    [Fact]
    public void BuildStdinJson_SnakeCaseFieldNames() {
        var input = new HookInput {
            Event = HookEvent.PreToolUse,
            SessionId = "s1",
            Payload = new Dictionary<string, JsonElement> {
                ["toolName"] = JsonElementHelper.FromString("bash"),
                ["toolUseId"] = JsonElementHelper.FromString("tu-123"),
                ["toolInput"] = JsonElementHelper.FromString("{}")
            }
        };

        var json = HookStdinBuilder.BuildStdinJson(input);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("bash", doc.RootElement.GetProperty("tool_name").GetString());
        Assert.Equal("tu-123", doc.RootElement.GetProperty("tool_use_id").GetString());
        Assert.Equal("{}", doc.RootElement.GetProperty("tool_input").GetString());
    }

    /// <summary>TASK071-D3 — hook_event_name 对齐基准事件名。</summary>
    [Fact]
    public void BuildStdinJson_HookEventName_MatchesBaseline() {
        var input = new HookInput {
            Event = HookEvent.PostToolUse,
            Payload = new Dictionary<string, JsonElement>()
        };

        var json = HookStdinBuilder.BuildStdinJson(input);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("PostToolUse", doc.RootElement.GetProperty("hook_event_name").GetString());
    }

    /// <summary>TASK071-D3 — 空 SessionId/transcriptPath 不抛异常（默认空字符串）。</summary>
    [Fact]
    public void BuildStdinJson_NullOptionalFields_DefaultsToEmpty() {
        var input = new HookInput {
            Event = HookEvent.SessionStart,
            Payload = new Dictionary<string, JsonElement>()
        };

        var json = HookStdinBuilder.BuildStdinJson(input);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(string.Empty, doc.RootElement.GetProperty("session_id").GetString());
        Assert.Equal(string.Empty, doc.RootElement.GetProperty("transcript_path").GetString());
    }
}
