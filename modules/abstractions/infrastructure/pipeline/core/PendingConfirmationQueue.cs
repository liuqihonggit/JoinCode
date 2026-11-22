namespace Infrastructure.Pipeline;

/// <summary>
/// 后台待确认队列实现 — 与 <see cref="AgentNotificationQueue"/> 同样按目标代理分桶；
/// 后台只登记票据并异步等待，前台通过 <see cref="Snapshot"/> 批量展示后 <see cref="TryResolve"/> 结算。
/// <para>票据带默认 30 分钟有效期与 200 条上限：后台可能长期无人值守，票据不能无限堆积；
/// 过期条目在下次访问时惰性清理（先完成等待再移除），不引入后台定时器线程。</para>
/// <para>登记时对参数做脱敏与截断，票据会直接投影到 Web/TUI 界面，不能携带密钥或超长正文。</para>
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.IPendingConfirmationQueue), ServiceLifetime.Singleton)]
public sealed partial class PendingConfirmationQueue : ServiceEntity, JoinCode.Abstractions.Interfaces.IPendingConfirmationQueue {
    /// <summary>默认票据有效期 — 覆盖一轮无人值守任务后在下次前台交互内可被看到。</summary>
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    /// <summary>默认票据上限 — 超过后丢弃最旧票据，避免长时间运行内存无界增长。</summary>
    private const int DefaultMaxEntries = 200;

    /// <summary>单条字符串参数展示上限（字符）。</summary>
    private const int MaxArgumentTextLength = 512;

    /// <summary>单张票据最多保留的参数键数。</summary>
    private const int MaxArgumentKeys = 32;

    /// <summary>参数键名展示上限（字符）。</summary>
    private const int MaxArgumentKeyLength = 64;

    /// <summary>参数值中的内联密钥模式（<c>token=...</c>、<c>password: ...</c> 等）。</summary>
    private static readonly Regex InlineSecretPattern = new(
        @"(?i)(token|secret|password|passwd|api[_-]?key)\s*[=:]\s*[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>请求头里的凭据整段模式（<c>Authorization: Bearer ...</c>、<c>Cookie: ...</c>）。</summary>
    private static readonly Regex HeaderSecretPattern = new(
        @"(?i)(authorization|cookie)\s*[=:]\s*[^\r\n,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ConcurrentDictionary<string, Entry> _pending = new(StringComparer.Ordinal);
    private readonly ILogger<PendingConfirmationQueue>? _logger;
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;

    private sealed record Entry(
        JoinCode.Abstractions.Interfaces.PendingConfirmationItem Item,
        TaskCompletionSource<JoinCode.Abstractions.Interfaces.PendingConfirmationDecision> Completion);

    /// <summary>创建待确认队列（默认 30 分钟有效期、200 条上限）。</summary>
    /// <param name="logger">可选日志器。</param>
    public PendingConfirmationQueue(ILogger<PendingConfirmationQueue>? logger = null)
        : this(DefaultTtl, DefaultMaxEntries, logger) {
    }

    /// <summary>测试/定制入口 — 显式指定有效期与上限。</summary>
    /// <param name="ttl">票据有效期；必须为正。</param>
    /// <param name="maxEntries">票据上限；必须为正。</param>
    /// <param name="logger">可选日志器。</param>
    internal PendingConfirmationQueue(TimeSpan ttl, int maxEntries, ILogger<PendingConfirmationQueue>? logger = null) {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        _ttl = ttl;
        _maxEntries = maxEntries;
        _logger = logger;
    }

    /// <inheritdoc/>
    public JoinCode.Abstractions.Interfaces.PendingConfirmationItem Enqueue(
        string? agentId,
        string toolName,
        string reason,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> arguments) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(arguments);

        PurgeExpired();
        EvictOldestWhenFull();

        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JoinCode.Abstractions.Interfaces.PendingConfirmationDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new JoinCode.Abstractions.Interfaces.PendingConfirmationItem {
            Id = id,
            AgentId = agentId,
            ToolName = toolName,
            Reason = TruncateText(reason),
            Arguments = SanitizeArguments(arguments),
            Decision = completion.Task,
        };

        _pending[id] = new Entry(item, completion);
        _logger?.LogDebug("[PendingConfirmationQueue] 登记: id={Id}, agent={Agent}, tool={Tool}", id, agentId ?? "(main)", toolName);
        Diag.WriteLine($"[PENDING-CONFIRM-QUEUE] enqueue id={id} agent={agentId ?? "(main)"} tool={toolName}");
        return item;
    }

    /// <inheritdoc/>
    public IReadOnlyList<JoinCode.Abstractions.Interfaces.PendingConfirmationItem> Snapshot() {
        PurgeExpired();
        return [.. _pending.Values.Select(static entry => entry.Item).OrderBy(static item => item.CreatedAtUtc)];
    }

    /// <inheritdoc/>
    public bool TryResolve(string id, JoinCode.Abstractions.Interfaces.PendingConfirmationDecision decision) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        // Defer = 后台已让出等待，但票据仍保留给前台展示；Approve/Deny 才真正结算并移除。
        if (decision == JoinCode.Abstractions.Interfaces.PendingConfirmationDecision.Defer) {
            if (!_pending.TryGetValue(id, out var deferred)) {
                return false;
            }

            var completed = deferred.Completion.TrySetResult(decision);
            if (completed) {
                Diag.WriteLine($"[PENDING-CONFIRM-QUEUE] defer id={id} (kept for foreground)");
            }
            return completed;
        }

        if (!_pending.TryGetValue(id, out var candidate)) {
            return false;
        }
        if (IsExpired(candidate.Item)) {
            if (_pending.TryRemove(id, out var expired)) {
                expired.Completion.TrySetResult(JoinCode.Abstractions.Interfaces.PendingConfirmationDecision.Defer);
                Diag.WriteLine($"[PENDING-CONFIRM-QUEUE] expired id={id} (dropped on resolve)");
            }
            return false;
        }
        if (!_pending.TryRemove(id, out var entry)) {
            return false;
        }

        entry.Completion.TrySetResult(decision);
        _logger?.LogDebug("[PendingConfirmationQueue] 结算: id={Id}, decision={Decision}", id, decision);
        Diag.WriteLine($"[PENDING-CONFIRM-QUEUE] resolve id={id} decision={decision}");
        return true;
    }

    /// <inheritdoc/>
    public bool HasPending {
        get {
            PurgeExpired();
            return !_pending.IsEmpty;
        }
    }

    /// <summary>清理超过有效期的票据：先完成等待（Defer），再移除；前台不再看到过期项。</summary>
    private void PurgeExpired() {
        var expired = _pending
            .Where(pair => IsExpired(pair.Value.Item))
            .Select(pair => pair.Key)
            .ToList();
        foreach (var id in expired) {
            if (_pending.TryRemove(id, out var removed)) {
                removed.Completion.TrySetResult(JoinCode.Abstractions.Interfaces.PendingConfirmationDecision.Defer);
                Diag.WriteLine($"[PENDING-CONFIRM-QUEUE] expired id={id} tool={removed.Item.ToolName}");
            }
        }
    }

    /// <summary>超过上限时丢弃最旧票据（同样先完成等待），保证内存有界。</summary>
    private void EvictOldestWhenFull() {
        while (_pending.Count >= _maxEntries) {
            var oldest = _pending
                .OrderBy(pair => pair.Value.Item.CreatedAtUtc)
                .Select(pair => pair.Key)
                .FirstOrDefault();
            if (oldest is null || !_pending.TryRemove(oldest, out var removed)) {
                return;
            }
            removed.Completion.TrySetResult(JoinCode.Abstractions.Interfaces.PendingConfirmationDecision.Defer);
            Diag.WriteLine($"[PENDING-CONFIRM-QUEUE] evicted id={oldest} tool={removed.Item.ToolName} (capacity)");
        }
    }

    /// <summary>票据是否已超过有效期。</summary>
    private bool IsExpired(JoinCode.Abstractions.Interfaces.PendingConfirmationItem item) =>
        DateTime.UtcNow - item.CreatedAtUtc >= _ttl;

    /// <summary>
    /// 参数脱敏：敏感键整值替换、所有字符串折叠换行并截断、内联密钥模式替换、限制键数量。
    /// 票据只用于界面展示，不需要保留完整正文；原始参数仍在执行器上下文内。
    /// </summary>
    private static IReadOnlyDictionary<string, System.Text.Json.JsonElement> SanitizeArguments(
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> arguments) {
        if (arguments.Count == 0) {
            return FrozenDictionary<string, System.Text.Json.JsonElement>.Empty;
        }

        var sanitized = new Dictionary<string, System.Text.Json.JsonElement>(Math.Min(arguments.Count, MaxArgumentKeys), StringComparer.Ordinal);
        foreach (var (key, value) in arguments) {
            if (sanitized.Count >= MaxArgumentKeys) {
                break;
            }
            sanitized[TruncateText(key)] = IsSensitiveKey(key)
                ? System.Text.Json.JsonSerializer.SerializeToElement("[REDACTED]", PendingConfirmationJsonContext.Default.String)
                : SanitizeValue(value);
        }
        return sanitized.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>参数值脱敏：字符串截断 + 内联密钥替换；复杂结构降级为截断后的原文字符串。</summary>
    private static System.Text.Json.JsonElement SanitizeValue(System.Text.Json.JsonElement value) {
        return value.ValueKind switch {
            System.Text.Json.JsonValueKind.String => System.Text.Json.JsonSerializer.SerializeToElement(
                TruncateText(RedactInlineSecrets(value.GetString() ?? string.Empty)),
                PendingConfirmationJsonContext.Default.String),
            System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined =>
                System.Text.Json.JsonSerializer.SerializeToElement(string.Empty, PendingConfirmationJsonContext.Default.String),
            System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => value.Clone(),
            _ => System.Text.Json.JsonSerializer.SerializeToElement(
                TruncateText(value.GetRawText()),
                PendingConfirmationJsonContext.Default.String),
        };
    }

    /// <summary>键名是否表示凭据类参数（大小写与分隔符不敏感）。</summary>
    private static bool IsSensitiveKey(string key) {
        var normalized = key.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("passwd", StringComparison.Ordinal)
            || normalized.Contains("apikey", StringComparison.Ordinal)
            || normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized.Contains("cookie", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal)
            || normalized.Contains("privatekey", StringComparison.Ordinal)
            || normalized.Contains("accesskey", StringComparison.Ordinal);
    }

    /// <summary>把 <c>token=xxx</c> 这类内联密钥替换为 <c>token=[REDACTED]</c>；请求头整段替换。</summary>
    private static string RedactInlineSecrets(string text) =>
        HeaderSecretPattern.Replace(
            InlineSecretPattern.Replace(text, static match => $"{match.Groups[1].Value}=[REDACTED]"),
            static match => $"{match.Groups[1].Value}=[REDACTED]");

    /// <summary>折叠换行并按展示上限截断。</summary>
    private static string TruncateText(string text) {
        if (string.IsNullOrEmpty(text)) {
            return string.Empty;
        }
        var collapsed = text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return collapsed.Length <= MaxArgumentTextLength
            ? collapsed
            : $"{collapsed[..MaxArgumentTextLength]}…[truncated {collapsed.Length - MaxArgumentTextLength} chars]";
    }

    /// <summary>释放队列 — 未结算票据统一 Defer，避免后台等待任务悬挂。</summary>
    public override void Dispose() {
        foreach (var (id, entry) in _pending) {
            if (_pending.TryRemove(id, out var removed)) {
                removed.Completion.TrySetResult(JoinCode.Abstractions.Interfaces.PendingConfirmationDecision.Defer);
            }
        }
        base.Dispose();
    }
}

/// <summary>待确认队列参数脱敏的 JSON 序列化上下文（仅字符串，AOT 安全）。</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(string))]
internal sealed partial class PendingConfirmationJsonContext : JsonSerializerContext;
