namespace Infrastructure.Localization;

public static partial class LocalizerInitializer
{
    private static void RegisterEyesEntries(Dictionary<string, string> defaultEntries, Dictionary<string, string> zhEntries)
    {
        // === CodeIndexService ===
        defaultEntries[StringKey.CodeIndexServiceStarting] = "Starting CodeIndex service...";
        defaultEntries[StringKey.CodeIndexServiceStarted] = "CodeIndex service started";
        defaultEntries[StringKey.CodeIndexServiceStopping] = "Stopping CodeIndex service...";
        defaultEntries[StringKey.CodeIndexServiceStopped] = "CodeIndex service stopped";
        defaultEntries[StringKey.CodeIndexRebuilding] = "Rebuilding code index...";
        defaultEntries[StringKey.CodeIndexRebuilt] = "Code index rebuilt";

        zhEntries[StringKey.CodeIndexServiceStarting] = "正在启动 CodeIndex 服务...";
        zhEntries[StringKey.CodeIndexServiceStarted] = "CodeIndex 服务已启动";
        zhEntries[StringKey.CodeIndexServiceStopping] = "正在停止 CodeIndex 服务...";
        zhEntries[StringKey.CodeIndexServiceStopped] = "CodeIndex 服务已停止";
        zhEntries[StringKey.CodeIndexRebuilding] = "正在重建代码索引...";
        zhEntries[StringKey.CodeIndexRebuilt] = "代码索引已重建";

        // === LspIntegration ===
        defaultEntries[StringKey.LspIntegrationStarting] = "Starting LSP integration...";
        defaultEntries[StringKey.LspIntegrationStarted] = "LSP integration started";
        defaultEntries[StringKey.LspIntegrationStopping] = "Stopping LSP integration...";
        defaultEntries[StringKey.LspIntegrationStopped] = "LSP integration stopped";

        zhEntries[StringKey.LspIntegrationStarting] = "正在启动 LSP 集成...";
        zhEntries[StringKey.LspIntegrationStarted] = "LSP 集成已启动";
        zhEntries[StringKey.LspIntegrationStopping] = "正在停止 LSP 集成...";
        zhEntries[StringKey.LspIntegrationStopped] = "LSP 集成已停止";

        // === ProgressiveDisclosureService ===
        defaultEntries[StringKey.ProgressiveDisclosureServiceStarting] = "Starting ProgressiveDisclosure service...";
        defaultEntries[StringKey.ProgressiveDisclosureServiceStarted] = "ProgressiveDisclosure service started";
        defaultEntries[StringKey.ProgressiveDisclosureServiceStopping] = "Stopping ProgressiveDisclosure service...";
        defaultEntries[StringKey.ProgressiveDisclosureServiceStopped] = "ProgressiveDisclosure service stopped";
        defaultEntries[StringKey.ProgressiveDisclosureEnabled] = "Progressive disclosure enabled";
        defaultEntries[StringKey.ProgressiveDisclosureDisabled] = "Progressive disclosure disabled";
        defaultEntries[StringKey.ProgressiveDisclosureUpdateStarted] = "Updating progressive disclosure...";
        defaultEntries[StringKey.ProgressiveDisclosureUpdateCompleted] = "Progressive disclosure updated";

        zhEntries[StringKey.ProgressiveDisclosureServiceStarting] = "正在启动渐进式披露服务...";
        zhEntries[StringKey.ProgressiveDisclosureServiceStarted] = "渐进式披露服务已启动";
        zhEntries[StringKey.ProgressiveDisclosureServiceStopping] = "正在停止渐进式披露服务...";
        zhEntries[StringKey.ProgressiveDisclosureServiceStopped] = "渐进式披露服务已停止";
        zhEntries[StringKey.ProgressiveDisclosureEnabled] = "渐进式披露已启用";
        zhEntries[StringKey.ProgressiveDisclosureDisabled] = "渐进式披露已禁用";
        zhEntries[StringKey.ProgressiveDisclosureUpdateStarted] = "正在更新渐进式披露...";
        zhEntries[StringKey.ProgressiveDisclosureUpdateCompleted] = "渐进式披露已更新";

        // === LspClient ===
        defaultEntries[StringKey.LspClientConnecting] = "Connecting to LSP server: {0}";
        defaultEntries[StringKey.LspClientConnected] = "Connected to LSP server: {0}";
        defaultEntries[StringKey.LspClientDisconnecting] = "Disconnecting from LSP server...";
        defaultEntries[StringKey.LspClientDisconnected] = "Disconnected from LSP server";
        defaultEntries[StringKey.LspClientError] = "LSP client error: {0}";
        defaultEntries[StringKey.LspClientRequestFailed] = "LSP request failed: {0}";

        zhEntries[StringKey.LspClientConnecting] = "正在连接到 LSP 服务器: {0}";
        zhEntries[StringKey.LspClientConnected] = "已连接到 LSP 服务器: {0}";
        zhEntries[StringKey.LspClientDisconnecting] = "正在断开 LSP 服务器连接...";
        zhEntries[StringKey.LspClientDisconnected] = "已断开 LSP 服务器连接";
        zhEntries[StringKey.LspClientError] = "LSP 客户端错误: {0}";
        zhEntries[StringKey.LspClientRequestFailed] = "LSP 请求失败: {0}";

        // === LspConfigLoader ===
        defaultEntries[StringKey.LspConfigLoaderLoading] = "Loading LSP configuration...";
        defaultEntries[StringKey.LspConfigLoaderLoaded] = "LSP configuration loaded";
        defaultEntries[StringKey.LspConfigLoaderError] = "LSP configuration error: {0}";
        defaultEntries[StringKey.LspConfigLoaderNoConfig] = "No LSP configuration found";
        defaultEntries[StringKey.LspConfigLoaderInvalid] = "Invalid LSP configuration";
        defaultEntries[StringKey.LspConfigLoaderLanguageNotFound] = "LSP server not found for language: {0}";

        zhEntries[StringKey.LspConfigLoaderLoading] = "正在加载 LSP 配置...";
        zhEntries[StringKey.LspConfigLoaderLoaded] = "LSP 配置已加载";
        zhEntries[StringKey.LspConfigLoaderError] = "LSP 配置错误: {0}";
        zhEntries[StringKey.LspConfigLoaderNoConfig] = "未找到 LSP 配置";
        zhEntries[StringKey.LspConfigLoaderInvalid] = "无效的 LSP 配置";
        zhEntries[StringKey.LspConfigLoaderLanguageNotFound] = "未找到语言 {0} 的 LSP 服务器";

        // === LspFileSync ===
        defaultEntries[StringKey.LspFileSyncStarting] = "Starting LSP file sync...";
        defaultEntries[StringKey.LspFileSyncStarted] = "LSP file sync started";
        defaultEntries[StringKey.LspFileSyncStopping] = "Stopping LSP file sync...";
        defaultEntries[StringKey.LspFileSyncStopped] = "LSP file sync stopped";
        defaultEntries[StringKey.LspFileSyncWatching] = "Watching file changes: {0}";
        defaultEntries[StringKey.LspFileSyncFileChanged] = "File changed: {0}";
        defaultEntries[StringKey.LspFileSyncFileCreated] = "File created: {0}";
        defaultEntries[StringKey.LspFileSyncFileDeleted] = "File deleted: {0}";
        defaultEntries[StringKey.LspFileSyncError] = "LSP file sync error: {0}";
        defaultEntries[StringKey.LspFileSyncIgnored] = "Ignored file: {0}";

        zhEntries[StringKey.LspFileSyncStarting] = "正在启动 LSP 文件同步...";
        zhEntries[StringKey.LspFileSyncStarted] = "LSP 文件同步已启动";
        zhEntries[StringKey.LspFileSyncStopping] = "正在停止 LSP 文件同步...";
        zhEntries[StringKey.LspFileSyncStopped] = "LSP 文件同步已停止";
        zhEntries[StringKey.LspFileSyncWatching] = "监控文件变更: {0}";
        zhEntries[StringKey.LspFileSyncFileChanged] = "文件已变更: {0}";
        zhEntries[StringKey.LspFileSyncFileCreated] = "文件已创建: {0}";
        zhEntries[StringKey.LspFileSyncFileDeleted] = "文件已删除: {0}";
        zhEntries[StringKey.LspFileSyncError] = "LSP 文件同步错误: {0}";
        zhEntries[StringKey.LspFileSyncIgnored] = "已忽略文件: {0}";

        // === LspService ===
        defaultEntries[StringKey.LspServiceStarting] = "Starting LSP service...";
        defaultEntries[StringKey.LspServiceStarted] = "LSP service started";
        defaultEntries[StringKey.LspServiceStopping] = "Stopping LSP service...";
        defaultEntries[StringKey.LspServiceStopped] = "LSP service stopped";
        defaultEntries[StringKey.LspServiceClientConnected] = "LSP client connected: {0}";
        defaultEntries[StringKey.LspServiceClientDisconnected] = "LSP client disconnected: {0}";
        defaultEntries[StringKey.LspServiceError] = "LSP service error: {0}";

        zhEntries[StringKey.LspServiceStarting] = "正在启动 LSP 服务...";
        zhEntries[StringKey.LspServiceStarted] = "LSP 服务已启动";
        zhEntries[StringKey.LspServiceStopping] = "正在停止 LSP 服务...";
        zhEntries[StringKey.LspServiceStopped] = "LSP 服务已停止";
        zhEntries[StringKey.LspServiceClientConnected] = "LSP 客户端已连接: {0}";
        zhEntries[StringKey.LspServiceClientDisconnected] = "LSP 客户端已断开: {0}";
        zhEntries[StringKey.LspServiceError] = "LSP 服务错误: {0}";

        // === CodeIndexService Additional ===
        defaultEntries[StringKey.CodeIndexServiceWorkspace] = "Code index service starting, workspace: {0}";
        defaultEntries[StringKey.CodeIndexBuildCompleted] = "Index build completed: Updated {0}, Skipped {1}, Deleted {2}";
        defaultEntries[StringKey.CodeIndexWatcherStarted] = "File watcher started";
        defaultEntries[StringKey.CodeIndexLspReady] = "LSP integration ready, LSP available: {0}";
        defaultEntries[StringKey.CodeIndexStartFailed] = "Code index service start failed";

        zhEntries[StringKey.CodeIndexServiceWorkspace] = "代码索引服务启动，工作区: {0}";
        zhEntries[StringKey.CodeIndexBuildCompleted] = "索引构建完成: 更新 {0}, 跳过 {1}, 删除 {2}";
        zhEntries[StringKey.CodeIndexWatcherStarted] = "文件监听已启动";
        zhEntries[StringKey.CodeIndexLspReady] = "LSP 集成已就绪，LSP 可用: {0}";
        zhEntries[StringKey.CodeIndexStartFailed] = "代码索引服务启动失败";

        // === LspIntegration ===
        defaultEntries[StringKey.LspIntegrationGotoDefinitionFailed] = "LSP GotoDefinition failed";
        defaultEntries[StringKey.LspIntegrationFindReferencesFailed] = "LSP FindReferences failed";
        defaultEntries[StringKey.LspIntegrationIncrementalUpdateFailed] = "LSP integration incremental update failed: {0}";

        zhEntries[StringKey.LspIntegrationGotoDefinitionFailed] = "LSP GotoDefinition 失败";
        zhEntries[StringKey.LspIntegrationFindReferencesFailed] = "LSP FindReferences 失败";
        zhEntries[StringKey.LspIntegrationIncrementalUpdateFailed] = "LSP 集成增量更新失败: {0}";

        // === ProgressiveDisclosureService ===
        defaultEntries[StringKey.ProgressiveDisclosureNoSymbolsFound] = "No symbols found matching \"{0}\"";
        defaultEntries[StringKey.ProgressiveDisclosureSymbolIndex] = "## Symbol Index: ";
        defaultEntries[StringKey.ProgressiveDisclosureCallGraph] = "## Call Graph";
        defaultEntries[StringKey.ProgressiveDisclosureCallers] = " callers";
        defaultEntries[StringKey.ProgressiveDisclosureCallees] = " calls";
        defaultEntries[StringKey.ProgressiveDisclosureInheritors] = " inheritors";
        defaultEntries[StringKey.ProgressiveDisclosureDependencies] = " dependencies";
        defaultEntries[StringKey.ProgressiveDisclosureRelationshipFailed] = "Failed to get relationship info for {0}";
        defaultEntries[StringKey.ProgressiveDisclosureSourceCode] = "## Source Code";
        defaultEntries[StringKey.ProgressiveDisclosureReadSourceFailed] = "Failed to read source for {0}";

        zhEntries[StringKey.ProgressiveDisclosureNoSymbolsFound] = "未找到与 \"{0}\" 匹配的符号";
        zhEntries[StringKey.ProgressiveDisclosureSymbolIndex] = "## 符号索引: ";
        zhEntries[StringKey.ProgressiveDisclosureCallGraph] = "## 调用关系";
        zhEntries[StringKey.ProgressiveDisclosureCallers] = " 的调用者";
        zhEntries[StringKey.ProgressiveDisclosureCallees] = " 调用了";
        zhEntries[StringKey.ProgressiveDisclosureInheritors] = " 的继承者";
        zhEntries[StringKey.ProgressiveDisclosureDependencies] = " 的依赖";
        zhEntries[StringKey.ProgressiveDisclosureRelationshipFailed] = "获取 {0} 的关系信息失败";
        zhEntries[StringKey.ProgressiveDisclosureSourceCode] = "## 源代码";
        zhEntries[StringKey.ProgressiveDisclosureReadSourceFailed] = "读取 {0} 源码失败";

        // === LspClient ===
        defaultEntries[StringKey.LspClientInitializeFailed] = "LSP server initialization failed";
        zhEntries[StringKey.LspClientInitializeFailed] = "LSP服务器初始化失败";
    }
}