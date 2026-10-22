namespace JoinCode.Gui.WebHost;

/// <summary>
/// 消息反馈存储（TASK066 C4）：DSH 前端对助手消息的赞/踩与说明落盘到
/// <c>~/.jcc/dsh-message-feedback.json</c>，按「会话 → 消息」两层索引。
/// 版本号是不透明字符串，前端原样回传做乐观并发；冲突时返回当前条目（可能为 null），
/// 与 DSH 契约 <c>{ok:false,error:{code:"version-conflict",current}}</c> 对齐。
/// </summary>
internal sealed class DshMessageFeedbackStore {
    /// <summary>备注 UTF-8 字节上限（DSH note-too-large 阈值）</summary>
    internal const int MaxNoteBytes = 4096;

    private readonly IFileSystem _fs;
    private readonly string _filePath;
    private readonly Lock Gate = new();
    private readonly Dictionary<string, Dictionary<string, DshMessageFeedbackItem>> Sessions = new(StringComparer.Ordinal);
    private Task? _loading;
    private bool _loaded;

    /// <summary>构造反馈存储</summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="filePath">落盘路径（默认 <c>~/.jcc/dsh-message-feedback.json</c>）</param>
    internal DshMessageFeedbackStore(IFileSystem fs, string? filePath = null) {
        _fs = fs;
        _filePath = filePath ?? Path.Combine(AppDataPaths.Default.JccDirectory, "dsh-message-feedback.json");
    }

    /// <summary>启动期加载存档（单飞幂等；文件缺失/损坏按空文档启动，不阻塞反馈面板）</summary>
    internal Task InitializeAsync(CancellationToken cancellationToken = default) {
        lock (Gate) {
            if (_loaded) {
                return Task.CompletedTask;
            }
            return _loading ??= LoadCoreAsync(cancellationToken);
        }
    }

    /// <summary>读取存档并在锁内合并（失败只记日志，内存为空文档）</summary>
    private async Task LoadCoreAsync(CancellationToken cancellationToken) {
        DshMessageFeedbackDocument? document = null;
        try {
            if (_fs.FileExists(_filePath)) {
                var json = await _fs.ReadAllTextAsync(_filePath, cancellationToken);
                document = RelaxedJsonSerializer.Deserialize(json, DshJsonContext.Default.DshMessageFeedbackDocument);
            }
        } catch (Exception ex) {
            GuiDiag.Log($"[DSH-FEEDBACK] load failed: {ex.Message}");
        }
        MergeLoaded(document);
    }

    /// <summary>把已读存档合并进内存（同步锁内执行，避免 async 方法持有 lock）</summary>
    /// <param name="document">已反序列化文档（读取失败为 null）</param>
    private void MergeLoaded(DshMessageFeedbackDocument? document) {
        lock (Gate) {
            if (!_loaded && document is not null) {
                foreach (var (sessionId, items) in document.Sessions) {
                    var target = Sessions.TryGetValue(sessionId, out var existing)
                        ? existing
                        : Sessions[sessionId] = new Dictionary<string, DshMessageFeedbackItem>(StringComparer.Ordinal);
                    foreach (var (messageId, item) in items) {
                        target[messageId] = item;
                    }
                }
            }
            _loaded = true;
            _loading = null;
        }
    }

    /// <summary>列出某会话的全部反馈（按创建时间升序副本）</summary>
    internal IReadOnlyList<DshMessageFeedbackItem> List(string sessionId) {
        lock (Gate) {
            return Sessions.TryGetValue(sessionId, out var items)
                ? [.. items.Values.OrderBy(item => item.CreatedAt).Select(Clone)]
                : [];
        }
    }

    /// <summary>读取单条反馈（不存在返回 null）</summary>
    internal DshMessageFeedbackItem? Get(string sessionId, string messageId) {
        lock (Gate) {
            return Sessions.TryGetValue(sessionId, out var items) && items.TryGetValue(messageId, out var item)
                ? Clone(item)
                : null;
        }
    }

    /// <summary>
    /// 写入/替换一条反馈：<paramref name="expectedVersion"/> 为 null 表示期望不存在，
    /// 非 null 必须等于当前版本；不一致返回 <see cref="DshFeedbackPutOutcome.Saved"/> 为 null 的冲突结果。
    /// </summary>
    internal DshFeedbackPutOutcome Put(
        string sessionId,
        string messageId,
        string rating,
        string? note,
        string? category,
        string? expectedVersion) {
        lock (Gate) {
            var items = Sessions.TryGetValue(sessionId, out var existing)
                ? existing
                : Sessions[sessionId] = new Dictionary<string, DshMessageFeedbackItem>(StringComparer.Ordinal);
            items.TryGetValue(messageId, out var current);
            if (expectedVersion is null) {
                if (current is not null) {
                    return new DshFeedbackPutOutcome(Clone(current), null);
                }
            } else if (current is null || !string.Equals(current.Version, expectedVersion, StringComparison.Ordinal)) {
                return new DshFeedbackPutOutcome(current is null ? null : Clone(current), null);
            }
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var nextVersion = current is null ? 1 : ParseVersion(current.Version) + 1;
            var saved = new DshMessageFeedbackItem {
                MessageId = messageId,
                Rating = rating,
                Note = note,
                Category = category,
                Version = nextVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CreatedAt = current?.CreatedAt ?? now,
                UpdatedAt = now,
            };
            items[messageId] = saved;
            PersistLocked();
            return new DshFeedbackPutOutcome(current is null ? null : Clone(current), Clone(saved));
        }
    }

    /// <summary>删除一条反馈：版本一致才删除；不一致返回当前条目（可能为 null）</summary>
    internal DshFeedbackDeleteOutcome Delete(string sessionId, string messageId, string? expectedVersion) {
        lock (Gate) {
            if (!Sessions.TryGetValue(sessionId, out var items) || !items.TryGetValue(messageId, out var current)) {
                return new DshFeedbackDeleteOutcome(null, false);
            }
            if (!string.Equals(current.Version, expectedVersion, StringComparison.Ordinal)) {
                return new DshFeedbackDeleteOutcome(Clone(current), false);
            }
            items.Remove(messageId);
            if (items.Count == 0) {
                Sessions.Remove(sessionId);
            }
            PersistLocked();
            return new DshFeedbackDeleteOutcome(Clone(current), true);
        }
    }

    /// <summary>解析版本号（损坏存档按 0 处理，下一次写入自然升级）</summary>
    private static long ParseVersion(string version) =>
        long.TryParse(version, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    /// <summary>返回副本，避免调用方改写内存状态</summary>
    private static DshMessageFeedbackItem Clone(DshMessageFeedbackItem item) => new() {
        MessageId = item.MessageId,
        Rating = item.Rating,
        Note = item.Note,
        Category = item.Category,
        Version = item.Version,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt,
    };

    /// <summary>全量落盘（调用方必须持锁；失败只记日志，内存状态保留）</summary>
    private void PersistLocked() {
        try {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !_fs.DirectoryExists(directory)) {
                _fs.CreateDirectory(directory);
            }
            var document = new DshMessageFeedbackDocument {
                Sessions = Sessions.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToDictionary(entry => entry.Key, entry => Clone(entry.Value), StringComparer.Ordinal),
                    StringComparer.Ordinal),
            };
            var json = RelaxedJsonSerializer.Serialize(document, DshJsonContext.Default);
            _ = _fs.WriteAllText(_filePath, json);
        } catch (Exception ex) {
            GuiDiag.Log($"[DSH-FEEDBACK] persist failed: {ex.Message}");
        }
    }
}

/// <summary>写入结果：<see cref="Saved"/> 为 null 表示版本冲突，<see cref="Current"/> 为冲突时落盘值</summary>
internal sealed record DshFeedbackPutOutcome(DshMessageFeedbackItem? Current, DshMessageFeedbackItem? Saved);

/// <summary>删除结果：<see cref="Removed"/> 为 false 表示版本冲突/目标不存在，<see cref="Current"/> 为当前落盘值</summary>
internal sealed record DshFeedbackDeleteOutcome(DshMessageFeedbackItem? Current, bool Removed);
