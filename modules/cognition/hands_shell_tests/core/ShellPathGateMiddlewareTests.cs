// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
﻿namespace Hands.Tests.Shell;

/// <summary>
/// ShellPathGateMiddleware 单元测试 — 验证路径门控中间件不再自动转换路径(报错让 LLM 自己修正)
/// 仅保留 UNC 路径警告(只读不改) + next 调用
/// </summary>
public class ShellPathGateMiddlewareTests {
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Theory]
    [InlineData("C:\\Users\\test", "bash")]
    [InlineData("C:\\Users\\test", "powershell")]
    [InlineData("/c/Users/test", "powershell")]
    [InlineData("/c/Users/test", "bash")]
    [InlineData(null, "bash")]
    [InlineData("", "bash")]
    public async Task InvokeAsync_DoesNotModifyWorkingDirectory(string? input, string kindId) {
        var kind = SystemActuatorKind.FromId(kindId)!;
        var probeService = new Mock<IEnvironmentProbeService>();

        var provider = CreateMockProvider(kind);
        await using var sut = new ShellPathGateMiddleware(probeService.Object);
        var context = new ShellPipelineContext {
            Command = "echo hello",
            Provider = provider.Object,
            WorkingDirectory = input,
        };

        await sut.InvokeAsync(context, static (_, _) => Task.CompletedTask, CancellationToken.None);

        context.WorkingDirectory.Should().Be(input);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Theory]
    [InlineData("echo D:\\a\\b\\c/d", "bash")]
    [InlineData("cat C:\\proj\\src/file.txt", "powershell")]
    public async Task InvokeAsync_DoesNotModifyCommand(string command, string kindId) {
        var kind = SystemActuatorKind.FromId(kindId)!;
        var probeService = new Mock<IEnvironmentProbeService>();

        var provider = CreateMockProvider(kind);
        await using var sut = new ShellPathGateMiddleware(probeService.Object);
        var context = new ShellPipelineContext {
            Command = command,
            Provider = provider.Object,
            WorkingDirectory = "/home/user",
        };

        await sut.InvokeAsync(context, static (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Command.Should().Be(command);
    }

    [Fact]
    public async Task InvokeAsync_CallsNextMiddleware() {
        var probeService = new Mock<IEnvironmentProbeService>();
        var provider = CreateMockProvider(SystemActuatorKind.Bash);
        await using var sut = new ShellPathGateMiddleware(probeService.Object);
        var context = new ShellPipelineContext {
            Command = "echo hello",
            Provider = provider.Object,
            WorkingDirectory = "/home/user",
        };

        var nextCalled = false;
        await sut.InvokeAsync(context, (ctx, ct) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    private static Mock<ISystemActuator> CreateMockProvider(SystemActuatorKind kind) {
        var mock = new Mock<ISystemActuator>();
        mock.SetupGet(x => x.Kind).Returns(kind);
        mock.SetupGet(x => x.ShellPath).Returns(kind == SystemActuatorKind.Bash ? "bash" : kind == SystemActuatorKind.PowerShell ? "pwsh" : "cmd.exe");
        return mock;
    }
}