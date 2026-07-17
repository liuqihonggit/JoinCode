namespace JoinCode.ChatCommands;

/// <summary>
/// /version 命令 — 对齐 TS version.ts
/// TS 使用 MACRO.VERSION + MACRO.BUILD_TIME，C# 使用 Assembly 版本
/// 对齐内容：版本号+运行时+架构+OS
/// 架构差异：TS 有构建时间戳，C# 使用 Assembly 信息
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Version, Description = "显示版本信息", Usage = "/version", Category = ChatCommandCategory.Info)]
public sealed class VersionCommand : ChatCommandBase
{
    public override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var assemblyVersion = typeof(VersionCommand).Assembly.GetName().Version;
        var appVersion = assemblyVersion?.ToString() ?? "1.0.0";
        var runtimeVersion = Environment.Version.ToString();

        TerminalHelper.WriteLine($"{TerminalColors.Primary}JoinCode{AnsiStyleConstants.Reset}");
        TerminalHelper.WriteLine($"  版本: {appVersion}");
        TerminalHelper.WriteLine($"  运行时: .NET {runtimeVersion}");
        TerminalHelper.WriteLine($"  架构: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        TerminalHelper.WriteLine($"  OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

        return Task.FromResult(ChatCommandResult.Continue());
    }
}