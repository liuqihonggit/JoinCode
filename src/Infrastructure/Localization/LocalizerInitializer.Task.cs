namespace Infrastructure.Localization;

public static partial class LocalizerInitializer
{
    private static void RegisterTaskEntries(Dictionary<string, string> defaultEntries, Dictionary<string, string> zhEntries)
    {
        // === VerifyPlanExecutionToolHandlers ===
        defaultEntries[StringKey.VerifyPlanDefaultPrompt] = "Verify current plan execution status";
        defaultEntries[StringKey.VerifyPlanTitle] = "Plan Verification";
        defaultEntries[StringKey.VerifyPlanCriteria] = "Criteria: {0}";
        defaultEntries[StringKey.VerifyPlanSuccess] = "[OK] Plan executed successfully";
        defaultEntries[StringKey.VerifyPlanHasIssues] = "[WARNING] Plan execution has issues";
        defaultEntries[StringKey.VerifyPlanError] = "Error: {0}";
        defaultEntries[StringKey.VerifyPlanFailedLog] = "Plan verification failed";
        defaultEntries[StringKey.VerifyPlanFailed] = "Plan verification failed: {0}";

        zhEntries[StringKey.VerifyPlanDefaultPrompt] = "验证当前计划执行状态";
        zhEntries[StringKey.VerifyPlanTitle] = "计划验证";
        zhEntries[StringKey.VerifyPlanCriteria] = "验证标准: {0}";
        zhEntries[StringKey.VerifyPlanSuccess] = "[OK] 计划执行成功";
        zhEntries[StringKey.VerifyPlanHasIssues] = "[WARNING] 计划执行存在问题";
        zhEntries[StringKey.VerifyPlanError] = "错误: {0}";
        zhEntries[StringKey.VerifyPlanFailedLog] = "验证计划执行失败";
        zhEntries[StringKey.VerifyPlanFailed] = "验证计划执行失败: {0}";

        // === TaskOutputToolHandlers ===
        defaultEntries[StringKey.TaskIdCannotBeEmpty] = "task_id cannot be empty";
        defaultEntries[StringKey.TaskNotFound] = "Task not found: {0}";
        defaultEntries[StringKey.TaskOutputTitle] = "Task Output - {0}";
        defaultEntries[StringKey.TaskLabelTitle] = "Title: {0}";
        defaultEntries[StringKey.TaskLabelStatus] = "Status: {0}";
        defaultEntries[StringKey.TaskLabelOutput] = "[Output]";
        defaultEntries[StringKey.TaskOutputTruncated] = "\n... ({0} lines total, truncated to {1} lines)";
        defaultEntries[StringKey.TaskNoOutput] = "No output yet";
        defaultEntries[StringKey.TaskOutputFailedLog] = "Failed to get output for task {0}";
        defaultEntries[StringKey.TaskOutputFailed] = "Failed to get task output: {0}";

        zhEntries[StringKey.TaskIdCannotBeEmpty] = "task_id 不能为空";
        zhEntries[StringKey.TaskNotFound] = "未找到任务: {0}";
        zhEntries[StringKey.TaskOutputTitle] = "任务输出 - {0}";
        zhEntries[StringKey.TaskLabelTitle] = "标题: {0}";
        zhEntries[StringKey.TaskLabelStatus] = "状态: {0}";
        zhEntries[StringKey.TaskLabelOutput] = "[输出]";
        zhEntries[StringKey.TaskOutputTruncated] = "\n... (共 {0} 行，已截断至 {1} 行)";
        zhEntries[StringKey.TaskNoOutput] = "暂无输出";
        zhEntries[StringKey.TaskOutputFailedLog] = "获取任务 {0} 输出失败";
        zhEntries[StringKey.TaskOutputFailed] = "获取任务输出失败: {0}";

        // === SleepToolHandlers ===
        defaultEntries[StringKey.SleepDurationMustBePositive] = "duration_seconds must be greater than 0";
        defaultEntries[StringKey.SleepDurationTooLarge] = "duration_seconds cannot exceed {0} seconds (30 minutes)";
        defaultEntries[StringKey.SleepTickIntervalCannotBeNegative] = "tick_interval_seconds cannot be negative";
        defaultEntries[StringKey.SleepTickIntervalTooLarge] = "tick_interval_seconds cannot be greater than duration_seconds";
        defaultEntries[StringKey.SleepStartLog] = "Starting sleep for {0} seconds, reason: {1}, tick interval: {2}";
        defaultEntries[StringKey.SleepReasonUnspecified] = "unspecified";
        defaultEntries[StringKey.SleepTickIntervalSeconds] = "{0}s";
        defaultEntries[StringKey.SleepTickIntervalNone] = "none";
        defaultEntries[StringKey.SleepTickLog] = "Sleep tick #{0}, {1} seconds remaining";
        defaultEntries[StringKey.SleepCompleted] = "Sleep completed";
        defaultEntries[StringKey.SleepPlannedDuration] = "Planned: {0} seconds";
        defaultEntries[StringKey.SleepActualDuration] = "Actual: {0:F2} seconds";
        defaultEntries[StringKey.SleepTickInterval] = "Tick interval: {0} seconds";
        defaultEntries[StringKey.SleepTickCount] = "Tick count: {0}";
        defaultEntries[StringKey.SleepReason] = "Reason: {0}";
        defaultEntries[StringKey.SleepCancelledLog] = "Sleep cancelled";
        defaultEntries[StringKey.SleepFailedLog] = "Sleep failed";
        defaultEntries[StringKey.SleepFailed] = "Sleep failed: {0}";
        defaultEntries[StringKey.SleepTargetTimeCannotBeEmpty] = "target_time cannot be empty";
        defaultEntries[StringKey.SleepTimeParseFailed] = "Cannot parse time format: {0}\nSupported formats: HH:mm or yyyy-MM-dd HH:mm:ss";
        defaultEntries[StringKey.SleepTargetTimeExpired] = "Target time {0} has already expired";
        defaultEntries[StringKey.SleepWaitTooLong] = "Wait duration too long ({0:F1} minutes), maximum allowed {1} minutes\nPlease use cron tool to schedule tasks further in the future";
        defaultEntries[StringKey.SleepUntilStartLog] = "Waiting until {0} (approx. {1:F1} seconds)";
        defaultEntries[StringKey.SleepUntilReached] = "Reached target time: {0}";
        defaultEntries[StringKey.SleepUntilCancelledLog] = "Wait cancelled";

        zhEntries[StringKey.SleepDurationMustBePositive] = "duration_seconds 必须大于 0";
        zhEntries[StringKey.SleepDurationTooLarge] = "duration_seconds 不能超过 {0} 秒（30分钟）";
        zhEntries[StringKey.SleepTickIntervalCannotBeNegative] = "tick_interval_seconds 不能为负数";
        zhEntries[StringKey.SleepTickIntervalTooLarge] = "tick_interval_seconds 不能大于 duration_seconds";
        zhEntries[StringKey.SleepStartLog] = "开始休眠 {0} 秒，原因: {1}，唤醒间隔: {2}";
        zhEntries[StringKey.SleepReasonUnspecified] = "未指定";
        zhEntries[StringKey.SleepTickIntervalSeconds] = "{0}秒";
        zhEntries[StringKey.SleepTickIntervalNone] = "无";
        zhEntries[StringKey.SleepTickLog] = "休眠 tick #{0}，剩余 {1} 秒";
        zhEntries[StringKey.SleepCompleted] = "休眠完成";
        zhEntries[StringKey.SleepPlannedDuration] = "计划时间: {0} 秒";
        zhEntries[StringKey.SleepActualDuration] = "实际时间: {0:F2} 秒";
        zhEntries[StringKey.SleepTickInterval] = "唤醒间隔: {0} 秒";
        zhEntries[StringKey.SleepTickCount] = "唤醒次数: {0}";
        zhEntries[StringKey.SleepReason] = "原因: {0}";
        zhEntries[StringKey.SleepCancelledLog] = "休眠被取消";
        zhEntries[StringKey.SleepFailedLog] = "休眠失败";
        zhEntries[StringKey.SleepFailed] = "休眠失败: {0}";
        zhEntries[StringKey.SleepTargetTimeCannotBeEmpty] = "target_time 不能为空";
        zhEntries[StringKey.SleepTimeParseFailed] = "无法解析时间格式: {0}\n支持的格式: HH:mm 或 yyyy-MM-dd HH:mm:ss";
        zhEntries[StringKey.SleepTargetTimeExpired] = "目标时间 {0} 已经过期";
        zhEntries[StringKey.SleepWaitTooLong] = "等待时间过长 ({0:F1} 分钟)，最大允许 {1} 分钟\n请使用 cron 工具安排长时间后的任务";
        zhEntries[StringKey.SleepUntilStartLog] = "等待直到 {0}（约 {1:F1} 秒）";
        zhEntries[StringKey.SleepUntilReached] = "已等到 {0}";
        zhEntries[StringKey.SleepUntilCancelledLog] = "等待被取消";
    }
}
