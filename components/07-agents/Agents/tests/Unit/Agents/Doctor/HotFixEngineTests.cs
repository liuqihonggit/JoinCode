namespace Core.Tests.Agents.Doctor;

public class HotFixEngineTests
{
    [Fact]
    public void ChooseAction_SourceCodePatch_ReturnsCorrectAction()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.LoopDetected, HotFixActionType.SourceCodePatch);

        var action = engine.ChooseAction(report);

        Assert.Equal(HotFixActionType.SourceCodePatch, action.ActionType);
        Assert.NotNull(action.Description);
    }

    [Fact]
    public void ChooseAction_SourceCodePatch_InfersFilePathFromEventProperties()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.LoopDetected, HotFixActionType.SourceCodePatch,
            triggeringEvents:
            [
                new DiagnosticEvent
                {
                    EventType = "loop_detected",
                    Properties = new Dictionary<string, string> { ["source_file"] = "src/Middleware.cs" }
                }
            ]);

        var action = engine.ChooseAction(report);

        Assert.Equal("src/Middleware.cs", action.TargetFilePath);
    }

    [Fact]
    public void ChooseAction_SourceCodePatch_InfersFilePathFromFilePathProperty()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.LoopDetected, HotFixActionType.SourceCodePatch,
            triggeringEvents:
            [
                new DiagnosticEvent
                {
                    EventType = "loop_detected",
                    Properties = new Dictionary<string, string> { ["file_path"] = "src/Handler.cs" }
                }
            ]);

        var action = engine.ChooseAction(report);

        Assert.Equal("src/Handler.cs", action.TargetFilePath);
    }

    [Fact]
    public void ChooseAction_LoopDetected_InfersMiddlewareFileFromTool()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.LoopDetected, HotFixActionType.SourceCodePatch,
            triggeringEvents:
            [
                new DiagnosticEvent
                {
                    EventType = "loop_detected",
                    Properties = new Dictionary<string, string> { ["tool"] = "Read" }
                }
            ]);

        var action = engine.ChooseAction(report);

        Assert.Equal("FileReadMiddleware.cs", action.TargetFilePath);
    }

    [Fact]
    public void ChooseAction_LoopDetected_NoEventProperties_ReturnsNullFilePath()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.LoopDetected, HotFixActionType.SourceCodePatch);

        var action = engine.ChooseAction(report);

        Assert.Null(action.TargetFilePath);
    }

    [Fact]
    public void ChooseAction_ConfigChange_SetsSettingsJsonPath()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.ToolPermissionDenied, HotFixActionType.ConfigChange);

        var action = engine.ChooseAction(report);

        Assert.Equal(HotFixActionType.ConfigChange, action.ActionType);
        Assert.Equal("settings.json", action.TargetFilePath);
    }

    [Fact]
    public void ChooseAction_ConfigChange_ToolPermissionDenied_AutoGeneratesPatchedContent()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.ToolPermissionDenied, HotFixActionType.ConfigChange,
            triggeringEvents:
            [
                new DiagnosticEvent
                {
                    EventType = "permission_denied",
                    Properties = new Dictionary<string, string> { ["tool"] = "Bash" }
                }
            ]);

        var action = engine.ChooseAction(report);

        Assert.Equal(HotFixActionType.ConfigChange, action.ActionType);
        Assert.NotNull(action.PatchedContent);
        Assert.Contains("Bash", action.PatchedContent);
    }

    [Fact]
    public void ChooseAction_ConfigChange_ToolExecutionError_NoAutoPatchedContent()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.ToolExecutionError, HotFixActionType.ConfigChange,
            triggeringEvents:
            [
                new DiagnosticEvent
                {
                    EventType = "tool_error",
                    Properties = new Dictionary<string, string> { ["tool"] = "Read" }
                }
            ]);

        var action = engine.ChooseAction(report);

        Assert.Equal(HotFixActionType.ConfigChange, action.ActionType);
        Assert.Null(action.PatchedContent);
    }

    [Fact]
    public void ChooseAction_CompactContext_SetsCompactCommand()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.ContextOverflow, HotFixActionType.CompactContext);

        var action = engine.ChooseAction(report);

        Assert.Equal(HotFixActionType.CompactContext, action.ActionType);
        Assert.Equal("/compact", action.CommandToSend);
    }

    [Fact]
    public void ChooseAction_RestartProcess_ReturnsCorrectAction()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.ProcessHung, HotFixActionType.RestartProcess);

        var action = engine.ChooseAction(report);

        Assert.Equal(HotFixActionType.RestartProcess, action.ActionType);
        Assert.NotNull(action.Description);
    }

    [Fact]
    public void ChooseAction_ApiError_SetsSettingsJsonPath()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.ApiError, HotFixActionType.ConfigChange);

        var action = engine.ChooseAction(report);

        Assert.Equal(HotFixActionType.ConfigChange, action.ActionType);
        Assert.Equal("settings.json", action.TargetFilePath);
    }

    [Fact]
    public async Task ExecuteFix_CompactContext_SendsCommand()
    {
        var engine = CreateEngine(out var transportMock);
        var report = CreateReport(DiagnosticRuleId.ContextOverflow, HotFixActionType.CompactContext);

        transportMock
            .Setup(t => t.SendCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await engine.ExecuteFixAsync(report);

        Assert.True(result.Success);
        Assert.Equal(HotFixActionType.CompactContext, result.Action.ActionType);
        transportMock.Verify(t => t.SendCommandAsync(It.IsAny<string>(), It.Is<string>(s => s.Contains("/compact")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteFix_CompactContext_SendFails_ReturnsFailure()
    {
        var engine = CreateEngine(out var transportMock);
        var report = CreateReport(DiagnosticRuleId.ContextOverflow, HotFixActionType.CompactContext);

        transportMock
            .Setup(t => t.SendCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("stdin closed"));

        var result = await engine.ExecuteFixAsync(report);

        Assert.False(result.Success);
        Assert.Contains("stdin closed", result.Description);
    }

    [Fact]
    public async Task ExecuteFix_SourceCodePatch_NoTargetFile_ReturnsFailure()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.LoopDetected, HotFixActionType.SourceCodePatch);

        var result = await engine.ExecuteFixAsync(report);

        Assert.False(result.Success);
        Assert.Contains("未指定目标文件路径", result.Description);
    }

    [Fact]
    public async Task ExecuteFix_SourceCodePatch_NoPatchedContent_ReturnsFailure()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.LoopDetected, HotFixActionType.SourceCodePatch);

        var result = await engine.ExecuteFixAsync(report);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteFix_ConfigChange_NoPatchedContent_ReturnsFailure()
    {
        var engine = CreateEngine();
        var report = new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.ApiError,
            Severity = DiagnosticSeverity.Error,
            Description = "API 连续失败",
            SuggestedFixType = HotFixActionType.ConfigChange,
            SuggestedFixDescription = "检查 API 配置"
        };

        var result = await engine.ExecuteFixAsync(report);

        Assert.False(result.Success);
        Assert.Contains("未指定配置内容", result.Description);
    }

    [Fact]
    public async Task ExecuteFix_NoneAction_ReturnsSuccess()
    {
        var engine = CreateEngine();
        var report = new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.LoopDetected,
            Severity = DiagnosticSeverity.Info,
            Description = "test",
            SuggestedFixType = HotFixActionType.None
        };

        var result = await engine.ExecuteFixAsync(report);

        Assert.True(result.Success);
        Assert.Equal("无需修复", result.Description);
    }

    [Fact]
    public async Task ExecuteFix_ResultsAccumulate()
    {
        var engine = CreateEngine(out var transportMock);
        transportMock
            .Setup(t => t.SendCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var report1 = CreateReport(DiagnosticRuleId.ContextOverflow, HotFixActionType.CompactContext);
        var report2 = CreateReport(DiagnosticRuleId.ContextOverflow, HotFixActionType.CompactContext);

        await engine.ExecuteFixAsync(report1);
        await engine.ExecuteFixAsync(report2);

        Assert.Equal(2, engine.Results.Count);
    }

    [Fact]
    public async Task ExecuteFix_FixAppliedEventFired()
    {
        var engine = CreateEngine(out var transportMock);
        transportMock
            .Setup(t => t.SendCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        HotFixResult? captured = null;
        engine.FixApplied += (_, r) => captured = r;

        var report = CreateReport(DiagnosticRuleId.ContextOverflow, HotFixActionType.CompactContext);
        await engine.ExecuteFixAsync(report);

        Assert.NotNull(captured);
        Assert.True(captured.Success);
    }

    [Fact]
    public async Task ExecuteFix_NullReport_ThrowsArgumentNullException()
    {
        var engine = CreateEngine();
        await Assert.ThrowsAsync<ArgumentNullException>(() => engine.ExecuteFixAsync(null!));
    }

    [Fact]
    public async Task ExecuteFix_SourceCodePatch_FileNotExists_ReturnsFailure()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.LoopDetected, HotFixActionType.SourceCodePatch);

        var action = engine.ChooseAction(report) with
        {
            TargetFilePath = "nonexistent.cs",
            PatchedContent = "new content"
        };

        var result = await engine.ExecuteFixAsync(report);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteFix_ConfigChange_WithPatchedContent_WritesFile()
    {
        var fs = new InMemoryFileSystem();
        var engine = CreateEngine(fs, out var transportMock);
        transportMock
            .Setup(t => t.SendCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fs.WriteAllText("settings.json", "{\"old\": true}");

        var report = new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.ToolPermissionDenied,
            Severity = DiagnosticSeverity.Error,
            Description = "工具权限被拒绝",
            SuggestedFixType = HotFixActionType.ConfigChange,
            SuggestedFixDescription = "添加权限配置"
        };

        var result = await engine.ExecuteFixAsync(report);

        Assert.False(result.Success);
    }

    [Fact]
    public void ChooseAction_LoopDetected_SourcePropertyInfersFile()
    {
        var engine = CreateEngine();
        var report = CreateReport(DiagnosticRuleId.LoopDetected, HotFixActionType.SourceCodePatch,
            triggeringEvents:
            [
                new DiagnosticEvent
                {
                    EventType = "loop_detected",
                    Properties = new Dictionary<string, string> { ["source"] = "src/LoopHandler.cs" }
                }
            ]);

        var action = engine.ChooseAction(report);

        Assert.Equal("src/LoopHandler.cs", action.TargetFilePath);
    }

    private static HotFixEngine CreateEngine()
    {
        return CreateEngine(new InMemoryFileSystem(), out _);
    }

    private static HotFixEngine CreateEngine(out Mock<IDoctorTransport> transportMock)
    {
        return CreateEngine(new InMemoryFileSystem(), out transportMock);
    }

    private static HotFixEngine CreateEngine(InMemoryFileSystem fs, out Mock<IDoctorTransport> transportMock)
    {
        var processService = new Mock<IProcessService>();

        var patientManager = new PatientProcessManager(processService.Object);
        var patcher = new SourceCodePatcher(fs);
        var builder = new BuildOrchestrator(processService.Object);
        transportMock = new Mock<IDoctorTransport>();
        transportMock.Setup(t => t.IsConnected).Returns(true);
        transportMock.Setup(t => t.ConnectedPatientIds).Returns(new List<string> { "test-patient" });

        var engine = new HotFixEngine(
            patcher,
            builder,
            patientManager,
            transportMock.Object,
            fs);

        return engine;
    }

    private static DiagnosticReport CreateReport(DiagnosticRuleId ruleId, HotFixActionType fixType,
        IReadOnlyList<DiagnosticEvent>? triggeringEvents = null)
    {
        return new DiagnosticReport
        {
            RuleId = ruleId,
            Severity = DiagnosticSeverity.Warning,
            Description = $"测试诊断: {ruleId}",
            SuggestedFixType = fixType,
            SuggestedFixDescription = $"建议修复: {fixType}",
            TriggeringEvents = triggeringEvents ?? []
        };
    }
}
