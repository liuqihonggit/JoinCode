namespace JoinCode.ChatCommands;

/// <summary>
/// /version 命令 — 对齐 TS version.ts
/// TS 使用 MACRO.VERSION + MACRO.BUILD_TIME，C# 使用 Assembly 版本
/// 对齐内容：版本号+运行时+架构+OS
/// 架构差异：TS 有构建时间戳，C# 使用 Assembly 信息
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Version, Description = "显示版本信息", Usage = "/version", Category = ChatCommandCategory.Info)]
public sealed class VersionCommand : ChatCommandBase {
    /// <summary>
    /// 执行版本命令,输出 JoinCode 版本号、运行时、架构与操作系统信息
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>表示命令执行结果的任务,始终返回 Continue 以继续会话</returns>
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        var assemblyVersion = typeof(VersionCommand).Assembly.GetName().Version;
        var appVersion = assemblyVersion?.ToString() ?? "1.0.0";
        var snapshot = JoinCode.Abstractions.Utils.EnvironmentSnapshot.CaptureQuick();

        TerminalHelper.WriteLine($"{TerminalColors.Primary}JoinCode{AnsiStyleEnumConstants.Reset}");
        TerminalHelper.WriteLine($"  版本: {appVersion}");
        TerminalHelper.WriteLine($"  运行时: {snapshot.FrameworkDescription} (.NET {snapshot.RuntimeVersion})");
        TerminalHelper.WriteLine($"  架构: {snapshot.ProcessArchitecture}");
        TerminalHelper.WriteLine($"  OS: {snapshot.OsDescription}");

        return Task.FromResult(ChatCommandResult.Continue());
    }
}