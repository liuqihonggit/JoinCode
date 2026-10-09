// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Host.Tests.Cli;

/// <summary>
/// CliOutputEnvelope&lt;T&gt; 泛型信封单元测试 — 验证 AOT 兼容序列化 + 编译期类型安全。
/// <para>泛型化目的：传 JsonObject/JsonNode 等未注册类型时编译报错（无 Success 重载），
/// 所有 Data 类型必须在 CliOutputJsonContext 注册 JsonSerializableAttribute。</para>
/// </summary>
public sealed class CliOutputEnvelopeTests {

    [Fact]
    public void Success_Generic_ShouldSerializeRgJsonResult() {
        var matches = new List<RgJsonMatch> {
            new("src/foo.cs", 3, ["1", "5", "10"]),
            new("src/bar.cs", 1, null),
        };
        var data = new RgJsonResult(matches, 4, 2);
        var json = CliOutputEnvelope<RgJsonResult>.Success(data).ToJsonString();

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"totalMatches\":4");
        json.Should().Contain("\"fileCount\":2");
        json.Should().Contain("\"file\":\"src/foo.cs\"");
        json.Should().Contain("\"count\":3");
        json.Should().Contain("\"lines\":[\"1\",\"5\",\"10\"]");
    }

    [Fact]
    public void Success_Generic_ShouldSerializeRgJsonResult_EmptyLinesOmitted() {
        var data = new RgJsonResult([new("x.cs", 1, null)], 1, 1);
        var json = CliOutputEnvelope<RgJsonResult>.Success(data).ToJsonString();

        json.Should().Contain("\"count\":1");
        json.Should().NotContain("\"lines\"");
    }

    [Fact]
    public void Success_Generic_ShouldSerializeMcpServeExitReport() {
        var data = new McpServeExitReport("http", 40, 100, 5, 3.14);
        var json = CliOutputEnvelope<McpServeExitReport>.Success(data).ToJsonString();

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"transport\":\"http\"");
        json.Should().Contain("\"toolCount\":40");
        json.Should().Contain("\"uptimeSeconds\":3.14");
    }

    [Fact]
    public void Success_Generic_ShouldSerializeCliSchemaResult() {
        var props = new[] {
            new CliSchemaPropertyDto("--help", "-h", "帮助", "boolean", false, null, "基础", null),
            new CliSchemaPropertyDto("--model", "-m", "模型", "string", true, null, "基础", "jcc -m gpt-4o"),
        };
        var data = new CliSchemaResult(props);
        var json = CliOutputEnvelope<CliSchemaResult>.Success(data).ToJsonString();

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"name\":\"--help\"");
        json.Should().Contain("\"shortName\":\"-h\"");
        json.Should().Contain("\"example\":\"jcc -m gpt-4o\"");
    }

    [Fact]
    public void Success_Generic_ShouldSerializeCliNonInteractiveResult() {
        var data = new CliNonInteractiveResult(0, "hello world");
        var json = CliOutputEnvelope<CliNonInteractiveResult>.Success(data).ToJsonString();

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"exitCode\":0");
        json.Should().Contain("\"response\":\"hello world\"");
    }

    [Fact]
    public void Success_Generic_ShouldSerializeToolResult() {
        var result = new JoinCode.Abstractions.Tools.ToolResult {
            Content = [new JoinCode.Abstractions.Tools.ToolContent { Text = "hello" }],
            IsError = false,
        };
        var json = CliOutputEnvelope<JoinCode.Abstractions.Tools.ToolResult>.Success(result).ToJsonString();

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"text\":\"hello\"");
    }

    [Fact]
    public void Success_Generic_ShouldIncludeMeta() {
        var data = new CliNonInteractiveResult(0, "");
        var meta = new CliOutputMeta { TotalCount = 42, DurationMs = 100 };
        var json = CliOutputEnvelope<CliNonInteractiveResult>.Success(data, meta).ToJsonString();

        json.Should().Contain("\"totalCount\":42");
        json.Should().Contain("\"durationMs\":100");
    }

    [Fact]
    public void Fail_ShouldSerializeErrorEnvelope() {
        var error = new CliStructuredError("TEST_ERROR", "test message", "hint", false);
        var json = CliOutputEnvelope.Fail(error).ToString();

        json.Should().Contain("\"ok\":false");
        json.Should().Contain("\"code\":\"TEST_ERROR\"");
        json.Should().Contain("\"message\":\"test message\"");
        json.Should().Contain("\"hint\":\"hint\"");
    }

    [Fact]
    public void Fail_ShouldNotContainDataField() {
        var error = new CliStructuredError("ERR", "msg", null, false);
        var json = CliOutputEnvelope.Fail(error).ToString();

        json.Should().NotContain("\"data\"");
    }

    [Fact]
    public void Success_Generic_Data_ShouldBeStrongTyped() {
        var data = new RgJsonResult([], 0, 0);
        var envelope = CliOutputEnvelope<RgJsonResult>.Success(data);

        envelope.Data.Should().BeSameAs(data);
        envelope.Ok.Should().BeTrue();
        envelope.SchemaVersion.Should().Be("1");
    }

    [Fact]
    public void Success_Generic_ShouldSerializeListOfCliToolListItem() {
        var items = new List<CliToolListItem> {
            new("gh_pr_list", "列出 PR", "github", null, ""),
            new("gh_pr_view", "查看 PR", "github", null, ""),
        };
        var meta = new CliOutputMeta { TotalCount = 2 };
        var json = CliOutputEnvelope<List<CliToolListItem>>.Success(items, meta).ToJsonString();

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"totalCount\":2");
        json.Should().Contain("\"name\":\"gh_pr_list\"");
        json.Should().Contain("\"name\":\"gh_pr_view\"");
    }

    [Fact]
    public void Success_Generic_ShouldSerializeListOfCliSlashCommandListItem() {
        var items = new List<CliSlashCommandListItem> {
            new("/compact", "压缩", "/compact [指令]", "Session", ["comp"]),
        };
        var json = CliOutputEnvelope<List<CliSlashCommandListItem>>.Success(items).ToJsonString();

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"name\":\"/compact\"");
        json.Should().Contain("\"aliases\":[\"comp\"]");
    }

    [Fact]
    public void Success_Generic_ShouldSerializeToolSchema() {
        var schema = new JoinCode.Abstractions.Tools.ToolSchema {
            Properties = {
                ["pr_number"] = new JoinCode.Abstractions.Tools.ToolSchemaProperty { Type = "string", Description = "PR 编号" }
            },
            Required = ["pr_number"]
        };
        var json = CliOutputEnvelope<JoinCode.Abstractions.Tools.ToolSchema>.Success(schema, new CliOutputMeta { TotalCount = 1 }).ToJsonString();

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"type\":\"object\"");
        json.Should().Contain("\"pr_number\"");
        json.Should().Contain("\"required\":[\"pr_number\"]");
        json.Should().Contain("\"totalCount\":1");
    }

    [Fact]
    public void Success_Generic_ShouldDeserializeToolSchema() {
        var schema = new JoinCode.Abstractions.Tools.ToolSchema {
            Properties = {
                ["pr_number"] = new JoinCode.Abstractions.Tools.ToolSchemaProperty { Type = "string", Description = "PR 编号" }
            },
            Required = ["pr_number"]
        };
        var json = CliOutputEnvelope<JoinCode.Abstractions.Tools.ToolSchema>.Success(schema).ToJsonString();
        var envelope = System.Text.Json.JsonSerializer.Deserialize<CliOutputEnvelope<JoinCode.Abstractions.Tools.ToolSchema>>(json, CliOutputJsonContext.Default.Options);

        envelope.Should().NotBeNull();
        envelope!.Ok.Should().BeTrue();
        envelope.Data!.Type.Should().Be("object");
        envelope.Data.Properties.Should().ContainKey("pr_number");
        envelope.Data.Required.Should().Contain("pr_number");
    }

    [Fact]
    public void Success_Generic_ShouldSerializeCliSlashSchemaHintResult() {
        var data = new CliSlashSchemaHintResult("compact", null, "<指令>");
        var json = CliOutputEnvelope<CliSlashSchemaHintResult>.Success(data).ToJsonString();

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"command\":\"compact\"");
        json.Should().Contain("\"argumentHint\":\"<指令>\"");
        json.Should().NotContain("\"schema\"");
    }

    [Fact]
    public void Success_Generic_ShouldDeserializeCliSlashSchemaHintResult() {
        var data = new CliSlashSchemaHintResult("help", null, null);
        var json = CliOutputEnvelope<CliSlashSchemaHintResult>.Success(data).ToJsonString();
        var envelope = System.Text.Json.JsonSerializer.Deserialize<CliOutputEnvelope<CliSlashSchemaHintResult>>(json, CliOutputJsonContext.Default.Options);

        envelope.Should().NotBeNull();
        envelope!.Ok.Should().BeTrue();
        envelope.Data!.Command.Should().Be("help");
        envelope.Data.Schema.Should().BeNull();
        envelope.Data.ArgumentHint.Should().BeNull();
    }

    [Fact]
    public void Success_Generic_ShouldSerializeCliSlashSchemaHintResult_WithSchema() {
        var schema = new JoinCode.Abstractions.Tools.ToolSchema {
            Properties = { ["arg"] = new JoinCode.Abstractions.Tools.ToolSchemaProperty { Type = "string" } }
        };
        var data = new CliSlashSchemaHintResult("cmd", schema, null);
        var json = CliOutputEnvelope<CliSlashSchemaHintResult>.Success(data).ToJsonString();

        json.Should().Contain("\"command\":\"cmd\"");
        json.Should().Contain("\"schema\":{");
        json.Should().NotContain("\"argumentHint\"");
    }
}
