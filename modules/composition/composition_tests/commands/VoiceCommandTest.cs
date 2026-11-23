namespace Composition.Tests.Commands;

/// <summary>
/// /voice 命令单元测试 — 覆盖 on/start/record 启动录制/off/stop 停止录制(含识别结果渲染)/
/// 无参显示状态(NullAction=Status)/停止异常降级/服务缺失静默/别名归一
/// <para>IVoiceService 经 DI 注入;继承 ToggleCommandBase,start/record→On、stop→Off 映射</para>
/// </summary>
[Collection("command-output")]
public sealed class VoiceCommandTest {
    private static (VoiceCommand Command, Mock<IVoiceService> Voice, ChatCommandContext Context) Arrange(
        string arguments = "",
        bool withService = true,
        VoiceRecordingState state = VoiceRecordingState.Idle,
        bool isRecording = false,
        string? transcription = "识别出的文本",
        Exception? stopError = null) {
        var voice = new Mock<IVoiceService>();
        voice.Setup(v => v.State).Returns(state);
        voice.Setup(v => v.IsRecording).Returns(isRecording);
        voice.Setup(v => v.StartRecordingAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        if (stopError is not null) {
            voice.Setup(v => v.StopRecordingAsync(It.IsAny<CancellationToken>())).ThrowsAsync(stopError);
        } else {
            voice.Setup(v => v.StopRecordingAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VoiceRecordingResult {
                    Success = true,
                    AudioData = [],
                    Duration = TimeSpan.FromSeconds(2),
                    Transcription = transcription,
                });
        }

        var services = CommandTestHarness.CreateServices(
            configureDi: di => {
                if (withService) di.AddSingleton(voice.Object);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new VoiceCommand(), voice, context);
    }

    [Fact]
    public async Task Execute_On_StartsRecording() {
        var (cmd, voice, context) = Arrange(arguments: "on");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        voice.Verify(v => v.StartRecordingAsync(It.IsAny<CancellationToken>()), Times.Once, "on 应启动录音");
        output.Should().Contain("语音录制已开始，请说话...");
    }

    [Theory]
    [InlineData("start")]
    [InlineData("record")]
    public async Task Execute_StartAndRecordAliases_StartRecording(string action) {
        var (cmd, voice, context) = Arrange(arguments: action);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        voice.Verify(v => v.StartRecordingAsync(It.IsAny<CancellationToken>()), Times.Once,
            $"{action} 别名应映射为开");
        output.Should().Contain("语音录制已开始");
    }

    [Fact]
    public async Task Execute_Off_StopsRecordingAndShowsTranscription() {
        var (cmd, voice, context) = Arrange(arguments: "off", transcription: "你好世界");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        voice.Verify(v => v.StopRecordingAsync(It.IsAny<CancellationToken>()), Times.Once, "off 应停止录音");
        output.Should().Contain("语音录制已停止");
        output.Should().Contain("识别结果: 你好世界");
    }

    [Fact]
    public async Task Execute_StopAlias_SameAsOff() {
        var (cmd, voice, context) = Arrange(arguments: "stop", transcription: "停一下");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        voice.Verify(v => v.StopRecordingAsync(It.IsAny<CancellationToken>()), Times.Once, "stop 别名应映射为关");
        output.Should().Contain("识别结果: 停一下");
    }

    [Fact]
    public async Task Execute_Off_EmptyTranscription_OmitsResultLine() {
        var (cmd, voice, context) = Arrange(arguments: "off", transcription: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("语音录制已停止");
        output.Should().NotContain("识别结果:", "转录为空时不应渲染识别结果行");
    }

    [Fact]
    public async Task Execute_EmptyArgs_ShowsCurrentStatus() {
        var (cmd, voice, context) = Arrange(
            arguments: "", state: VoiceRecordingState.Recording, isRecording: true);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        voice.Verify(v => v.StartRecordingAsync(It.IsAny<CancellationToken>()), Times.Never,
            "无参 NullAction=Status,不应启动录音");
        output.Should().Contain("语音服务状态: Recording");
        output.Should().Contain("录制中: 是");
        output.Should().Contain("使用 /voice on 开始录制");
        output.Should().Contain("使用 /voice off 停止录制并识别");
    }

    [Fact]
    public async Task Execute_StatusArg_SameAsEmptyArgs() {
        var (cmd, voice, context) = Arrange(arguments: "status", state: VoiceRecordingState.Idle, isRecording: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        voice.Verify(v => v.StartRecordingAsync(It.IsAny<CancellationToken>()), Times.Never);
        output.Should().Contain("录制中: 否");
    }

    [Fact]
    public async Task Execute_StopThrows_ShowsErrorWithoutCrash() {
        var (cmd, voice, context) = Arrange(arguments: "off", stopError: new InvalidOperationException("mic busy"));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("停止录音异常不应中断会话");
        output.Should().Contain("停止语音录制失败");
        output.Should().Contain("mic busy");
    }

    [Fact]
    public async Task Execute_WithoutService_ReturnsContinueSilently() {
        var (cmd, voice, context) = Arrange(arguments: "on", withService: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("语音服务缺失应静默降级而非抛异常");
        voice.Verify(v => v.StartRecordingAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new VoiceCommand();
        cmd.Name.Should().Be("voice");
        cmd.Usage.Should().Be("/voice [on|off|status]");
        cmd.IsHidden.Should().BeTrue();
    }
}
