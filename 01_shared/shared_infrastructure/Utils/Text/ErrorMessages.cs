namespace Core.Utils;

/// <summary>
/// 错误消息常量 — 每条消息带唯一编码前缀 [域+3位数字]，方便在日志乱码时通过编码定位
/// 域: TRN=Transport通用, SSE=SSE传输, WSK=WebSocket, SSH=SSH会话, MCP=MCP协议, MPB=MCPB bundle,
///     AGT=Agent, CMP=编译, AUT=认证, PPL=管道, CDI=代码索引, GIT=Git, INF=基础设施, GEN=通用
/// </summary>
public static class ErrorMessages
{
    /// <summary>
    /// 传输未连接
    /// </summary>
    public const string TransportNotConnected = "[TRN001] 传输未连接";

    /// <summary>
    /// WebSocket 未连接
    /// </summary>
    public const string WebSocketNotConnected = "[WSK001] WebSocket 未连接";

    /// <summary>
    /// SSE 传输未就绪
    /// </summary>
    public const string SseTransportNotReady = "[SSE001] SSE 传输未就绪";

    /// <summary>
    /// 发送聊天消息失败
    /// </summary>
    public const string SendApiMessageFailed = "[AGT001] 发送聊天消息失败";

    /// <summary>
    /// 清空聊天历史失败
    /// </summary>
    public const string ClearMessageListFailed = "[AGT002] 清空聊天历史失败";

    /// <summary>
    /// AgentCoordinator 未初始化
    /// </summary>
    public const string AgentCoordinatorNotInitialized = "[AGT003] AgentCoordinator 未初始化";

    /// <summary>
    /// 没有可用的当前层级进行提升
    /// </summary>
    public const string NoCurrentLevelToPromote = "[AGT004] 没有可用的当前层级进行提升";

    /// <summary>
    /// Docker 客户端未初始化
    /// </summary>
    public const string DockerClientNotInitialized = "[AGT005] Docker 客户端未初始化";

    /// <summary>
    /// 无法启动编译进程
    /// </summary>
    public const string CannotStartCompilationProcess = "[CMP001] 无法启动编译进程";

    /// <summary>
    /// 编译超时
    /// </summary>
    public const string CompilationTimeout = "[CMP002] 编译超时";

    /// <summary>
    /// 编译失败
    /// </summary>
    public const string CompilationFailed = "[CMP003] 编译失败:\n{0}";

    /// <summary>
    /// 无法启动执行进程
    /// </summary>
    public const string CannotStartExecutionProcess = "[CMP004] 无法启动执行进程";

    /// <summary>
    /// 此构造函数仅用于模拟模式
    /// </summary>
    public const string ConstructorOnlyForMockMode = "[GEN001] 此构造函数仅用于模拟模式，请使用带 IServiceProvider 参数的构造函数";

    /// <summary>
    /// MCP 客户端未连接
    /// </summary>
    public const string McpClientNotConnected = "[MCP001] MCP 客户端未连接";

    /// <summary>
    /// 无法解析初始化响应
    /// </summary>
    public const string CannotParseInitializationResponse = "[MCP002] 无法解析初始化响应";

    /// <summary>
    /// 传输未运行
    /// </summary>
    public const string TransportNotRunning = "[TRN002] 传输未运行";

    /// <summary>
    /// 不支持远程客户端管理
    /// </summary>
    public const string RemoteClientManagementNotSupported = "[TRN003] LocalToolRegistry 不支持远程客户端管理";

    /// <summary>
    /// 同步委托未设置
    /// </summary>
    public const string SyncDelegateNotSet = "[PPL001] 同步委托未设置，请使用 ProceedAsync 方法";

    /// <summary>
    /// 方法执行委托未设置
    /// </summary>
    public const string MethodExecutionDelegateNotSet = "[PPL002] 方法执行委托未设置";

    /// <summary>
    /// 请使用异步拦截方法
    /// </summary>
    public const string UseAsyncInterceptMethod = "[PPL003] 请使用 InterceptMethodAsync 方法拦截异步方法";

    /// <summary>
    /// SSE 传输不支持
    /// </summary>
    public const string SseTransportNotSupported = "[SSE002] SSE transport is not yet supported";

    /// <summary>
    /// 无法获取有效的访问令牌
    /// </summary>
    public const string CannotGetValidAccessToken = "[AUT001] 无法获取有效的访问令牌";

    /// <summary>
    /// 未连接到服务器
    /// </summary>
    public const string NotConnectedToServer = "[MCP003] 未连接到服务器";

    /// <summary>
    /// 意外的流结束
    /// </summary>
    public const string UnexpectedEndOfStream = "[TRN004] Unexpected end of stream while reading message body.";

    /// <summary>
    /// 不支持直接发送消息
    /// </summary>
    public const string DirectMessageSendingNotSupported = "[TRN005] {0} 使用 SDK 内部传输机制，不支持直接发送消息";

    /// <summary>
    /// 传输未启动
    /// </summary>
    public const string TransportNotStarted = "[TRN006] 传输未启动";

    /// <summary>
    /// ApiKey 是必需的
    /// </summary>
    public const string ApiKeyRequired = "[AUT002] ApiKey is required for ApiKey auth";

    /// <summary>
    /// BearerToken 是必需的
    /// </summary>
    public const string BearerTokenRequired = "[AUT003] BearerToken is required for Bearer auth";

    /// <summary>
    /// 用户名是必需的
    /// </summary>
    public const string UsernameRequired = "[AUT004] Username is required for Basic auth";

    /// <summary>
    /// 密码是必需的
    /// </summary>
    public const string PasswordRequired = "[AUT005] Password is required for Basic auth";

    /// <summary>
    /// ClientId 是必需的
    /// </summary>
    public const string ClientIdRequired = "[AUT006] ClientId is required for OAuth2 auth";

    /// <summary>
    /// ClientSecret 是必需的
    /// </summary>
    public const string ClientSecretRequired = "[AUT007] ClientSecret is required for OAuth2 auth";

    /// <summary>
    /// TokenUrl 是必需的
    /// </summary>
    public const string TokenUrlRequired = "[AUT008] TokenUrl is required for OAuth2 auth";

    /// <summary>
    /// 列配置数量与值选择器数量不匹配
    /// </summary>
    public const string ColumnConfigCountMismatch = "[INF001] 列配置数量与值选择器数量不匹配";

    /// <summary>
    /// 无法解析 JSON-RPC 请求
    /// </summary>
    public const string CannotParseJsonRpcRequest = "[MCP004] 无法解析 JSON-RPC 请求";

    /// <summary>
    /// 无法解析 JSON-RPC 通知
    /// </summary>
    public const string CannotParseJsonRpcNotification = "[MCP005] 无法解析 JSON-RPC 通知";

    /// <summary>
    /// 无法解析 JSON-RPC 响应
    /// </summary>
    public const string CannotParseJsonRpcResponse = "[MCP006] 无法解析 JSON-RPC 响应";

    /// <summary>
    /// 未配置引用解析器
    /// </summary>
    public const string ReferenceResolverNotConfigured = "[CDI001] 未配置引用解析器，无法解析代码引用";

    /// <summary>
    /// 未找到 Git 仓库根目录
    /// </summary>
    public const string GitRepositoryRootNotFound = "[GIT001] 未找到 Git 仓库根目录";

    /// <summary>
    /// 命令不能为空
    /// </summary>
    public const string CommandCannotBeEmpty = "[INF002] 命令不能为空";

    /// <summary>
    /// 目录不存在
    /// </summary>
    public const string DirectoryNotFound = "[INF003] 目录不存在: {0}";

    /// <summary>
    /// 路径不存在
    /// </summary>
    public const string PathNotFound = "[INF004] 路径不存在: {0}";

    /// <summary>
    /// 代码不能为空
    /// </summary>
    public const string CodeCannotBeEmpty = "[CDI002] 代码不能为空";

    /// <summary>
    /// 代码长度超过限制
    /// </summary>
    public const string CodeLengthExceeded = "[CDI003] 代码长度超过限制 (最大 {0} 字符)";

    /// <summary>
    /// 任务不存在
    /// </summary>
    public const string TaskNotFound = "[INF005] 任务 {0} 不存在";

    /// <summary>
    /// 依赖任务不存在
    /// </summary>
    public const string DependencyTaskNotFound = "[INF006] 依赖任务 {0} 不存在";

    /// <summary>
    /// 依赖关系不存在
    /// </summary>
    public const string DependencyNotFound = "[INF007] 依赖关系 {0} 不存在";

    /// <summary>
    /// 待办事项不存在
    /// </summary>
    public const string TodoItemNotFound = "[INF008] 待办事项不存在";

    /// <summary>
    /// 未找到要替换的字符串
    /// </summary>
    public const string ReplacementStringNotFound = "[INF009] 未找到要替换的字符串";

    /// <summary>
    /// 参数不能为空
    /// </summary>
    public const string ArgumentCannotBeEmpty = "[INF010] {0} 不能为空";
}
