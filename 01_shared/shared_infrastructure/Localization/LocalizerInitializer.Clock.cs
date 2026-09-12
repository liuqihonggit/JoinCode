namespace Infrastructure.Localization;

public static partial class LocalizerInitializer
{
    private static void RegisterClockEntries(Dictionary<string, string> defaultEntries, Dictionary<string, string> zhEntries)
    {
        // === GoalEngine ===
        defaultEntries[StringKey.GoalEngineAlreadyRunning] = "There is already a goal running, please pause or clear it first";
        defaultEntries[StringKey.GoalEngineBudgetUnlimited] = "Unlimited";
        defaultEntries[StringKey.GoalEngineUserResumeReason] = "User resumed goal";
        defaultEntries[StringKey.GoalEngineStarting] = "Goal engine started: {0} - {1} (Budget: {2})";
        defaultEntries[StringKey.GoalEnginePaused] = "Goal engine paused: {0}";
        defaultEntries[StringKey.GoalEngineResumed] = "Goal engine resumed: {0}";
        defaultEntries[StringKey.GoalEngineCleared] = "Goal engine cleared: {0}";
        defaultEntries[StringKey.GoalEngineCompletedByModel] = "Goal completed (marked by model): {0}, Reason: {1}";
        defaultEntries[StringKey.GoalEngineUnmetByModel] = "Goal unmet (marked by model): {0}, Reason: {1}";
        defaultEntries[StringKey.GoalEngineBudgetExhausted] = "Goal budget exhausted: {0}, Used: {1}/{2}";
        defaultEntries[StringKey.GoalEngineCompleted] = "Goal completed: {0}, Turns: {1}, Tokens: {2}";
        defaultEntries[StringKey.GoalEngineContinuing] = "Goal continuing: {0}, Evaluator feedback: {1}";
        defaultEntries[StringKey.GoalEngineHeartbeatTriggered] = "Heartbeat triggered, Goal: {0}, Turns: {1}";

        zhEntries[StringKey.GoalEngineAlreadyRunning] = "已有目标正在运行，请先暂停或清除";
        zhEntries[StringKey.GoalEngineBudgetUnlimited] = "无限制";
        zhEntries[StringKey.GoalEngineUserResumeReason] = "用户恢复目标";
        zhEntries[StringKey.GoalEngineStarting] = "目标引擎启动: {0} - {1} (预算: {2})";
        zhEntries[StringKey.GoalEnginePaused] = "目标引擎已暂停: {0}";
        zhEntries[StringKey.GoalEngineResumed] = "目标引擎已恢复: {0}";
        zhEntries[StringKey.GoalEngineCleared] = "目标引擎已清除: {0}";
        zhEntries[StringKey.GoalEngineCompletedByModel] = "目标已完成（模型标记）: {0}, 理由: {1}";
        zhEntries[StringKey.GoalEngineUnmetByModel] = "目标无法完成（模型标记）: {0}, 理由: {1}";
        zhEntries[StringKey.GoalEngineBudgetExhausted] = "目标预算耗尽: {0}, 已用: {1}/{2}";
        zhEntries[StringKey.GoalEngineCompleted] = "目标完成: {0}, 轮数: {1}, Token: {2}";
        zhEntries[StringKey.GoalEngineContinuing] = "目标继续: {0}, 评估器反馈: {1}";
        zhEntries[StringKey.GoalEngineHeartbeatTriggered] = "心跳触发, 目标: {0}, 轮数: {1}";

        // === GoalEvaluator ===
        defaultEntries[StringKey.GoalEvaluatorCallFailed] = "Evaluator call failed, treated as not completed";
        defaultEntries[StringKey.GoalEvaluatorEmptyResult] = "Evaluator returned empty result";
        defaultEntries[StringKey.GoalEvaluatorFormatError] = "Evaluator returned malformed format: {0}";
        defaultEntries[StringKey.GoalEvaluatorNoConstraints] = "None";

        zhEntries[StringKey.GoalEvaluatorCallFailed] = "评估器不可用";
        zhEntries[StringKey.GoalEvaluatorEmptyResult] = "评估器返回空结果";
        zhEntries[StringKey.GoalEvaluatorFormatError] = "评估器返回格式异常: {0}";
        zhEntries[StringKey.GoalEvaluatorNoConstraints] = "无";

        // === ServiceHost ===
        defaultEntries[StringKey.ServiceHostAlreadyRegistered] = "Service '{0}' is already registered";
        defaultEntries[StringKey.ServiceHostAlreadyRunning] = "Service host is already running";
        defaultEntries[StringKey.ServiceHostStarting] = "Starting service host...";
        defaultEntries[StringKey.ServiceHostStartFailed] = "Failed to start service {0}";
        defaultEntries[StringKey.ServiceHostStarted] = "Service host started, {0} services total";
        defaultEntries[StringKey.ServiceHostNotRunning] = "Service host is not running";
        defaultEntries[StringKey.ServiceHostStopping] = "Stopping service host...";
        defaultEntries[StringKey.ServiceHostStopError] = "Error stopping service {0}";
        defaultEntries[StringKey.ServiceHostStopped] = "Service host stopped";
        defaultEntries[StringKey.ServiceHostNotFound] = "Service {0} not found";
        defaultEntries[StringKey.ServiceHostStartingService] = "Starting service: {0}";
        defaultEntries[StringKey.ServiceHostServiceStarted] = "Service started: {0}";
        defaultEntries[StringKey.ServiceHostStoppingService] = "Stopping service: {0}";
        defaultEntries[StringKey.ServiceHostServiceStopped] = "Service stopped: {0}";
        defaultEntries[StringKey.ServiceHostStopFailed] = "Failed to stop service {0}";

        zhEntries[StringKey.ServiceHostAlreadyRegistered] = "服务 '{0}' 已注册";
        zhEntries[StringKey.ServiceHostAlreadyRunning] = "服务主机已经在运行";
        zhEntries[StringKey.ServiceHostStarting] = "正在启动服务主机...";
        zhEntries[StringKey.ServiceHostStartFailed] = "启动服务 {0} 失败";
        zhEntries[StringKey.ServiceHostStarted] = "服务主机启动完成，共 {0} 个服务";
        zhEntries[StringKey.ServiceHostNotRunning] = "服务主机未在运行";
        zhEntries[StringKey.ServiceHostStopping] = "正在停止服务主机...";
        zhEntries[StringKey.ServiceHostStopError] = "停止服务 {0} 时发生错误";
        zhEntries[StringKey.ServiceHostStopped] = "服务主机已停止";
        zhEntries[StringKey.ServiceHostNotFound] = "服务 {0} 未找到";
        zhEntries[StringKey.ServiceHostStartingService] = "正在启动服务: {0}";
        zhEntries[StringKey.ServiceHostServiceStarted] = "服务已启动: {0}";
        zhEntries[StringKey.ServiceHostStoppingService] = "正在停止服务: {0}";
        zhEntries[StringKey.ServiceHostServiceStopped] = "服务已停止: {0}";
        zhEntries[StringKey.ServiceHostStopFailed] = "停止服务 {0} 失败";

        // === WorkflowApplication ===
        defaultEntries[StringKey.WorkflowAppInitializing] = "Initializing Workflow application...";
        defaultEntries[StringKey.WorkflowAppCronRegistered] = "CronScheduler service registered";
        defaultEntries[StringKey.WorkflowAppInitialized] = "Workflow application initialized, {0} services registered";
        defaultEntries[StringKey.WorkflowAppStarting] = "Starting Workflow application...";
        defaultEntries[StringKey.WorkflowAppStarted] = "Workflow application started";
        defaultEntries[StringKey.WorkflowAppStopping] = "Stopping Workflow application...";
        defaultEntries[StringKey.WorkflowAppStopped] = "Workflow application stopped";
        defaultEntries[StringKey.WorkflowAppStatusChanged] = "Service {0} status changed: {1} -> {2}";

        zhEntries[StringKey.WorkflowAppInitializing] = "正在初始化 Workflow 应用程序...";
        zhEntries[StringKey.WorkflowAppCronRegistered] = "已注册 CronScheduler 服务";
        zhEntries[StringKey.WorkflowAppInitialized] = "Workflow 应用程序初始化完成，共注册 {0} 个服务";
        zhEntries[StringKey.WorkflowAppStarting] = "正在启动 Workflow 应用程序...";
        zhEntries[StringKey.WorkflowAppStarted] = "Workflow 应用程序已启动";
        zhEntries[StringKey.WorkflowAppStopping] = "正在停止 Workflow 应用程序...";
        zhEntries[StringKey.WorkflowAppStopped] = "Workflow 应用程序已停止";
        zhEntries[StringKey.WorkflowAppStatusChanged] = "服务 {0} 状态变更: {1} -> {2}";

        // === CronSchedulerService ===
        defaultEntries[StringKey.CronSchedulerAlreadyRunning] = "CronScheduler service is already running";
        defaultEntries[StringKey.CronSchedulerStarting] = "Starting CronScheduler service...";
        defaultEntries[StringKey.CronSchedulerTaskFired] = "[Cron] Task fired: {0} - {1}";
        defaultEntries[StringKey.CronSchedulerTaskNotificationTitle] = "Scheduled task fired";
        defaultEntries[StringKey.CronSchedulerStarted] = "CronScheduler service started";
        defaultEntries[StringKey.CronSchedulerStopping] = "Stopping CronScheduler service...";
        defaultEntries[StringKey.CronSchedulerStopped] = "CronScheduler service stopped";
        defaultEntries[StringKey.CronSchedulerDisposeError] = "CronSchedulerService.DisposeAsync timed out or failed";

        zhEntries[StringKey.CronSchedulerAlreadyRunning] = "CronScheduler 服务已经在运行";
        zhEntries[StringKey.CronSchedulerStarting] = "正在启动 CronScheduler 服务...";
        zhEntries[StringKey.CronSchedulerTaskFired] = "[Cron] 任务触发: {0} - {1}";
        zhEntries[StringKey.CronSchedulerTaskNotificationTitle] = "定时任务触发";
        zhEntries[StringKey.CronSchedulerStarted] = "CronScheduler 服务已启动";
        zhEntries[StringKey.CronSchedulerStopping] = "正在停止 CronScheduler 服务...";
        zhEntries[StringKey.CronSchedulerStopped] = "CronScheduler 服务已停止";
        zhEntries[StringKey.CronSchedulerDisposeError] = "CronSchedulerService.DisposeAsync 超时或失败";

        // === Permission Mode ===
        defaultEntries[StringKey.PermissionModeSwitched] = "Permission mode switched: {0} -> Auto";
        defaultEntries[StringKey.PermissionModeSwitchFailed] = "Failed to switch permission mode, continuing with current mode";
        defaultEntries[StringKey.PermissionModeRestored] = "Permission mode restored: -> {0}";
        defaultEntries[StringKey.PermissionModeRestoreFailed] = "Failed to restore permission mode";

        zhEntries[StringKey.PermissionModeSwitched] = "权限模式已切换: {0} → Auto";
        zhEntries[StringKey.PermissionModeSwitchFailed] = "切换权限模式失败，继续使用当前模式";
        zhEntries[StringKey.PermissionModeRestored] = "权限模式已恢复: → {0}";
        zhEntries[StringKey.PermissionModeRestoreFailed] = "恢复权限模式失败";

        // === GoalHeartbeat ===
        defaultEntries[StringKey.GoalHeartbeatActivityStarted] = "Heartbeat activity started: {0}, RefCount: {1}";
        defaultEntries[StringKey.GoalHeartbeatActivityStopped] = "Heartbeat activity stopped: {0}, RefCount: {1}";
        defaultEntries[StringKey.GoalHeartbeatReset] = "Heartbeat reset";
        defaultEntries[StringKey.GoalHeartbeatCallbackFailed] = "Heartbeat callback execution failed";

        zhEntries[StringKey.GoalHeartbeatActivityStarted] = "心跳活动启动: {0}, 引用计数: {1}";
        zhEntries[StringKey.GoalHeartbeatActivityStopped] = "心跳活动停止: {0}, 引用计数: {1}";
        zhEntries[StringKey.GoalHeartbeatReset] = "心跳已重置";
        zhEntries[StringKey.GoalHeartbeatCallbackFailed] = "心跳回调执行失败";
    }
}