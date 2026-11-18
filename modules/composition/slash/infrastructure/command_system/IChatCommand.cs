namespace JoinCode.ChatCommands;

/// <summary>
/// 命令服务容器扩展 — 从 ChatCommandContext.Services (IServiceProvider) 获取强类型 CommandServices
/// </summary>
public static class ChatCommandContextExtensions {
    /// <summary>
    /// 从 DI 容器获取 CommandServices 强类型服务包
    /// </summary>
    public static CommandServices GetCommandServices(this ChatCommandContext context) {
        return context.Services.GetService<CommandServices>()
            ?? throw new InvalidOperationException("CommandServices 未注册到 DI 容器");
    }

    /// <summary>
    /// 尝试获取 CommandServices — 未注册时返回 null（用于 ?. 模式）
    /// </summary>
    public static CommandServices? TryGetCommandServices(this ChatCommandContext context) {
        return context.Services.GetService<CommandServices>();
    }
}

/// <summary>
/// 聊天命令抽象基类 — 从 ChatCommandAttribute 特性读取命令元数据（名称、描述、用法、别名等），
/// 子类只需 override ExecuteAsync 实现具体逻辑，可选 override 各属性实现动态门控
/// </summary>
public abstract class ChatCommandBase : IChatCommand {
    private readonly ChatCommandAttribute? _attr;

    /// <summary>
    /// 构造 — 反射读取类型上的 ChatCommandAttribute 特性缓存到 _attr
    /// </summary>
    protected ChatCommandBase() {
        _attr = GetType().GetCustomAttributes(typeof(ChatCommandAttribute), false).Cast<ChatCommandAttribute>().FirstOrDefault();
    }

    /// <summary>命令名称 — 从特性读取</summary>
    public virtual string Name => _attr?.Name ?? string.Empty;
    /// <summary>命令描述 — 从特性读取</summary>
    public virtual string Description => _attr?.Description ?? string.Empty;
    /// <summary>命令用法 — 从特性读取</summary>
    public virtual string Usage => _attr?.Usage ?? string.Empty;
    /// <summary>命令别名 — 从特性读取</summary>
    public virtual string[] Aliases => _attr?.Aliases ?? [];
    /// <summary>参数提示文本 — 从特性读取</summary>
    public virtual string ArgumentHint => _attr?.ArgumentHint ?? string.Empty;
    /// <summary>是否隐藏命令 — 从特性读取</summary>
    public virtual bool IsHidden => _attr?.IsHidden ?? false;

    /// <summary>
    /// 是否暴露给 MCP（AI 可调用）— 从特性读取
    /// </summary>
    public virtual bool ExposeToMcp => _attr?.ExposeToMcp ?? false;

    /// <summary>
    /// ToolKind — 从特性读取，决定注入策略
    /// </summary>
    public virtual ToolKind Kind => _attr?.Kind ?? ToolKind.Slash;

    /// <summary>
    /// 命令是否当前可用 — 对齐 TS CommandBase.isEnabled()
    /// 默认从特性读取，子类可 override 实现动态门控
    /// </summary>
    public virtual bool IsEnabled => _attr?.IsEnabled ?? true;

    /// <summary>
    /// 执行命令 — 子类实现具体逻辑
    /// </summary>
    /// <param name="context">命令执行上下文</param>
    /// <returns>命令执行结果</returns>
    public abstract Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context);

    /// <summary>
    /// 从 ServiceProvider 获取服务，未注册时输出错误并返回 null
    /// </summary>
    internal static T? GetService<T>(ChatCommandContext context) where T : class {
        var service = context.Services.GetService<T>();
        if (service is null && !TerminalHelper.IsInputRedirected) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{typeof(T).Name} 服务未初始化{AnsiStyleEnumConstants.Reset}");
        }
        return service;
    }

    /// <summary>
    /// 从 ServiceProvider 获取服务（非泛型版本），未注册时输出错误并返回 null
    /// </summary>
    internal static T? GetService<T>(ChatCommandContext context, Type serviceType) where T : class {
        var service = context.Services.GetService(serviceType) as T;
        if (service is null && !TerminalHelper.IsInputRedirected) {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{serviceType.Name} 服务未初始化{AnsiStyleEnumConstants.Reset}");
        }
        return service;
    }

    /// <summary>
    /// 统一错误处理
    /// </summary>
    internal static void HandleError(string operation, Exception ex) {
        TerminalHelper.WriteLine($"{TerminalColors.Error}{operation}失败: {ex.Message}{AnsiStyleEnumConstants.Reset}");
    }

    /// <summary>
    /// 获取标准化参数文本。
    /// <para><c>slash_call</c> 把 key=value / JSON 规范化成 JSON 对象串放进 <see cref="ChatCommandContext.Arguments"/>，
    /// 若直接 Trim 会把整串 <c>{"vendor":"openai"}</c> 当成参数值（如 /vendor 报「未知供应商」）。
    /// 这里对 JSON 对象复用 <see cref="GetSplitArgs"/> 抽值后拼回「还原后的原始文本」，与 TUI 手输形态一致；
    /// 非 JSON 仍走原来的 Trim，避免影响位置参数命令。多段命令据此可自行 Split，不必逐命令改。</para>
    /// </summary>
    internal static string GetNormalizedArgs(ChatCommandContext context) {
        var raw = context.Arguments.Trim();
        if (raw.StartsWith('{') && raw.EndsWith('}')) {
            var values = GetSplitArgs(context);
            if (values.Length > 0) return string.Join(" ", values);
        }
        return raw;
    }

    /// <summary>
    /// 获取拆分后的参数数组。
    /// <para><c>slash_call</c> 把 key=value / JSON 都规范化成 JSON 字符串放进 <see cref="ChatCommandContext.Arguments"/>，
    /// 只按空格 Split 会把 <c>{"flags":"-p"}</c> 当成一个整体参数，命令里的 ParseFlags 永远匹配不到。
    /// 这里对「以 { 开头的动态参数对象」提取各字段值（ADR 0133 允许 JsonDocument 做动态字段提取），
    /// 其他形态仍走原来的空格拆分，避免影响既有位置参数命令。</para>
    /// </summary>
    internal static string[] GetSplitArgs(ChatCommandContext context) {
        var raw = context.Arguments.Trim();
        if (raw.StartsWith('{') && raw.EndsWith('}')) {
            try {
                using var doc = JsonDocument.Parse(raw);
                var values = new List<string>();
                foreach (var property in doc.RootElement.EnumerateObject()) {
                    var value = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.ToString();
                    if (!string.IsNullOrEmpty(value)) values.Add(value);
                }
                if (values.Count > 0) return values.ToArray();
            } catch (JsonException ex) {
                // 非标准 JSON 交给原来的拆分路径，由命令自己报错并给出正确写法。
                Diag.WriteLine($"[SLASH-ARGS] 参数不是合法 JSON，回退空格拆分: {ex.Message}");
            }
        }
        return raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}