namespace Core.Utils;

/// <summary>
/// 错误消息常量
/// </summary>
public static class ErrorMessages
{
    /// <summary>
    /// 传输未连接
    /// </summary>
    public const string TransportNotConnected = "传输未连接";

    /// <summary>
    /// WebSocket 未连接
    /// </summary>
    public const string WebSocketNotConnected = "WebSocket 未连接";

    /// <summary>
    /// SSE 传输未就绪
    /// </summary>
    public const string SseTransportNotReady = "SSE 传输未就绪";

    /// <summary>
    /// 发送聊天消息失败
    /// </summary>
    public const string SendApiMessageFailed = "发送聊天消息失败";

    /// <summary>
    /// 清空聊天历史失败
    /// </summary>
    public const string ClearMessageListFailed = "清空聊天历史失败";

    /// <summary>
    /// AgentCoordinator 未初始化
    /// </summary>
    public const string AgentCoordinatorNotInitialized = "AgentCoordinator 未初始化";

    /// <summary>
    /// 没有可用的当前层级进行提升
    /// </summary>
    public const string NoCurrentLevelToPromote = "没有可用的当前层级进行提升";

    /// <summary>
    /// Docker 客户端未初始化
    /// </summary>
    public const string DockerClientNotInitialized = "Docker 客户端未初始化";

    /// <summary>
    /// 无法启动编译进程
    /// </summary>
    public const string CannotStartCompilationProcess = "无法启动编译进程";

    /// <summary>
    /// 编译超时
    /// </summary>
    public const string CompilationTimeout = "编译超时";

    /// <summary>
    /// 编译失败
    /// </summary>
    public const string CompilationFailed = "编译失败:\n{0}";

    /// <summary>
    /// 无法启动执行进程
    /// </summary>
    public const string CannotStartExecutionProcess = "无法启动执行进程";

    /// <summary>
    /// 此构造函数仅用于模拟模式
    /// </summary>
    public const string ConstructorOnlyForMockMode = "此构造函数仅用于模拟模式，请使用带 IServiceProvider 参数的构造函数";

    /// <summary>
    /// MCP 客户端未连接
    /// </summary>
    public const string McpClientNotConnected = "MCP 客户端未连接";

    /// <summary>
    /// 无法解析初始化响应
    /// </summary>
    public const string CannotParseInitializationResponse = "无法解析初始化响应";

    /// <summary>
    /// 传输未运行
    /// </summary>
    public const string TransportNotRunning = "传输未运行";

    /// <summary>
    /// 不支持远程客户端管理
    /// </summary>
    public const string RemoteClientManagementNotSupported = "LocalToolRegistry 不支持远程客户端管理";

    /// <summary>
    /// 同步委托未设置
    /// </summary>
    public const string SyncDelegateNotSet = "同步委托未设置，请使用 ProceedAsync 方法";

    /// <summary>
    /// 方法执行委托未设置
    /// </summary>
    public const string MethodExecutionDelegateNotSet = "方法执行委托未设置";

    /// <summary>
    /// 请使用异步拦截方法
    /// </summary>
    public const string UseAsyncInterceptMethod = "请使用 InterceptMethodAsync 方法拦截异步方法";

    /// <summary>
    /// SSE 传输不支持
    /// </summary>
    public const string SseTransportNotSupported = "SSE transport is not yet supported";

    /// <summary>
    /// 无法获取有效的访问令牌
    /// </summary>
    public const string CannotGetValidAccessToken = "无法获取有效的访问令牌";

    /// <summary>
    /// 未连接到服务器
    /// </summary>
    public const string NotConnectedToServer = "未连接到服务器";

    /// <summary>
    /// 意外的流结束
    /// </summary>
    public const string UnexpectedEndOfStream = "Unexpected end of stream while reading message body.";

    /// <summary>
    /// 不支持直接发送消息
    /// </summary>
    public const string DirectMessageSendingNotSupported = "{0} 使用 SDK 内部传输机制，不支持直接发送消息";

    /// <summary>
    /// 传输未启动
    /// </summary>
    public const string TransportNotStarted = "传输未启动";

    /// <summary>
    /// ApiKey 是必需的
    /// </summary>
    public const string ApiKeyRequired = "ApiKey is required for ApiKey auth";

    /// <summary>
    /// BearerToken 是必需的
    /// </summary>
    public const string BearerTokenRequired = "BearerToken is required for Bearer auth";

    /// <summary>
    /// 用户名是必需的
    /// </summary>
    public const string UsernameRequired = "Username is required for Basic auth";

    /// <summary>
    /// 密码是必需的
    /// </summary>
    public const string PasswordRequired = "Password is required for Basic auth";

    /// <summary>
    /// ClientId 是必需的
    /// </summary>
    public const string ClientIdRequired = "ClientId is required for OAuth2 auth";

    /// <summary>
    /// ClientSecret 是必需的
    /// </summary>
    public const string ClientSecretRequired = "ClientSecret is required for OAuth2 auth";

    /// <summary>
    /// TokenUrl 是必需的
    /// </summary>
    public const string TokenUrlRequired = "TokenUrl is required for OAuth2 auth";

    /// <summary>
    /// 列配置数量与值选择器数量不匹配
    /// </summary>
    public const string ColumnConfigCountMismatch = "列配置数量与值选择器数量不匹配";

    /// <summary>
    /// 无法解析 JSON-RPC 请求
    /// </summary>
    public const string CannotParseJsonRpcRequest = "无法解析 JSON-RPC 请求";

    /// <summary>
    /// 无法解析 JSON-RPC 通知
    /// </summary>
    public const string CannotParseJsonRpcNotification = "无法解析 JSON-RPC 通知";

    /// <summary>
    /// 无法解析 JSON-RPC 响应
    /// </summary>
    public const string CannotParseJsonRpcResponse = "无法解析 JSON-RPC 响应";

    /// <summary>
    /// 未配置引用解析器
    /// </summary>
    public const string ReferenceResolverNotConfigured = "未配置引用解析器，无法解析代码引用";

    /// <summary>
    /// 未找到 Git 仓库根目录
    /// </summary>
    public const string GitRepositoryRootNotFound = "未找到 Git 仓库根目录";

    /// <summary>
    /// 命令不能为空
    /// </summary>
    public const string CommandCannotBeEmpty = "命令不能为空";

    /// <summary>
    /// 目录不存在
    /// </summary>
    public const string DirectoryNotFound = "目录不存在: {0}";

    /// <summary>
    /// 路径不存在
    /// </summary>
    public const string PathNotFound = "路径不存在: {0}";

    /// <summary>
    /// 代码不能为空
    /// </summary>
    public const string CodeCannotBeEmpty = "代码不能为空";

    /// <summary>
    /// 代码长度超过限制
    /// </summary>
    public const string CodeLengthExceeded = "代码长度超过限制 (最大 {0} 字符)";

    /// <summary>
    /// 任务不存在
    /// </summary>
    public const string TaskNotFound = "任务 {0} 不存在";

    /// <summary>
    /// 依赖任务不存在
    /// </summary>
    public const string DependencyTaskNotFound = "依赖任务 {0} 不存在";

    /// <summary>
    /// 依赖关系不存在
    /// </summary>
    public const string DependencyNotFound = "依赖关系 {0} 不存在";

    /// <summary>
    /// 待办事项不存在
    /// </summary>
    public const string TodoItemNotFound = "待办事项不存在";

    /// <summary>
    /// 未找到要替换的字符串
    /// </summary>
    public const string ReplacementStringNotFound = "未找到要替换的字符串";

    /// <summary>
    /// 参数不能为空
    /// </summary>
    public const string ArgumentCannotBeEmpty = "{0} 不能为空";
}
