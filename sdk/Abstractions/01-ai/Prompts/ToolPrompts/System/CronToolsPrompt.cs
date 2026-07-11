
namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// CronCreateTool 提示词
/// </summary>
[ToolPrompt(ToolName = "CronCreate", Category = ToolPromptCategory.System)]
public static class CronCreateToolPrompt
{
    public const string ToolName = CronToolNameConstants.CronCreate;

    /// <summary>
    /// 获取工具描述
    /// </summary>
    public static string GetDescription(bool durableEnabled)
    {
        return durableEnabled
            ? $"安排一个提示在未来时间运行 —— 要么在 cron 计划上重复，要么在特定时间运行一次。传递 durable: true 以持久化到 {AppDataConstants.AppDataFolder}/{AppDataConstants.ScheduledTasksFileName}；否则仅会话。"
            : "安排一个提示在未来时间在此 Claude 会话中运行 —— 要么在 cron 计划上重复，要么在特定时间运行一次。";
    }

    /// <summary>
    /// 获取工具提示词
    /// </summary>
    public static string GetPrompt(bool durableEnabled, double defaultMaxAgeDays = WorkflowConstants.Worktree.StaleTimeoutDays)
    {
        var durabilitySection = durableEnabled
            ? $@"## 持久性

默认情况下（durable: false），作业仅在此会话中存在 —— 没有写入磁盘，退出时作业消失。传递 durable: true 以写入 {AppDataConstants.AppDataFolder}/{AppDataConstants.ScheduledTasksFileName}，以便作业在重启后存活。仅在用户明确要求任务持久化（""继续每天做这个""、""永久设置这个""）时使用 durable: true。大多数""5 分钟后提醒我""/""一小时后回来看看""的请求应该保持仅会话。"
            : @"## 仅会话

作业仅在此会话中存在 —— 没有写入磁盘，退出时作业消失。";

        var durableRuntimeNote = durableEnabled
            ? $"持久化作业持久化到 {AppDataConstants.AppDataFolder}/{AppDataConstants.ScheduledTasksFileName} 并在会话重启后自动恢复 —— 下次启动时它们自动恢复。错过的 REPL 关闭时的一次性持久化任务会被显示出来以供追赶。仅会话的作业随进程死亡。"
            : "";

        return $@"安排一个提示在未来时间入队。用于重复计划和一次性提醒。

使用用户本地时区中的标准 5 字段 cron：minute hour day-of-month month day-of-week。""0 9 * * *"" 表示上午 9 点本地 —— 不需要时区转换。

## 一次性任务（recurring: false）

对于""在 X 提醒我""或""在 <时间>，做 Y""的请求 —— 触发一次然后自动删除。
将 minute/hour/day-of-month/month 固定到特定值：
  ""今天下午 2:30 提醒我检查部署"" → cron: ""30 14 <today_dom> <today_month> *"", recurring: false
  ""明天早上，运行冒烟测试"" → cron: ""57 8 <tomorrow_dom> <tomorrow_month> *"", recurring: false

## 重复作业（recurring: true，默认）

对于""每 N 分钟""/""每小时""/""工作日上午 9 点""的请求：
  ""*/5 * * * *""（每 5 分钟），""0 * * * *""（每小时），""0 9 * * 1-5""（工作日上午 9 点本地）

## 在任务允许时避免 :00 和 :30 分钟标记

每个要求""上午 9 点""的用户得到 `0 9`，每个要求""每小时""的用户得到 `0 *` —— 这意味着来自全球各地的请求同时落在 API 上。当用户的请求是近似的时，选择一个不是 0 或 30 的分钟：
  ""每天早上大约 9 点"" → ""57 8 * * *"" 或 ""3 9 * * *""（不是 ""0 9 * * *""）
  ""每小时"" → ""7 * * * *""（不是 ""0 * * * *""）
  ""大约一小时后，提醒我..."" → 选择你落在的任何分钟，不要四舍五入

仅当用户命名那个确切时间并明确表示时（""在 9:00 整""、""在半点""、与会议协调），才使用分钟 0 或 30。如有疑问，提前或推迟几分钟 —— 用户不会注意到，而集群会。

{durabilitySection}

## 运行时行为

作业仅在 REPL 空闲时（不在查询中）触发。{durableRuntimeNote}调度器在你选择的任何时间上添加一个小的确定性抖动：重复任务在其周期最多 10% 后触发（最多 15 分钟）；落在 :00 或 :30 的一次性任务最多提前 90 秒触发。选择非整点分钟仍然是更大的杠杆。

重复任务在 {defaultMaxAgeDays} 天后自动过期 —— 它们最后一次触发，然后被删除。这限制了会话生命周期。在安排重复作业时告诉用户关于 {defaultMaxAgeDays} 天的限制。

返回一个你可以传递给 CronDelete 的作业 ID。";
    }
}

/// <summary>
/// CronDeleteTool 提示词
/// </summary>
[ToolPrompt(ToolName = "CronDelete", Category = ToolPromptCategory.System)]
public static class CronDeleteToolPrompt
{
    public const string ToolName = CronToolNameConstants.CronDelete;
    public const string Description = "通过 ID 取消计划的 cron 作业";

    /// <summary>
    /// 获取工具提示词
    /// </summary>
    public static string GetPrompt(bool durableEnabled)
    {
        return durableEnabled
            ? $"取消先前使用 CronCreate 安排的 cron 作业。从 {AppDataConstants.AppDataFolder}/{AppDataConstants.ScheduledTasksFileName}（持久化作业）或内存会话存储（仅会话作业）中删除它。"
            : "取消先前使用 CronCreate 安排的 cron 作业。从内存会话存储中删除它。";
    }
}

/// <summary>
/// CronListTool 提示词
/// </summary>
[ToolPrompt(ToolName = "CronList", Category = ToolPromptCategory.System)]
public static class CronListToolPrompt
{
    public const string ToolName = CronToolNameConstants.CronList;
    public const string Description = "列出计划的 cron 作业";

    /// <summary>
    /// 获取工具提示词
    /// </summary>
    public static string GetPrompt(bool durableEnabled)
    {
        return durableEnabled
            ? $"列出通过 CronCreate 安排的所有 cron 作业，包括持久化（{AppDataConstants.AppDataFolder}/{AppDataConstants.ScheduledTasksFileName}）和仅会话。"
            : "列出此会话中通过 CronCreate 安排的所有 cron 作业。";
    }
}
