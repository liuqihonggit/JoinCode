// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR


namespace JoinCode.CliNode.System;

/// <summary>
/// system 分组独立 CLI.exe — 第 9 个「逐组做实」样板。
/// <para>本地实现 24 个: sleep / sleep_until / get_process_info、config 读、错误诊断、permission 读写
/// （规则文件 {gitRoot}/.jcc/permission/rules.json 是唯一真相源，宿主按内容戳感知外部改写）、
/// model_search / policy_list、通知交互、brief_status。</para>
/// -h 文本来自现有 [McpTool] 特性；入口逻辑统一在 CliNodeRunner。
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "system",
        Title = "system 分组",
        HelpResource = "JoinCode.CliNodes.Groups.system.help.txt",
        Resolve = SystemCommands.Find,
        LocalToolIds = () => SystemCommands.LocalToolIds,
        HostReason = "config_set / brief_mode / memory 写 / cron 写读的是宿主启动期快照或进程内状态，"
            + "agent_*/analytics_* 是活进程数据，ask_user_question / confirm_action 是 TUI 回调——均留宿主。"
    }, args);
}
