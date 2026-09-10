namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 插件包 ID — 自增铸造，格式 pkg-&lt;n&gt;
/// <para>对齐 DSH PackageId：nextPackageId 自增计数器</para>
/// </summary>
public readonly struct PluginPackageId : IEquatable<PluginPackageId>
{
    private readonly int _value;

    /// <summary>构造</summary>
    public PluginPackageId(int value) => _value = value;

    /// <summary>数值</summary>
    public int Value => _value;

    /// <inheritdoc/>
    public bool Equals(PluginPackageId other) => _value == other._value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PluginPackageId id && Equals(id);

    /// <inheritdoc/>
    public override int GetHashCode() => _value;

    /// <inheritdoc/>
    public override string ToString() => $"pkg-{_value}";

    /// <summary>相等</summary>
    public static bool operator ==(PluginPackageId left, PluginPackageId right) => left.Equals(right);

    /// <summary>不相等</summary>
    public static bool operator !=(PluginPackageId left, PluginPackageId right) => !left.Equals(right);
}

/// <summary>
/// 不可变插件包 — 对齐 DSH Package
/// <para>包含程序集路径、版本、入口方法、capability 声明</para>
/// <para>NativeAOT 降级：只支持已编译 DLL 路径，不支持源码求值</para>
/// </summary>
public sealed class PluginPackage
{
    /// <summary>包 ID</summary>
    public PluginPackageId PackageId { get; }

    /// <summary>插件名（唯一键）</summary>
    public string Name { get; }

    /// <summary>已编译程序集路径（DLL 绝对路径）</summary>
    public string AssemblyPath { get; }

    /// <summary>版本号</summary>
    public Version Version { get; }

    /// <summary>入口类型全名</summary>
    public string EntryType { get; }

    /// <summary>入口方法名</summary>
    public string EntryMethod { get; }

    /// <summary>capability 声明（该包要求访问的服务名集合）</summary>
    public FrozenSet<string> Capabilities { get; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>构造</summary>
    public PluginPackage(
        PluginPackageId packageId,
        string name,
        string assemblyPath,
        Version version,
        string entryType,
        string entryMethod,
        FrozenSet<string> capabilities,
        DateTimeOffset createdAt)
    {
        PackageId = packageId;
        Name = name;
        AssemblyPath = assemblyPath;
        Version = version;
        EntryType = entryType;
        EntryMethod = entryMethod;
        Capabilities = capabilities;
        CreatedAt = createdAt;
    }
}

/// <summary>
/// 动态插件运行状态
/// </summary>
public enum DynamicPluginState
{
    /// <summary>已定义，未运行</summary>
    Defined,

    /// <summary>运行中</summary>
    Running,

    /// <summary>已停止（可重新运行）</summary>
    Stopped,

    /// <summary>已卸载（永久删除）</summary>
    Undefined,
}

/// <summary>
/// invoke 失败码 — 对齐 DSH 4 类失败
/// </summary>
public enum PluginInvokeFailure
{
    /// <summary>插件未运行</summary>
    PluginNotRunning,

    /// <summary>运行版本已过期（UpdatePlugin 后旧引用失效）</summary>
    StaleRun,

    /// <summary>方法未找到</summary>
    MethodNotFound,

    /// <summary>handler 执行抛异常</summary>
    HandlerError,
}

/// <summary>
/// invoke 结果 — 成功返回值或失败码
/// </summary>
public readonly struct PluginInvokeResult
{
    /// <summary>是否成功</summary>
    public bool IsSuccess { get; }

    /// <summary>返回值（成功时）</summary>
    public object? Value { get; }

    /// <summary>失败码（失败时）</summary>
    public PluginInvokeFailure? Failure { get; }

    /// <summary>错误消息（失败时）</summary>
    public string? ErrorMessage { get; }

    private PluginInvokeResult(bool isSuccess, object? value, PluginInvokeFailure? failure, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        Failure = failure;
        ErrorMessage = errorMessage;
    }

    /// <summary>成功</summary>
    public static PluginInvokeResult Ok(object? value) => new(true, value, null, null);

    /// <summary>失败</summary>
    public static PluginInvokeResult Fail(PluginInvokeFailure failure, string? message = null)
        => new(false, null, failure, message);
}

/// <summary>
/// 运行中的插件实例 — 绑定 Package + ALC + handler 表
/// </summary>
internal sealed class RunningPluginInstance
{
    public PluginPackageId PackageId { get; }
    public PluginAlc Alc { get; }
    public FrozenDictionary<string, Func<object?[], object?>> Handlers { get; }
    public DateTimeOffset StartedAt { get; }

    public RunningPluginInstance(
        PluginPackageId packageId,
        PluginAlc alc,
        FrozenDictionary<string, Func<object?[], object?>> handlers,
        DateTimeOffset startedAt)
    {
        PackageId = packageId;
        Alc = alc;
        Handlers = handlers;
        StartedAt = startedAt;
    }
}

/// <summary>
/// 动态插件运行时注册表 — 对齐 DSH define/run/update/stop/undefine + inspect + invoke
/// <para>进程内存 Map，重启全部丢失（纯内存，不持久化）</para>
/// <para>DefinePlugin 声明包元数据，RunPlugin 加载程序集并激活，StopPlugin 只停运行，UndefinePlugin 永久删除</para>
/// <para>UpdatePlugin 原子替换包版本，旧运行实例标记 stale</para>
/// <para>审批前置：RunPlugin 前检查 PluginApprovalRegistry（依赖 #3）</para>
/// <para>NativeAOT 降级：不支持源码求值，只加载已编译 DLL</para>
/// <para>线程安全：ConcurrentDictionary + lock + Interlocked</para>
/// <para>时钟可注入：Func&lt;DateTimeOffset&gt;? clock = null，测试可控、生产用 UtcNow</para>
/// </summary>
public sealed class DynamicPluginRegistry
{
    private int _packageCounter;
    private readonly ConcurrentDictionary<string, PluginPackage> _packages = new();
    private readonly ConcurrentDictionary<string, RunningPluginInstance> _running = new();
    private readonly ConcurrentDictionary<string, DynamicPluginState> _states = new();
    private readonly PluginApprovalRegistry _approval;
    private readonly Func<DateTimeOffset>? _clock;
    private readonly Func<PluginPackage, PluginAlc, FrozenDictionary<string, Func<object?[], object?>>> _loader;
    private readonly object _runLock = new();

    /// <param name="approval">审批注册表（依赖 #3），null 时跳过审批</param>
    /// <param name="clock">时钟注入，null 用 DateTimeOffset.UtcNow</param>
    /// <param name="loader">handler 表加载委托，null 用默认 LoadHandlerTable（真实程序集加载）</param>
    public DynamicPluginRegistry(
        PluginApprovalRegistry? approval = null,
        Func<DateTimeOffset>? clock = null,
        Func<PluginPackage, PluginAlc, FrozenDictionary<string, Func<object?[], object?>>>? loader = null)
    {
        _approval = approval ?? new PluginApprovalRegistry(clock);
        _clock = clock;
        _loader = loader ?? LoadHandlerTable;
    }

    private DateTimeOffset Now() => _clock?.Invoke() ?? DateTimeOffset.UtcNow;

    /// <summary>铸造新包 ID — 自增递增</summary>
    public PluginPackageId NextPackageId() => new(Interlocked.Increment(ref _packageCounter));

    /// <summary>
    /// 声明插件包 — 注册元数据但不加载程序集
    /// <para>同名插件已存在且未 undefine → 抛 InvalidOperationException</para>
    /// </summary>
    public PluginPackage DefinePlugin(
        string name,
        string assemblyPath,
        Version version,
        string entryType,
        string entryMethod,
        FrozenSet<string>? capabilities = null)
    {
        var id = NextPackageId();
        var pkg = new PluginPackage(
            id, name, assemblyPath, version, entryType, entryMethod,
            capabilities ?? FrozenSet<string>.Empty, Now());

        if (!_packages.TryAdd(name, pkg))
        {
            throw new InvalidOperationException(
                $"[DYN-DEFINE-DUP] 插件 {name} 已定义，请先 UndefinePlugin 再重新定义。");
        }

        _states[name] = DynamicPluginState.Defined;
        return pkg;
    }

    /// <summary>
    /// 运行插件 — 加载程序集到新 ALC，激活 handler 表
    /// <para>审批前置：若 PluginApprovalRegistry 有该插件的待审批请求且未批准 → 抛 InvalidOperationException</para>
    /// <para>插件未定义 → 抛 KeyNotFoundException</para>
    /// <para>已在运行 → 返回当前运行实例的 PackageId（幂等）</para>
    /// </summary>
    public PluginPackageId RunPlugin(string name)
    {
        if (!_packages.TryGetValue(name, out var pkg))
            throw new KeyNotFoundException($"[DYN-RUN-UNDEFINED] 插件 {name} 未定义。");

        lock (_runLock)
        {
            if (_running.TryGetValue(name, out var existing))
                return existing.PackageId;

            var pending = _approval.PendingRequestFor(name);
            if (pending is not null && pending.State == ApprovalState.Pending)
            {
                throw new InvalidOperationException(
                    $"[DYN-RUN-PENDING-APPROVAL] 插件 {name} 有待审批请求 {pending.RequestId}，请先审批。");
            }

            var alc = new PluginAlc($"plugin-{name}-{pkg.PackageId}");
            var handlers = _loader(pkg, alc);
            var instance = new RunningPluginInstance(pkg.PackageId, alc, handlers, Now());
            _running[name] = instance;
            _states[name] = DynamicPluginState.Running;
            return pkg.PackageId;
        }
    }

    /// <summary>
    /// 更新插件 — 原子替换包版本，旧运行实例标记 stale（卸载旧 ALC）
    /// <para>插件未定义 → 抛 KeyNotFoundException</para>
    /// <para>新版本号 &lt;= 旧版本号 → 抛 ArgumentException</para>
    /// <para>若旧版本在运行，先 stop 再替换</para>
    /// </summary>
    public PluginPackage UpdatePlugin(
        string name,
        string assemblyPath,
        Version newVersion,
        string entryType,
        string entryMethod,
        FrozenSet<string>? capabilities = null)
    {
        if (!_packages.TryGetValue(name, out var oldPkg))
            throw new KeyNotFoundException($"[DYN-UPDATE-UNDEFINED] 插件 {name} 未定义。");

        if (newVersion <= oldPkg.Version)
            throw new ArgumentException(
                $"[DYN-UPDATE-VERSION] 新版本 {newVersion} 必须大于旧版本 {oldPkg.Version}。");

        var wasRunning = _running.TryRemove(name, out var oldInstance);
        if (wasRunning)
        {
            oldInstance!.Alc.Unload();
        }

        var id = NextPackageId();
        var newPkg = new PluginPackage(
            id, name, assemblyPath, newVersion, entryType, entryMethod,
            capabilities ?? FrozenSet<string>.Empty, Now());

        _packages[name] = newPkg;
        _states[name] = wasRunning ? DynamicPluginState.Stopped : DynamicPluginState.Defined;
        return newPkg;
    }

    /// <summary>
    /// 停止插件 — 只停运行，不删除定义（可重新 RunPlugin）
    /// <para>插件未运行 → 返回 false（幂等）</para>
    /// </summary>
    public bool StopPlugin(string name)
    {
        if (!_running.TryRemove(name, out var instance))
        {
            if (_states.TryGetValue(name, out var state) && state == DynamicPluginState.Stopped)
                return false;
            return false;
        }

        instance.Alc.Unload();
        _states[name] = DynamicPluginState.Stopped;
        return true;
    }

    /// <summary>
    /// 永久删除插件 — undefine，从内存移除包定义
    /// <para>若在运行，先 stop 再删除</para>
    /// <para>插件未定义 → 返回 false</para>
    /// </summary>
    public bool UndefinePlugin(string name)
    {
        if (_running.TryRemove(name, out var instance))
            instance.Alc.Unload();

        var removed = _packages.TryRemove(name, out _);
        _states.TryRemove(name, out _);
        _running.TryRemove(name, out _);
        return removed;
    }

    /// <summary>
    /// 查看插件状态
    /// </summary>
    public DynamicPluginState GetState(string name)
    {
        return _states.GetValueOrDefault(name, DynamicPluginState.Undefined);
    }

    /// <summary>
    /// 查看插件包定义（inspect provider — 运行时自省）
    /// <para>插件未定义 → 返回 null</para>
    /// </summary>
    public PluginPackage? InspectPlugin(string name)
    {
        return _packages.GetValueOrDefault(name);
    }

    /// <summary>
    /// 列出所有已定义插件名（inspect provider — 运行时自省）
    /// </summary>
    public IReadOnlyList<string> ListPlugins()
    {
        return _packages.Keys.ToArray();
    }

    /// <summary>
    /// 获取当前运行实例的包 ID（inspect provider）
    /// <para>未运行 → 返回 null</para>
    /// </summary>
    public PluginPackageId? GetRunningPackageId(string name)
    {
        return _running.TryGetValue(name, out var instance) ? instance.PackageId : null;
    }

    /// <summary>
    /// invoke handler — host.call(method, args) 路由
    /// <para>4 类失败码：PluginNotRunning / StaleRun / MethodNotFound / HandlerError</para>
    /// <para>expectedPackageId 不匹配当前运行实例 → StaleRun（UpdatePlugin 后旧引用失效）</para>
    /// </summary>
    public PluginInvokeResult Invoke(string name, string method, object?[] args, PluginPackageId? expectedPackageId = null)
    {
        if (!_running.TryGetValue(name, out var instance))
            return PluginInvokeResult.Fail(PluginInvokeFailure.PluginNotRunning, $"插件 {name} 未运行。");

        if (expectedPackageId.HasValue && expectedPackageId.Value != instance.PackageId)
            return PluginInvokeResult.Fail(PluginInvokeFailure.StaleRun,
                $"期望包 {expectedPackageId.Value} 已过期，当前运行包 {instance.PackageId}。");

        if (!instance.Handlers.TryGetValue(method, out var handler))
            return PluginInvokeResult.Fail(PluginInvokeFailure.MethodNotFound,
                $"插件 {name} 无方法 {method}。");

        try
        {
            var result = handler(args);
            return PluginInvokeResult.Ok(result);
        }
        catch (Exception ex)
        {
            return PluginInvokeResult.Fail(PluginInvokeFailure.HandlerError, ex.Message);
        }
    }

    /// <summary>
    /// 加载 handler 表 — 从程序集加载入口类型，提取 [ServiceInvoke] 标记的方法
    /// <para>NativeAOT 降级：实际程序集加载由 PluginAlc 负责，此处返回空表（无反射）</para>
    /// <para>真实实现需源码生成器在编译时为每个插件生成 handler 注册代码</para>
    /// <para>ADR 0098 #8 AOT 降级：LoadFromAssemblyPath 不兼容 trimming，仅非 AOT 模式可用</para>
    /// </summary>
    private static FrozenDictionary<string, Func<object?[], object?>> LoadHandlerTable(PluginPackage pkg, PluginAlc alc)
    {
#pragma warning disable IL2026
        _ = alc.LoadFromAssemblyPath(pkg.AssemblyPath);
#pragma warning restore IL2026
        return FrozenDictionary<string, Func<object?[], object?>>.Empty;
    }
}
