namespace MockServer.Core.Tests;

/// <summary>
/// MockServerController 单元测试 — 验证注入队列、模式切换、录制状态管理逻辑。
/// 不启动 HTTP 服务器，纯逻辑测试。
/// </summary>
public sealed class MockServerControllerTests {
    [Fact]
    public void Inject_ThenConsume_ReturnsInjectedResponse() {
        var controller = new MockServerController();
        var response = new InjectedResponse { Body = "test body", StatusCode = 200 };

        controller.Inject(response);
        controller.ConsumeInjection().Should().BeEquivalentTo(response);
    }

    [Fact]
    public void ConsumeInjection_EmptyQueue_ReturnsNull() {
        var controller = new MockServerController();
        controller.ConsumeInjection().Should().BeNull();
    }

    [Fact]
    public void Inject_MultipleResponses_ConsumedInFifoOrder() {
        var controller = new MockServerController();
        controller.Inject(new InjectedResponse { Body = "first" });
        controller.Inject(new InjectedResponse { Body = "second" });

        controller.ConsumeInjection()!.Body.Should().Be("first");
        controller.ConsumeInjection()!.Body.Should().Be("second");
        controller.ConsumeInjection().Should().BeNull();
    }

    [Fact]
    public void ClearInjections_EmptiesQueue() {
        var controller = new MockServerController();
        controller.Inject(new InjectedResponse { Body = "a" });
        controller.Inject(new InjectedResponse { Body = "b" });

        controller.ClearInjections();

        var state = controller.GetState(0);
        state.InjectQueueLength.Should().Be(0);
    }

    [Fact]
    public void Mode_DefaultIsInteractive() {
        var controller = new MockServerController();
        controller.Mode.Should().Be(MockServerMode.Interactive);
    }

    [Fact]
    public void StartRecording_SwitchesToRecordingMode() {
        var controller = new MockServerController();
        controller.StartRecording(new RecordStartRequest { Upstream = "https://api.openai.com/v1", ApiKey = "key", Vendor = "openai" });

        controller.Mode.Should().Be(MockServerMode.Recording);
        var state = controller.GetState(0);
        state.RecordingUpstream.Should().Be("https://api.openai.com/v1");
    }

    [Fact]
    public void StopRecording_ReturnsToInteractiveMode() {
        var controller = new MockServerController();
        controller.StartRecording(new RecordStartRequest { Upstream = "https://api.openai.com/v1" });
        controller.RecordInteraction(new RecordedInteraction {
            Index = 0, Method = "POST", Path = "/v1/chat/completions",
            RequestBody = "{}", StatusCode = 200, ResponseBody = "{}", ResponseContentType = "application/json"
        });

        var result = controller.StopRecording();

        controller.Mode.Should().Be(MockServerMode.Interactive);
        result.InteractionCount.Should().Be(1);
    }

    [Fact]
    public void RecordInteraction_OnlyRecordsInRecordingMode() {
        var controller = new MockServerController();
        var interaction = new RecordedInteraction {
            Index = 0, Method = "POST", Path = "/test",
            RequestBody = "{}", StatusCode = 200, ResponseBody = "{}", ResponseContentType = "application/json"
        };

        controller.RecordInteraction(interaction);
        controller.GetState(0).RecordedInteractions.Should().Be(0);

        controller.StartRecording(new RecordStartRequest { Upstream = "https://upstream" });
        controller.RecordInteraction(interaction);
        controller.GetState(0).RecordedInteractions.Should().Be(1);
    }

    [Fact]
    public void LoadReplay_SwitchesToReplayMode_AndQueuesInjections() {
        var controller = new MockServerController();
        var file = new RecordingFile {
            Upstream = "https://api.openai.com/v1",
            Vendor = "openai",
            Interactions = [
                new RecordedInteraction { Index = 0, Method = "POST", Path = "/v1/chat/completions", RequestBody = "{}", StatusCode = 200, ResponseBody = "resp1", ResponseContentType = "application/json" },
                new RecordedInteraction { Index = 1, Method = "POST", Path = "/v1/chat/completions", RequestBody = "{}", StatusCode = 200, ResponseBody = "resp2", ResponseContentType = "application/json" }
            ]
        };

        controller.LoadReplay(file);

        controller.Mode.Should().Be(MockServerMode.Replay);
        var state = controller.GetState(0);
        state.InjectQueueLength.Should().Be(2);
    }

    [Fact]
    public void GetState_ReflectsCurrentState() {
        var controller = new MockServerController();
        controller.Inject(new InjectedResponse { Body = "a" });
        controller.Inject(new InjectedResponse { Body = "b" });

        var state = controller.GetState(totalRequests: 5);

        state.Mode.Should().Be(MockServerMode.Interactive);
        state.TotalRequests.Should().Be(5);
        state.InjectQueueLength.Should().Be(2);
        state.RecordedInteractions.Should().Be(0);
    }

    [Fact]
    public void GetRecordingConfig_ReturnsUpstreamAndKey() {
        var controller = new MockServerController();
        controller.StartRecording(new RecordStartRequest { Upstream = "https://upstream", ApiKey = "secret", Vendor = "anthropic" });

        var (upstream, apiKey, vendor) = controller.GetRecordingConfig();

        upstream.Should().Be("https://upstream");
        apiKey.Should().Be("secret");
        vendor.Should().Be("anthropic");
    }

    [Fact]
    public void ControlState_SerializesToJson() {
        var state = new ControlState {
            Mode = MockServerMode.Interactive,
            TotalRequests = 5,
            InjectQueueLength = 2,
            RecordedInteractions = 0
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(state, MockServerJsonContext.Default.ControlState);
        var text = Encoding.UTF8.GetString(json);

        text.Should().Contain("\"mode\":2");
        text.Should().Contain("\"total_requests\":5");
    }

    [Fact]
    public void InjectedResponse_DeserializesFromJson() {
        var json = """{"status_code":429,"content_type":"application/json","body":"{\"error\":\"rate\"}"}""";

        var response = JsonSerializer.Deserialize(json, MockServerJsonContext.Default.InjectedResponse);

        response.Should().NotBeNull();
        response!.StatusCode.Should().Be(429);
        response.Body.Should().Contain("rate");
    }
}
