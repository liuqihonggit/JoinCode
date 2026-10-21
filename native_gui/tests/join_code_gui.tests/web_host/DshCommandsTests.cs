namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// DSH 斜杠命令路由测试（TASK058 B5）：commands/list 参数契约与元数据投影翻译。
/// 引擎执行分支（SlashCommandRunner）由真机验收覆盖。
/// </summary>
public sealed class DshCommandsTests {
    /// <summary>构造 commands/execute 请求（位置参数 wire 平铺：agentId + line，DTO + JsonContext）</summary>
    private static JsonElement ExecuteArgs(string? agentId, string? line) =>
        JsonSerializer.SerializeToElement(
            new DshCommandsExecuteArgs { AgentId = agentId ?? "", Line = line },
            DshJsonContext.Default.DshCommandsExecuteArgs);

    /// <summary>无引擎测试分发器</summary>
    private static DshRpcDispatcher NewDispatcher() =>
        new(new DshHostServices(new InMemoryFileSystem()));

    [Fact]
    public async Task CommandsList_WithoutEngine_ReturnsEmptyStableOk() {
        var result = await NewDispatcher().HandleAsync("commands/list", default(JsonElement));

        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.GetValueOrDefault().GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task CommandsExecute_MissingLine_RejectsWithGuidance() {
        var result = await NewDispatcher().HandleAsync("commands/execute", ExecuteArgs("s1", null));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("commands/rejected");
    }

    [Fact]
    public async Task CommandsExecute_EngineNotReady_ReturnsStableNotReadyError() {
        var result = await NewDispatcher().HandleAsync("commands/execute", ExecuteArgs("s1", "/model deepseek-v4-pro"));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("commands/not-ready");
    }

    [Fact]
    public void Projection_StripsLeadingSlash_AndMapsHint() {
        var metadata = new SlashCommandMetadata {
            Name = "/model",
            Description = "切换模型",
            ArgumentHint = "model-id",
        };

        var info = DshCommandsProjection.Project(metadata);

        info.Name.Should().Be("model", "前端以 /${name} 组装命令行，宿主去掉前缀");
        info.Description.Should().Be("切换模型");
        var input = info.Input.Should().BeOfType<DshCommandInputInfo>().Subject;
        input.Hint.Should().Be("model-id");
    }

    [Fact]
    public void Projection_NoHint_OmitsInput() {
        var info = DshCommandsProjection.Project(new SlashCommandMetadata { Name = "/clear", Description = "清空" });

        info.Input.Should().BeNull();
    }
}
