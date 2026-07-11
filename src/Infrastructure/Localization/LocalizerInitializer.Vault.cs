namespace Infrastructure.Localization;

public static partial class LocalizerInitializer
{
    private static void RegisterVaultEntries(Dictionary<string, string> defaultEntries, Dictionary<string, string> zhEntries)
    {
        // === MemoryManagementToolHandlers ===
        defaultEntries[StringKey.VaultQueryCannotBeEmpty] = "query cannot be empty";
        defaultEntries[StringKey.VaultMemoryScanResult] = "Memory scan result";
        defaultEntries[StringKey.VaultLabelQuery] = "Query: {0}";
        defaultEntries[StringKey.VaultFoundRelevantMemories] = "Found {0} relevant memories (scanned {1} total)";
        defaultEntries[StringKey.VaultNoRelevantMemories] = "No relevant memories found";
        defaultEntries[StringKey.VaultLabelScore] = "{0}. [{1}] Score: {2:F2}";
        defaultEntries[StringKey.VaultLabelContent] = "   Content: {0}...";
        defaultEntries[StringKey.VaultLabelMatch] = "   Match: {0}";
        defaultEntries[StringKey.VaultLabelTypeAccess] = "   Type: {0} | Access: {1} times";
        defaultEntries[StringKey.VaultMemoryAgeInfo] = "Memory age info";
        defaultEntries[StringKey.VaultTotalMemories] = "Total {0} memories";
        defaultEntries[StringKey.VaultNoMemories] = "No memories";
        defaultEntries[StringKey.VaultLabelAge] = "   Age: {0:F0} days";
        defaultEntries[StringKey.VaultLabelUnaccessed] = "   Unaccessed: {0:F0} days";
        defaultEntries[StringKey.VaultLabelAccessCount] = "   Access count: {0}";
        defaultEntries[StringKey.VaultLabelHealthScore] = "   Health score: {0:F1}/100";
        defaultEntries[StringKey.VaultSuggestDelete] = "   {0} Suggest delete";
        defaultEntries[StringKey.VaultSuggestArchive] = "   {0} Suggest archive";
        defaultEntries[StringKey.VaultCleanupConfirmRequired] = "Please enter 'yes' to confirm memory cleanup\nWarning: This will archive or delete old memories, proceed with caution";
        defaultEntries[StringKey.VaultMemoryCleanupComplete] = "Memory cleanup complete";
        defaultEntries[StringKey.VaultCheckedMemories] = "Checked memories: {0}";
        defaultEntries[StringKey.VaultArchivedMemories] = "Archived memories: {0}";
        defaultEntries[StringKey.VaultDeletedMemories] = "Deleted memories: {0}";
        defaultEntries[StringKey.VaultRetainedMemories] = "Retained memories: {0}";
        defaultEntries[StringKey.VaultProcessedMemoryIds] = "Processed memory IDs:";
        defaultEntries[StringKey.VaultMoreItems] = "  ... {0} more";
        defaultEntries[StringKey.VaultMemoryHealthReport] = "Memory health report";
        defaultEntries[StringKey.VaultTotalMemoryCount] = "Total memories: {0}";
        defaultEntries[StringKey.VaultHealthyMemories] = "Healthy memories: {0} ({1}%)";
        defaultEntries[StringKey.VaultNeedsAttention] = "Needs attention: {0} ({1}%)";
        defaultEntries[StringKey.VaultSuggestArchiveCount] = "Suggest archive: {0}";
        defaultEntries[StringKey.VaultSuggestDeleteCount] = "Suggest delete: {0}";
        defaultEntries[StringKey.VaultAvgHealthScore] = "Average health score: {0:F1}/100";
        defaultEntries[StringKey.VaultAgeDistribution] = "Age distribution:";
        defaultEntries[StringKey.VaultSuggestions] = "Suggestions:";
        defaultEntries[StringKey.VaultSuggestDeleteUseCleanup] = "  - {0} memories suggested for deletion, use memory_cleanup to clean";
        defaultEntries[StringKey.VaultSuggestArchiveCount2] = "  - {0} memories suggested for archival";
        defaultEntries[StringKey.VaultTeamIdCannotBeEmpty] = "team_id cannot be empty";
        defaultEntries[StringKey.VaultPathCannotBeEmpty] = "path cannot be empty";
        defaultEntries[StringKey.VaultTeamMemoryPathAdded] = "Team memory path added";
        defaultEntries[StringKey.VaultLabelTeam] = "Team: {0}";
        defaultEntries[StringKey.VaultLabelPath] = "Path: {0}";
        defaultEntries[StringKey.VaultLabelShared] = "Shared: {0}";
        defaultEntries[StringKey.VaultYes] = "Yes";
        defaultEntries[StringKey.VaultNo] = "No";
        defaultEntries[StringKey.VaultLabelAllowedAgents] = "Allowed agents: {0}";
        defaultEntries[StringKey.VaultTeamMemoryPaths] = "Team memory paths";
        defaultEntries[StringKey.VaultPathCount] = "Total {0} paths";
        defaultEntries[StringKey.VaultNoTeamMemoryPaths] = "No team memory paths";
        defaultEntries[StringKey.VaultTeamNotFound] = "Specified team memory path not found";
        defaultEntries[StringKey.VaultTeamPathRemoved] = "{0} Removed team '{1}' memory path: {2}";
        defaultEntries[StringKey.VaultTeamSharedMemoryScan] = "{0} Team '{1}' shared memory scan";
        defaultEntries[StringKey.VaultLabelSource] = "   Source: {0}";

        zhEntries[StringKey.VaultQueryCannotBeEmpty] = "query 不能为空";
        zhEntries[StringKey.VaultMemoryScanResult] = "内存扫描结果";
        zhEntries[StringKey.VaultLabelQuery] = "查询: {0}";
        zhEntries[StringKey.VaultFoundRelevantMemories] = "找到 {0} 条相关记忆 (共扫描 {1} 条)";
        zhEntries[StringKey.VaultNoRelevantMemories] = "未找到相关记忆";
        zhEntries[StringKey.VaultLabelScore] = "{0}. [{1}] 分数: {2:F2}";
        zhEntries[StringKey.VaultLabelContent] = "   内容: {0}...";
        zhEntries[StringKey.VaultLabelMatch] = "   匹配: {0}";
        zhEntries[StringKey.VaultLabelTypeAccess] = "   类型: {0} | 访问: {1} 次";
        zhEntries[StringKey.VaultMemoryAgeInfo] = "内存年龄信息";
        zhEntries[StringKey.VaultTotalMemories] = "共 {0} 条记忆";
        zhEntries[StringKey.VaultNoMemories] = "暂无记忆";
        zhEntries[StringKey.VaultLabelAge] = "   年龄: {0:F0} 天";
        zhEntries[StringKey.VaultLabelUnaccessed] = "   未访问: {0:F0} 天";
        zhEntries[StringKey.VaultLabelAccessCount] = "   访问次数: {0}";
        zhEntries[StringKey.VaultLabelHealthScore] = "   健康分数: {0:F1}/100";
        zhEntries[StringKey.VaultSuggestDelete] = "   {0} 建议删除";
        zhEntries[StringKey.VaultSuggestArchive] = "   {0} 建议归档";
        zhEntries[StringKey.VaultCleanupConfirmRequired] = "请输入 'yes' 确认执行内存清理\n警告: 此操作将归档或删除旧记忆，请谨慎操作";
        zhEntries[StringKey.VaultMemoryCleanupComplete] = "内存清理完成";
        zhEntries[StringKey.VaultCheckedMemories] = "检查记忆: {0}";
        zhEntries[StringKey.VaultArchivedMemories] = "归档记忆: {0}";
        zhEntries[StringKey.VaultDeletedMemories] = "删除记忆: {0}";
        zhEntries[StringKey.VaultRetainedMemories] = "保留记忆: {0}";
        zhEntries[StringKey.VaultProcessedMemoryIds] = "处理的记忆ID:";
        zhEntries[StringKey.VaultMoreItems] = "  ... 还有 {0} 个";
        zhEntries[StringKey.VaultMemoryHealthReport] = "内存健康报告";
        zhEntries[StringKey.VaultTotalMemoryCount] = "总记忆数: {0}";
        zhEntries[StringKey.VaultHealthyMemories] = "健康记忆: {0} ({1}%)";
        zhEntries[StringKey.VaultNeedsAttention] = "需要关注: {0} ({1}%)";
        zhEntries[StringKey.VaultSuggestArchiveCount] = "建议归档: {0}";
        zhEntries[StringKey.VaultSuggestDeleteCount] = "建议删除: {0}";
        zhEntries[StringKey.VaultAvgHealthScore] = "平均健康分数: {0:F1}/100";
        zhEntries[StringKey.VaultAgeDistribution] = "年龄分布:";
        zhEntries[StringKey.VaultSuggestions] = "建议:";
        zhEntries[StringKey.VaultSuggestDeleteUseCleanup] = "  - 有 {0} 条记忆建议删除，使用 memory_cleanup 清理";
        zhEntries[StringKey.VaultSuggestArchiveCount2] = "  - 有 {0} 条记忆建议归档";
        zhEntries[StringKey.VaultTeamIdCannotBeEmpty] = "team_id 不能为空";
        zhEntries[StringKey.VaultPathCannotBeEmpty] = "path 不能为空";
        zhEntries[StringKey.VaultTeamMemoryPathAdded] = "团队内存路径已添加";
        zhEntries[StringKey.VaultLabelTeam] = "团队: {0}";
        zhEntries[StringKey.VaultLabelPath] = "路径: {0}";
        zhEntries[StringKey.VaultLabelShared] = "共享: {0}";
        zhEntries[StringKey.VaultYes] = "是";
        zhEntries[StringKey.VaultNo] = "否";
        zhEntries[StringKey.VaultLabelAllowedAgents] = "允许代理: {0}";
        zhEntries[StringKey.VaultTeamMemoryPaths] = "团队内存路径";
        zhEntries[StringKey.VaultPathCount] = "共 {0} 条路径";
        zhEntries[StringKey.VaultNoTeamMemoryPaths] = "暂无团队内存路径";
        zhEntries[StringKey.VaultTeamNotFound] = "未找到指定的团队内存路径";
        zhEntries[StringKey.VaultTeamPathRemoved] = "{0} 已移除团队 '{1}' 的内存路径: {2}";
        zhEntries[StringKey.VaultTeamSharedMemoryScan] = "{0} 团队 '{1}' 共享记忆扫描";
        zhEntries[StringKey.VaultLabelSource] = "   来源: {0}";

        // === MemoryExtensionToolHandlers ===
        defaultEntries[StringKey.VaultContentCannotBeEmpty] = "content cannot be empty";
        defaultEntries[StringKey.VaultLogEntryAppended] = "Log entry appended";
        defaultEntries[StringKey.VaultLabelCategory] = "Category: {0}";
        defaultEntries[StringKey.VaultLabelRelatedMemory] = "Related memory: {0}";
        defaultEntries[StringKey.VaultLabelTime] = "Time: {0}";
        defaultEntries[StringKey.VaultNoDailyLogToday] = "{0} No daily log today";
        defaultEntries[StringKey.VaultDailyLogToday] = "{0} Today's daily log";
        defaultEntries[StringKey.VaultSearchHistoryResult] = "Past conversation memory search result";
        defaultEntries[StringKey.VaultNoPastConversationMemories] = "No past conversation memories found";
        defaultEntries[StringKey.VaultToday] = "Today";
        defaultEntries[StringKey.VaultYesterday] = "Yesterday";
        defaultEntries[StringKey.VaultDaysAgo] = "{0} days ago";
        defaultEntries[StringKey.VaultWeeksAgo] = "{0} weeks ago";
        defaultEntries[StringKey.VaultMonthsAgo] = "{0} months ago";
        defaultEntries[StringKey.VaultNoTitle] = "No title";
        defaultEntries[StringKey.VaultLabelIdAccess] = "   ID: {0} | Access: {1} times";
        defaultEntries[StringKey.VaultLabelTags] = "   Tags: {0}";
        defaultEntries[StringKey.VaultTeamSyncServiceNotRegistered] = "Team memory sync service not registered, cannot sync";
        defaultEntries[StringKey.VaultTeamSyncComplete] = "{0} Team memory sync complete";
        defaultEntries[StringKey.VaultLabelSyncTime] = "Sync time: {0}";
        defaultEntries[StringKey.VaultLabelSyncedCount] = "Synced memory count: {0}";
        defaultEntries[StringKey.VaultLabelWatching] = "Watching: {0}";
        defaultEntries[StringKey.VaultConflictCount] = "{0} {1} conflicts found:";
        defaultEntries[StringKey.VaultConflictContentMismatch] = "Content mismatch";
        defaultEntries[StringKey.VaultConflictDeletedLocally] = "Deleted locally";
        defaultEntries[StringKey.VaultConflictDeletedRemotely] = "Deleted remotely";
        defaultEntries[StringKey.VaultConflictMemoryId] = "  - [{0}] Memory ID: {1}";
        defaultEntries[StringKey.VaultMoreConflicts] = "  ... {0} more conflicts";
        defaultEntries[StringKey.VaultTeamSyncServiceNotRegisteredStatus] = "Team memory sync service not registered, cannot get status";
        defaultEntries[StringKey.VaultTeamNeverSynced] = "Team '{0}' has never been synced";
        defaultEntries[StringKey.VaultTeamSyncStatus] = "{0} Team sync status";
        defaultEntries[StringKey.VaultLabelLastSync] = "Last sync: {0}";
        defaultEntries[StringKey.VaultNeverSynced] = "Never synced";
        defaultEntries[StringKey.VaultLabelSyncedMemories] = "Synced memories: {0}";
        defaultEntries[StringKey.VaultLabelHasConflicts] = "Has conflicts: {0}";

        zhEntries[StringKey.VaultContentCannotBeEmpty] = "content 不能为空";
        zhEntries[StringKey.VaultLogEntryAppended] = "日志条目已追加";
        zhEntries[StringKey.VaultLabelCategory] = "分类: {0}";
        zhEntries[StringKey.VaultLabelRelatedMemory] = "关联记忆: {0}";
        zhEntries[StringKey.VaultLabelTime] = "时间: {0}";
        zhEntries[StringKey.VaultNoDailyLogToday] = "{0} 今日暂无助手日志";
        zhEntries[StringKey.VaultDailyLogToday] = "{0} 今日助手日志";
        zhEntries[StringKey.VaultSearchHistoryResult] = "过往对话记忆搜索结果";
        zhEntries[StringKey.VaultNoPastConversationMemories] = "未找到相关过往对话记忆";
        zhEntries[StringKey.VaultToday] = "今天";
        zhEntries[StringKey.VaultYesterday] = "昨天";
        zhEntries[StringKey.VaultDaysAgo] = "{0} 天前";
        zhEntries[StringKey.VaultWeeksAgo] = "{0} 周前";
        zhEntries[StringKey.VaultMonthsAgo] = "{0} 个月前";
        zhEntries[StringKey.VaultNoTitle] = "无标题";
        zhEntries[StringKey.VaultLabelIdAccess] = "   ID: {0} | 访问: {1} 次";
        zhEntries[StringKey.VaultLabelTags] = "   标签: {0}";
        zhEntries[StringKey.VaultTeamSyncServiceNotRegistered] = "团队记忆同步服务未注册，无法执行同步";
        zhEntries[StringKey.VaultTeamSyncComplete] = "{0} 团队记忆同步完成";
        zhEntries[StringKey.VaultLabelSyncTime] = "同步时间: {0}";
        zhEntries[StringKey.VaultLabelSyncedCount] = "同步记忆数: {0}";
        zhEntries[StringKey.VaultLabelWatching] = "正在监视: {0}";
        zhEntries[StringKey.VaultConflictCount] = "{0} 存在 {1} 个冲突:";
        zhEntries[StringKey.VaultConflictContentMismatch] = "内容不一致";
        zhEntries[StringKey.VaultConflictDeletedLocally] = "本地已删除";
        zhEntries[StringKey.VaultConflictDeletedRemotely] = "远程已删除";
        zhEntries[StringKey.VaultConflictMemoryId] = "  - [{0}] 记忆ID: {1}";
        zhEntries[StringKey.VaultMoreConflicts] = "  ... 还有 {0} 个冲突";
        zhEntries[StringKey.VaultTeamSyncServiceNotRegisteredStatus] = "团队记忆同步服务未注册，无法获取状态";
        zhEntries[StringKey.VaultTeamNeverSynced] = "团队 '{0}' 尚未进行过同步";
        zhEntries[StringKey.VaultTeamSyncStatus] = "{0} 团队同步状态";
        zhEntries[StringKey.VaultLabelLastSync] = "最后同步: {0}";
        zhEntries[StringKey.VaultNeverSynced] = "从未同步";
        zhEntries[StringKey.VaultLabelSyncedMemories] = "已同步记忆: {0}";
        zhEntries[StringKey.VaultLabelHasConflicts] = "存在冲突: {0}";

        // === TaskToolHandlers ===
        defaultEntries[StringKey.VaultTitleCannotBeEmpty] = "title cannot be empty";
        defaultEntries[StringKey.VaultTaskIdCannotBeEmpty] = "task_id cannot be empty";
        defaultEntries[StringKey.VaultDependsOnTaskIdCannotBeEmpty] = "depends_on_task_id cannot be empty";
        defaultEntries[StringKey.VaultCreateTaskFailed] = "Failed to create task";
        defaultEntries[StringKey.VaultTaskCreated] = "Task created";
        defaultEntries[StringKey.VaultListTaskFailed] = "Failed to list tasks";
        defaultEntries[StringKey.VaultTaskList] = "Task list ({0} total)";
        defaultEntries[StringKey.VaultDisplayRange] = "Display: {0} - {1}";
        defaultEntries[StringKey.VaultUpdateTaskFailed] = "Failed to update task";
        defaultEntries[StringKey.VaultTaskUpdated] = "Task updated";
        defaultEntries[StringKey.VaultStopTaskFailed] = "Failed to stop task";
        defaultEntries[StringKey.VaultTaskStopped] = "Task stopped: {0}";
        defaultEntries[StringKey.VaultLabelReason] = "Reason: {0}";
        defaultEntries[StringKey.VaultTaskNotFound] = "Task not found: {0}";
        defaultEntries[StringKey.VaultTaskDetails] = "Task details";
        defaultEntries[StringKey.VaultSetDependencyFailed] = "Failed to set task dependency";
        defaultEntries[StringKey.VaultTaskDependencySet] = "Task dependency set";
        defaultEntries[StringKey.VaultLabelTask] = "Task: {0}";
        defaultEntries[StringKey.VaultLabelDependsOn] = "Depends on: {0}";
        defaultEntries[StringKey.VaultLabelDependencyType] = "Type: {0}";
        defaultEntries[StringKey.VaultRemoveDependencyFailed] = "Failed to remove task dependency";
        defaultEntries[StringKey.VaultTaskDependencyRemoved] = "Task dependency removed";
        defaultEntries[StringKey.VaultTaskDependencyList] = "Task dependency list: {0}";
        defaultEntries[StringKey.VaultNoDependencies] = "No dependencies for this task";
        defaultEntries[StringKey.VaultTaskExecutionCheck] = "Task execution check: {0}";
        defaultEntries[StringKey.VaultTaskCanExecute] = "{0} This task can execute";
        defaultEntries[StringKey.VaultAllDependenciesSatisfied] = "All dependencies satisfied, task status allows execution";
        defaultEntries[StringKey.VaultTaskCannotExecute] = "{0} This task cannot execute temporarily";
        defaultEntries[StringKey.VaultPossibleReasons] = "Possible reasons:";
        defaultEntries[StringKey.VaultReasonTaskNotExist] = "  - Task does not exist";
        defaultEntries[StringKey.VaultReasonInvalidStatus] = "  - Task status is not pending or waiting_for_dependencies";
        defaultEntries[StringKey.VaultReasonBlockingDependency] = "  - Unresolved blocking dependencies exist";
        defaultEntries[StringKey.VaultLabelTitle] = "Title: {0}";
        defaultEntries[StringKey.VaultLabelDescription] = "Description: {0}";
        defaultEntries[StringKey.VaultLabelStatus] = "Status: {0}";
        defaultEntries[StringKey.VaultLabelPriority] = "Priority: {0}";
        defaultEntries[StringKey.VaultLabelAssignee] = "Assignee: {0}";
        defaultEntries[StringKey.VaultLabelDueDate] = "Due date: {0}";
        defaultEntries[StringKey.VaultLabelCreatedAt] = "Created at: {0}";
        defaultEntries[StringKey.VaultSummaryAssignee] = " (Assignee: {0})";
        defaultEntries[StringKey.VaultSummaryDueDate] = " - Due: {0}";

        zhEntries[StringKey.VaultTitleCannotBeEmpty] = "title 不能为空";
        zhEntries[StringKey.VaultTaskIdCannotBeEmpty] = "task_id 不能为空";
        zhEntries[StringKey.VaultDependsOnTaskIdCannotBeEmpty] = "depends_on_task_id 不能为空";
        zhEntries[StringKey.VaultCreateTaskFailed] = "创建任务失败";
        zhEntries[StringKey.VaultTaskCreated] = "任务已创建";
        zhEntries[StringKey.VaultListTaskFailed] = "列出任务失败";
        zhEntries[StringKey.VaultTaskList] = "任务列表 (共 {0} 个)";
        zhEntries[StringKey.VaultDisplayRange] = "显示: {0} - {1}";
        zhEntries[StringKey.VaultUpdateTaskFailed] = "更新任务失败";
        zhEntries[StringKey.VaultTaskUpdated] = "任务已更新";
        zhEntries[StringKey.VaultStopTaskFailed] = "停止任务失败";
        zhEntries[StringKey.VaultTaskStopped] = "任务已停止: {0}";
        zhEntries[StringKey.VaultLabelReason] = "原因: {0}";
        zhEntries[StringKey.VaultTaskNotFound] = "未找到任务: {0}";
        zhEntries[StringKey.VaultTaskDetails] = "任务详情";
        zhEntries[StringKey.VaultSetDependencyFailed] = "设置任务依赖失败";
        zhEntries[StringKey.VaultTaskDependencySet] = "任务依赖已设置";
        zhEntries[StringKey.VaultLabelTask] = "任务: {0}";
        zhEntries[StringKey.VaultLabelDependsOn] = "依赖: {0}";
        zhEntries[StringKey.VaultLabelDependencyType] = "类型: {0}";
        zhEntries[StringKey.VaultRemoveDependencyFailed] = "移除任务依赖失败";
        zhEntries[StringKey.VaultTaskDependencyRemoved] = "任务依赖已移除";
        zhEntries[StringKey.VaultTaskDependencyList] = "任务依赖列表: {0}";
        zhEntries[StringKey.VaultNoDependencies] = "该任务没有依赖关系";
        zhEntries[StringKey.VaultTaskExecutionCheck] = "任务执行检查: {0}";
        zhEntries[StringKey.VaultTaskCanExecute] = "{0} 该任务可以执行";
        zhEntries[StringKey.VaultAllDependenciesSatisfied] = "所有依赖已满足，任务状态允许执行";
        zhEntries[StringKey.VaultTaskCannotExecute] = "{0} 该任务暂时不能执行";
        zhEntries[StringKey.VaultPossibleReasons] = "可能原因:";
        zhEntries[StringKey.VaultReasonTaskNotExist] = "  - 任务不存在";
        zhEntries[StringKey.VaultReasonInvalidStatus] = "  - 任务状态不是 pending 或 waiting_for_dependencies";
        zhEntries[StringKey.VaultReasonBlockingDependency] = "  - 存在未完成的阻塞依赖";
        zhEntries[StringKey.VaultLabelTitle] = "标题: {0}";
        zhEntries[StringKey.VaultLabelDescription] = "描述: {0}";
        zhEntries[StringKey.VaultLabelStatus] = "状态: {0}";
        zhEntries[StringKey.VaultLabelPriority] = "优先级: {0}";
        zhEntries[StringKey.VaultLabelAssignee] = "负责人: {0}";
        zhEntries[StringKey.VaultLabelDueDate] = "截止日期: {0}";
        zhEntries[StringKey.VaultLabelCreatedAt] = "创建时间: {0}";
        zhEntries[StringKey.VaultSummaryAssignee] = " (负责人: {0})";
        zhEntries[StringKey.VaultSummaryDueDate] = " - 截止: {0}";

        // === TodoService ===
        defaultEntries[StringKey.VaultTodoNotFound] = "Todo item not found";
        zhEntries[StringKey.VaultTodoNotFound] = "待办事项不存在";

        // === NotificationService ===
        defaultEntries[StringKey.VaultTaskCompleted] = "Task completed";
        defaultEntries[StringKey.VaultTaskFailed] = "Task failed";
        defaultEntries[StringKey.VaultAgentMessage] = "Agent message: {0}";
        defaultEntries[StringKey.VaultLogSendNotification] = "Sending notification: {0} - {1}";
        defaultEntries[StringKey.VaultLogWindowsNotificationFailed] = "Failed to send Windows notification: {0}";
        defaultEntries[StringKey.VaultLogWindowsNotificationException] = "Exception while sending Windows notification";

        zhEntries[StringKey.VaultTaskCompleted] = "任务完成";
        zhEntries[StringKey.VaultTaskFailed] = "任务失败";
        zhEntries[StringKey.VaultAgentMessage] = "代理消息: {0}";
        zhEntries[StringKey.VaultLogSendNotification] = "发送通知: {0} - {1}";
        zhEntries[StringKey.VaultLogWindowsNotificationFailed] = "发送 Windows 通知失败: {0}";
        zhEntries[StringKey.VaultLogWindowsNotificationException] = "发送 Windows 通知时发生异常";

        // === StateService ===
        defaultEntries[StringKey.VaultLogStateServiceInitialized] = "State service initialized, database path: {0}";
        defaultEntries[StringKey.VaultLogStateSaveSuccess] = "State saved successfully";
        defaultEntries[StringKey.VaultLogStateSaveFailed] = "Failed to save state";
        defaultEntries[StringKey.VaultLogStateLoadSuccess] = "State loaded successfully";
        defaultEntries[StringKey.VaultLogStateLoadFailed] = "Failed to load state";
        defaultEntries[StringKey.VaultLogStateClearSuccess] = "State cleared successfully";
        defaultEntries[StringKey.VaultLogStateClearFailed] = "Failed to clear state";
        defaultEntries[StringKey.VaultLogStatePersisted] = "AppState persisted";
        defaultEntries[StringKey.VaultLogPersistFailed] = "Failed to persist AppState";
        defaultEntries[StringKey.VaultLogNoPersistedState] = "No persisted AppState found";
        defaultEntries[StringKey.VaultLogStateLoadedFromPersist] = "AppState loaded from persistence";
        defaultEntries[StringKey.VaultLogStateLoadFromPersistFailed] = "Failed to load AppState from persistence";

        zhEntries[StringKey.VaultLogStateServiceInitialized] = "状态服务已初始化，数据库路径: {0}";
        zhEntries[StringKey.VaultLogStateSaveSuccess] = "状态保存成功";
        zhEntries[StringKey.VaultLogStateSaveFailed] = "保存状态失败";
        zhEntries[StringKey.VaultLogStateLoadSuccess] = "状态加载成功";
        zhEntries[StringKey.VaultLogStateLoadFailed] = "加载状态失败";
        zhEntries[StringKey.VaultLogStateClearSuccess] = "状态清除成功";
        zhEntries[StringKey.VaultLogStateClearFailed] = "清除状态失败";
        zhEntries[StringKey.VaultLogStatePersisted] = "AppState 已持久化";
        zhEntries[StringKey.VaultLogPersistFailed] = "持久化 AppState 失败";
        zhEntries[StringKey.VaultLogNoPersistedState] = "未找到持久化的 AppState";
        zhEntries[StringKey.VaultLogStateLoadedFromPersist] = "已从持久化加载 AppState";
        zhEntries[StringKey.VaultLogStateLoadFromPersistFailed] = "从持久化加载 AppState 失败";

        // === SqlitePersistencePlugin ===
        defaultEntries[StringKey.VaultLogSqliteInitialized] = "SQLite persistence plugin initialized, database path: {0}";
        defaultEntries[StringKey.VaultLogSqlitePersisted] = "AppState persisted to SQLite";
        defaultEntries[StringKey.VaultLogSqlitePersistFailed] = "Failed to persist AppState";
        defaultEntries[StringKey.VaultLogSqliteNoState] = "No persisted AppState found";
        defaultEntries[StringKey.VaultLogSqliteDeserializeFailed] = "Failed to deserialize AppStateDocument";
        defaultEntries[StringKey.VaultLogSqliteLoaded] = "AppState loaded from SQLite";
        defaultEntries[StringKey.VaultLogSqliteLoadFailed] = "Failed to load AppState from SQLite";

        zhEntries[StringKey.VaultLogSqliteInitialized] = "SQLite 持久化插件已初始化，数据库路径: {0}";
        zhEntries[StringKey.VaultLogSqlitePersisted] = "AppState 已持久化到 SQLite";
        zhEntries[StringKey.VaultLogSqlitePersistFailed] = "持久化 AppState 失败";
        zhEntries[StringKey.VaultLogSqliteNoState] = "未找到持久化的 AppState";
        zhEntries[StringKey.VaultLogSqliteDeserializeFailed] = "反序列化 AppStateDocument 失败";
        zhEntries[StringKey.VaultLogSqliteLoaded] = "已从 SQLite 加载 AppState";
        zhEntries[StringKey.VaultLogSqliteLoadFailed] = "从 SQLite 加载 AppState 失败";

        // === Store ===
        defaultEntries[StringKey.VaultLogStateLoadedFromPersistStore] = "State loaded from persistence";
        defaultEntries[StringKey.VaultLogStateLoadFailedDefault] = "Failed to load state from persistence, using default";
        defaultEntries[StringKey.VaultLogStateUnchanged] = "State unchanged (same reference returned)";
        defaultEntries[StringKey.VaultLogAsyncStateUnchanged] = "Async state unchanged (same reference returned)";
        defaultEntries[StringKey.VaultLogSubscriberError] = "Error in state subscriber handling change";
        defaultEntries[StringKey.VaultLogStatePersistedStore] = "State persisted";
        defaultEntries[StringKey.VaultLogStatePersistFailedStore] = "State persistence failed";

        zhEntries[StringKey.VaultLogStateLoadedFromPersistStore] = "已从持久化加载状态";
        zhEntries[StringKey.VaultLogStateLoadFailedDefault] = "从持久化加载状态失败，使用默认状态";
        zhEntries[StringKey.VaultLogStateUnchanged] = "状态未变更（返回相同引用）";
        zhEntries[StringKey.VaultLogAsyncStateUnchanged] = "异步状态未变更（返回相同引用）";
        zhEntries[StringKey.VaultLogSubscriberError] = "状态订阅者处理变更时发生错误";
        zhEntries[StringKey.VaultLogStatePersistedStore] = "状态已持久化";
        zhEntries[StringKey.VaultLogStatePersistFailedStore] = "状态持久化失败";

        // === TeamMemorySyncService ===
        defaultEntries[StringKey.VaultLogSyncAlreadyRunning] = "Team memory sync service is already running";
        defaultEntries[StringKey.VaultLogSyncStarted] = "Team memory sync service started, watching path: {0}";
        defaultEntries[StringKey.VaultLogSyncStopped] = "Team memory sync service stopped";
        defaultEntries[StringKey.VaultLogConflictMissingEntry] = "Cannot resolve conflict, missing local or remote entry: {0}";
        defaultEntries[StringKey.VaultConflictResolutionFailed] = "Conflict resolution failed";
        defaultEntries[StringKey.VaultLogScanLocalComplete] = "Local file scan complete, {0} files";
        defaultEntries[StringKey.VaultLogScanRemoteComplete] = "Remote file scan complete, {0} files";
        defaultEntries[StringKey.VaultLogScanRemoteFailed] = "Failed to scan remote files";
        defaultEntries[StringKey.VaultLogSyncFileFailed] = "Failed to sync file: {0}";
        defaultEntries[StringKey.VaultLogPushToRemote] = "Pushed file to remote: {0}";
        defaultEntries[StringKey.VaultLogPushToRemoteFailed] = "Failed to push file to remote: {0}";
        defaultEntries[StringKey.VaultLogPullFromRemote] = "Pulled file from remote: {0}";
        defaultEntries[StringKey.VaultLogPullFromRemoteFailed] = "Failed to pull file from remote: {0}";
        defaultEntries[StringKey.VaultLogPersistRemoteIndexFailed] = "Failed to persist remote index";

        zhEntries[StringKey.VaultLogSyncAlreadyRunning] = "团队记忆同步服务已在运行";
        zhEntries[StringKey.VaultLogSyncStarted] = "团队记忆同步服务已启动，监视路径: {0}";
        zhEntries[StringKey.VaultLogSyncStopped] = "团队记忆同步服务已停止";
        zhEntries[StringKey.VaultLogConflictMissingEntry] = "无法解决冲突，缺少本地或远程条目: {0}";
        zhEntries[StringKey.VaultConflictResolutionFailed] = "冲突解决失败";
        zhEntries[StringKey.VaultLogScanLocalComplete] = "扫描本地文件完成，共 {0} 个文件";
        zhEntries[StringKey.VaultLogScanRemoteComplete] = "扫描远程文件完成，共 {0} 个文件";
        zhEntries[StringKey.VaultLogScanRemoteFailed] = "扫描远程文件失败";
        zhEntries[StringKey.VaultLogSyncFileFailed] = "同步文件失败: {0}";
        zhEntries[StringKey.VaultLogPushToRemote] = "已推送文件到远程: {0}";
        zhEntries[StringKey.VaultLogPushToRemoteFailed] = "推送文件到远程失败: {0}";
        zhEntries[StringKey.VaultLogPullFromRemote] = "已从远程拉取文件: {0}";
        zhEntries[StringKey.VaultLogPullFromRemoteFailed] = "从远程拉取文件失败: {0}";
        zhEntries[StringKey.VaultLogPersistRemoteIndexFailed] = "持久化远程索引失败";

        // === MemoryManagementService ===
        defaultEntries[StringKey.VaultContentCannotBeEmptyThrow] = "Content cannot be empty";
        defaultEntries[StringKey.VaultLogMemoryAdded] = "[MemoryManagementService] Added memory: {0} [{1}]";
        defaultEntries[StringKey.VaultLogScanMemory] = "Scanning memory: {0}, category: {1}";
        defaultEntries[StringKey.VaultAllCategory] = "all";
        defaultEntries[StringKey.VaultLogRecordSearchHistoryFailed] = "Failed to record search history: {0}";
        defaultEntries[StringKey.VaultLogStartCleanup] = "Starting memory cleanup: archive > {0} days, delete > {1} days";
        defaultEntries[StringKey.VaultLogDeleteMemory] = "Deleted memory: {0}";
        defaultEntries[StringKey.VaultLogArchiveMemory] = "Archived memory: {0}";
        defaultEntries[StringKey.VaultLogCleanupComplete] = "Memory cleanup complete: checked {0}, archived {1}, deleted {2}, retained {3}";
        defaultEntries[StringKey.VaultLogArchiveNotImplemented] = "Archive feature not implemented, memory {0} not archived";
        defaultEntries[StringKey.VaultLogRestoreNotImplemented] = "Restore feature not implemented, memory {0} not restored";
        defaultEntries[StringKey.VaultLogSearchHistoryNotRegistered] = "Search history service not registered, cannot search past conversations";
        defaultEntries[StringKey.VaultLogSearchHistoryNotRegisteredContext] = "Search history service not registered, cannot build past context";
        defaultEntries[StringKey.VaultLogDailyLogNotRegistered] = "Daily log service not registered, cannot append log entry";
        defaultEntries[StringKey.VaultLogDailyLogNotRegisteredPrompt] = "Daily log service not registered, cannot build log prompt";
        defaultEntries[StringKey.VaultLogTeamSyncNotRegistered] = "Team memory sync service not registered, cannot sync team memory";
        defaultEntries[StringKey.VaultMatchReasonContent] = "Content match";
        defaultEntries[StringKey.VaultMatchReasonTag] = "Tag match";
        defaultEntries[StringKey.VaultMatchReasonType] = "Type match";
        defaultEntries[StringKey.VaultTeamShared] = "Team shared: {0}";
        defaultEntries[StringKey.VaultAgeLess7Days] = "< 7 days";
        defaultEntries[StringKey.VaultAge7To30Days] = "7-30 days";
        defaultEntries[StringKey.VaultAge30To90Days] = "30-90 days";
        defaultEntries[StringKey.VaultAgeMore90Days] = "> 90 days";
        defaultEntries[StringKey.VaultLogAddTeamPath] = "Added team memory path: {0} -> {1}";
        defaultEntries[StringKey.VaultLogRemoveTeamPath] = "Removed team memory path: {0} -> {1}";

        zhEntries[StringKey.VaultContentCannotBeEmptyThrow] = "内容不能为空";
        zhEntries[StringKey.VaultLogMemoryAdded] = "[MemoryManagementService] 添加记忆: {0} [{1}]";
        zhEntries[StringKey.VaultLogScanMemory] = "扫描内存: {0}, 类别: {1}";
        zhEntries[StringKey.VaultAllCategory] = "全部";
        zhEntries[StringKey.VaultLogRecordSearchHistoryFailed] = "记录搜索历史失败: {0}";
        zhEntries[StringKey.VaultLogStartCleanup] = "开始内存清理: 归档 > {0} 天, 删除 > {1} 天";
        zhEntries[StringKey.VaultLogDeleteMemory] = "删除记忆: {0}";
        zhEntries[StringKey.VaultLogArchiveMemory] = "归档记忆: {0}";
        zhEntries[StringKey.VaultLogCleanupComplete] = "内存清理完成: 检查 {0}, 归档 {1}, 删除 {2}, 保留 {3}";
        zhEntries[StringKey.VaultLogArchiveNotImplemented] = "归档功能尚未实现，记忆 {0} 未被归档";
        zhEntries[StringKey.VaultLogRestoreNotImplemented] = "恢复功能尚未实现，记忆 {0} 未被恢复";
        zhEntries[StringKey.VaultLogSearchHistoryNotRegistered] = "搜索历史服务未注册，无法搜索过往对话";
        zhEntries[StringKey.VaultLogSearchHistoryNotRegisteredContext] = "搜索历史服务未注册，无法构建过往上下文";
        zhEntries[StringKey.VaultLogDailyLogNotRegistered] = "助手日志服务未注册，无法追加日志条目";
        zhEntries[StringKey.VaultLogDailyLogNotRegisteredPrompt] = "助手日志服务未注册，无法构建日志提示";
        zhEntries[StringKey.VaultLogTeamSyncNotRegistered] = "团队记忆同步服务未注册，无法同步团队记忆";
        zhEntries[StringKey.VaultMatchReasonContent] = "内容匹配";
        zhEntries[StringKey.VaultMatchReasonTag] = "标签匹配";
        zhEntries[StringKey.VaultMatchReasonType] = "类型匹配";
        zhEntries[StringKey.VaultTeamShared] = "团队共享: {0}";
        zhEntries[StringKey.VaultAgeLess7Days] = "< 7 天";
        zhEntries[StringKey.VaultAge7To30Days] = "7-30 天";
        zhEntries[StringKey.VaultAge30To90Days] = "30-90 天";
        zhEntries[StringKey.VaultAgeMore90Days] = "> 90 天";
        zhEntries[StringKey.VaultLogAddTeamPath] = "添加团队内存路径: {0} -> {1}";
        zhEntries[StringKey.VaultLogRemoveTeamPath] = "移除团队内存路径: {0} -> {1}";

        // === UserInteractionService ===
        defaultEntries[StringKey.VaultLogHeadlessAsk] = "[Headless] Ask: {0}";
        defaultEntries[StringKey.VaultLogOption] = "Option {0}: {1}";
        defaultEntries[StringKey.VaultLogSuccess] = "[Success] {0}";
        defaultEntries[StringKey.VaultLogHeadlessConfirm] = "[Headless] Confirm: {0} -> auto-approved";

        zhEntries[StringKey.VaultLogHeadlessAsk] = "[Headless] 询问: {0}";
        zhEntries[StringKey.VaultLogOption] = "选项 {0}: {1}";
        zhEntries[StringKey.VaultLogSuccess] = "[成功] {0}";
        zhEntries[StringKey.VaultLogHeadlessConfirm] = "[Headless] 确认: {0} → auto-approved";

        // === WorkspaceService ===
        defaultEntries[StringKey.VaultLogDirectoryExists] = "Directory already exists: {0}";
        defaultEntries[StringKey.VaultLogAddedWorkspace] = "Added workspace directory: {0}";
        defaultEntries[StringKey.VaultLogRemovedWorkspace] = "Removed workspace directory: {0}";
        defaultEntries[StringKey.VaultLogDirectoryNotExist] = "Directory does not exist: {0}";
        defaultEntries[StringKey.VaultLogClearedWorkspaces] = "Cleared all additional workspace directories";

        zhEntries[StringKey.VaultLogDirectoryExists] = "目录已存在: {0}";
        zhEntries[StringKey.VaultLogAddedWorkspace] = "已添加工作目录: {0}";
        zhEntries[StringKey.VaultLogRemovedWorkspace] = "已移除工作目录: {0}";
        zhEntries[StringKey.VaultLogDirectoryNotExist] = "目录不存在: {0}";
        zhEntries[StringKey.VaultLogClearedWorkspaces] = "已清除所有额外工作目录";

        // === SessionTagService ===
        defaultEntries[StringKey.VaultLogSessionAddTag] = "Session {0} added tag: {1}";
        defaultEntries[StringKey.VaultLogSessionRemoveTag] = "Session {0} removed tag: {1}";
        defaultEntries[StringKey.VaultLogLoadedSessionTags] = "Loaded {0} session tags";
        defaultEntries[StringKey.VaultLogLoadSessionTagsFailed] = "Failed to load session tags";

        zhEntries[StringKey.VaultLogSessionAddTag] = "会话 {0} 添加标签: {1}";
        zhEntries[StringKey.VaultLogSessionRemoveTag] = "会话 {0} 移除标签: {1}";
        zhEntries[StringKey.VaultLogLoadedSessionTags] = "已加载 {0} 个会话标签";
        zhEntries[StringKey.VaultLogLoadSessionTagsFailed] = "加载会话标签失败";

        // === ThinkingStore ===
        defaultEntries[StringKey.VaultLogThinkingStore] = "[ThinkingStore] Stored thinking: Session={0}, Length={1}";
        defaultEntries[StringKey.VaultLogThinkingLoadFailed] = "[ThinkingStore] Failed to load thinking data";
        defaultEntries[StringKey.VaultLogThinkingSaveFailed] = "[ThinkingStore] Failed to save thinking data";

        zhEntries[StringKey.VaultLogThinkingStore] = "[ThinkingStore] 存储 thinking: Session={0}, Length={1}";
        zhEntries[StringKey.VaultLogThinkingLoadFailed] = "[ThinkingStore] 加载 thinking 数据失败";
        zhEntries[StringKey.VaultLogThinkingSaveFailed] = "[ThinkingStore] 保存 thinking 数据失败";

        // === FileOperationTracker ===
        defaultEntries[StringKey.VaultLogFileOperationRecord] = "File operation record: {0} {1}";
        defaultEntries[StringKey.VaultLogFileOperationCleared] = "File operation records cleared";

        zhEntries[StringKey.VaultLogFileOperationRecord] = "文件操作记录: {0} {1}";
        zhEntries[StringKey.VaultLogFileOperationCleared] = "文件操作记录已清除";

        // === MemorySearchHistory ===
        defaultEntries[StringKey.VaultLogRecordSearch] = "[SearchHistory] Record search: {0}, result count: {1}";
        defaultEntries[StringKey.VaultLogSearchPastConversations] = "[SearchHistory] Search past conversations: {0}, found {1}";
        defaultEntries[StringKey.VaultPastContextHeader] = "## Past Conversation Context";
        defaultEntries[StringKey.VaultPastContextIntro] = "The following relevant information was retrieved from historical conversations:";
        defaultEntries[StringKey.VaultTodayTime] = "Today";
        defaultEntries[StringKey.VaultYesterdayTime] = "Yesterday";
        defaultEntries[StringKey.VaultDaysAgoTime] = "{0} days ago";
        defaultEntries[StringKey.VaultWeeksAgoTime] = "{0} weeks ago";
        defaultEntries[StringKey.VaultMonthsAgoTime] = "{0} months ago";
        defaultEntries[StringKey.VaultNoTitleDefault] = "No title";
        defaultEntries[StringKey.VaultLabelTagsInline] = "Tags: {0}";
        defaultEntries[StringKey.VaultRelatedSearchHeader] = "### Related Search History";
        defaultEntries[StringKey.VaultRelatedSearchResult] = "- [{0}] \"{1}\" ({2} results)";
        defaultEntries[StringKey.VaultLogBuildPastContext] = "[SearchHistory] Build past context: query={0}, referenced {1} memories";

        zhEntries[StringKey.VaultLogRecordSearch] = "[SearchHistory] 记录搜索: {0}, 结果数: {1}";
        zhEntries[StringKey.VaultLogSearchPastConversations] = "[SearchHistory] 搜索过往对话: {0}, 找到 {1} 条";
        zhEntries[StringKey.VaultPastContextHeader] = "## 过往对话上下文";
        zhEntries[StringKey.VaultPastContextIntro] = "以下是从历史对话中检索到的相关信息：";
        zhEntries[StringKey.VaultTodayTime] = "今天";
        zhEntries[StringKey.VaultYesterdayTime] = "昨天";
        zhEntries[StringKey.VaultDaysAgoTime] = "{0} 天前";
        zhEntries[StringKey.VaultWeeksAgoTime] = "{0} 周前";
        zhEntries[StringKey.VaultMonthsAgoTime] = "{0} 个月前";
        zhEntries[StringKey.VaultNoTitleDefault] = "无标题";
        zhEntries[StringKey.VaultLabelTagsInline] = "标签: {0}";
        zhEntries[StringKey.VaultRelatedSearchHeader] = "### 相关历史搜索";
        zhEntries[StringKey.VaultRelatedSearchResult] = "- [{0}] \"{1}\" ({2} 条结果)";
        zhEntries[StringKey.VaultLogBuildPastContext] = "[SearchHistory] 构建过往上下文: 查询={0}, 引用={1} 条记忆";

        // === MemoryTruncator ===
        defaultEntries[StringKey.VaultTruncatedMaxLines] = "\n\n... (Content truncated, exceeded max line limit)";
        defaultEntries[StringKey.VaultTruncatedTotalLines] = "... (Content truncated, original had {0} lines)";
        defaultEntries[StringKey.VaultTruncatedMaxBytes] = "\n\n... (Content truncated, exceeded max byte limit)";

        zhEntries[StringKey.VaultTruncatedMaxLines] = "\n\n... (内容已截断，超过最大行数限制)";
        zhEntries[StringKey.VaultTruncatedTotalLines] = "... (内容已截断，原内容共 {0} 行)";
        zhEntries[StringKey.VaultTruncatedMaxBytes] = "\n\n... (内容已截断，超过最大字节数限制)";

        // === MemoryStore ===
        defaultEntries[StringKey.VaultLogStoreAddMemory] = "[MemoryStore] Added memory: {0} [{1}]";
        defaultEntries[StringKey.VaultLogStoreDeleteMemory] = "[MemoryStore] Deleted memory: {0}";
        defaultEntries[StringKey.VaultLogStoreArchiveMemory] = "[MemoryStore] Archived memory: {0}";
        defaultEntries[StringKey.VaultLogStoreCleanedExpired] = "[MemoryStore] Cleaned {0} expired memories";
        defaultEntries[StringKey.VaultLogStoreLoadedMemories] = "[MemoryStore] Loaded {0} memories";
        defaultEntries[StringKey.VaultLogStoreLoadFailed] = "[MemoryStore] Failed to load memories";
        defaultEntries[StringKey.VaultLogStoreSavedMemories] = "[MemoryStore] Saved {0} memories";
        defaultEntries[StringKey.VaultLogStoreSaveFailed] = "[MemoryStore] Failed to save memories";
        defaultEntries[StringKey.VaultLogStoreSaveFailedError] = "[MemoryStore] Failed to save memories: {0}";

        zhEntries[StringKey.VaultLogStoreAddMemory] = "[MemoryStore] 添加记忆: {0} [{1}]";
        zhEntries[StringKey.VaultLogStoreDeleteMemory] = "[MemoryStore] 删除记忆: {0}";
        zhEntries[StringKey.VaultLogStoreArchiveMemory] = "[MemoryStore] 归档记忆: {0}";
        zhEntries[StringKey.VaultLogStoreCleanedExpired] = "[MemoryStore] 清理了 {0} 条过期记忆";
        zhEntries[StringKey.VaultLogStoreLoadedMemories] = "[MemoryStore] 加载了 {0} 条记忆";
        zhEntries[StringKey.VaultLogStoreLoadFailed] = "[MemoryStore] 加载记忆失败";
        zhEntries[StringKey.VaultLogStoreSavedMemories] = "[MemoryStore] 已保存 {0} 条记忆";
        zhEntries[StringKey.VaultLogStoreSaveFailed] = "[MemoryStore] 保存记忆失败";
        zhEntries[StringKey.VaultLogStoreSaveFailedError] = "[MemoryStore] 保存记忆失败: {0}";
    }
}
