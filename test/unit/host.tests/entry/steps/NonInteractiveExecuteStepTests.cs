// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
// SYSLIB0050 抑制: 测试中用 FormatterServices 创建未初始化对象验证 dry-run 不调用 session 方法
#pragma warning disable JCC11003, SYSLIB0050
namespace JoinCode.Entry.Tests;

/// <summary>
/// NonInteractiveExecuteStep 单元测试 — BUG-2 回归: dry-run 模式拦截。
/// <para>修复: 在 ProcessUserInputAsync 前检查 Options.DryRun,只输出试跑信息不调用 LLM。</para>
/// </summary>
public sealed class NonInteractiveExecuteStepTests {

    /// <summary>
    /// BUG-2 回归: dry-run 模式应设置 ExitCode=0 并调用 next,不调用 LLM。
    /// 验证手段: session 用未初始化对象,若错误调用 ProcessUserInputAsync 会因字段 NRE 抛异常。
    /// </summary>
    [Fact]
    public async Task DryRun_Should_Not_Call_LLM_And_Set_ExitCode_Zero() {
        // Arrange — dry-run 模式 + 未初始化 session(调用其方法必 NRE)
        await using var step = new NonInteractiveExecuteStep();
        var context = CreateContext(dryRun: true, prompt: "测试提示词");
        var nextCalled = false;

        // Act — dry-run 应在 ProcessUserInputAsync 之前 return,不触碰 session 方法
        await step.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — dry-run 模式: ExitCode=0,next 被调用,未调用 LLM(未抛 NRE 即证明)
        context.ExitCode.Should().Be(0, "dry-run 模式应设置 ExitCode=0");
        nextCalled.Should().BeTrue("dry-run 模式应继续管道(next 被调用)");
    }

    /// <summary>
    /// BUG-2 回归: 非 dry-run 模式应实际调用 session.ProcessUserInputAsync。
    /// 验证手段: session 用未初始化对象,调用 ProcessUserInputAsync 会因字段 NRE 抛异常,
    /// 该异常被 catch (Exception ex) 捕获并设置 ExitCode=GeneralError。
    /// </summary>
    [Fact]
    public async Task NonDryRun_Should_Call_LLM_ProcessUserInputAsync() {
        // Arrange — 非 dry-run + 未初始化 session(调用 ProcessUserInputAsync 必抛异常)
        await using var step = new NonInteractiveExecuteStep();
        var context = CreateContext(dryRun: false, prompt: "测试提示词");

        // Act — 非 dry-run 会调用 session.ProcessUserInputAsync,因未初始化抛异常被 catch
        await step.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        // Assert — 非 dry-run 调用了 LLM(因 NRE 被 catch),ExitCode=GeneralError(非0)
        context.ExitCode.Should().NotBe(0, "非 dry-run 模式调用未初始化 session 应抛异常,被 catch 后 ExitCode=GeneralError");
    }

    /// <summary>
    /// BUG-2 回归: dry-run 模式下空提示词应在 dry-run 检查之前返回 GeneralError。
    /// 验证 dry-run 拦截位于 prompt 非空检查之后(代码顺序: session null → prompt empty → dry-run)。
    /// </summary>
    [Fact]
    public async Task DryRun_With_Empty_Prompt_Should_Return_GeneralError_Before_DryRun() {
        // Arrange — dry-run 但 prompt 为空
        await using var step = new NonInteractiveExecuteStep();
        var context = CreateContext(dryRun: true, prompt: "");
        var nextCalled = false;

        // Act
        await step.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Assert — 空 prompt 在 dry-run 检查之前就返回 GeneralError
        context.ExitCode.Should().NotBe(0, "空提示词应返回 GeneralError");
        nextCalled.Should().BeFalse("空提示词应直接返回不调用 next");
    }

    private static StartupContext CreateContext(bool dryRun, string prompt) {
        var config = new WorkflowConfig {
            Provider = new ProviderConfig {
                ApiKey = "sk-test-key",
                Vendor = "openai",
                ModelId = "gpt-4o",
            }
        };

        // 用未初始化对象创建 CliSession — dry-run 模式不调用其方法所以安全;
        // 非 dry-run 模式调用 ProcessUserInputAsync 会因字段 NRE 抛异常,正好验证 LLM 被调用
        var session = (CliSession)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(CliSession));
        // IsRunning 默认 false(未初始化),需设为 true 否则 ProcessUserInputAsync 第237行直接 return
        typeof(CliSession).GetProperty("IsRunning")!.SetValue(session, true);

        return new StartupContext {
            Config = config,
            Options = new CommandLineOptions { DryRun = dryRun, NonInteractive = true },
            Host = new Mock<IHost>().Object,
            FileSystem = new InMemoryFileSystem(),
            Session = session,
            NonInteractivePrompt = prompt,
        };
    }
}
