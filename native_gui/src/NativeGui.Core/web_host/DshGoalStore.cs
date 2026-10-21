namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 目标存储（TASK058 P2-B5）：每会话一个目标（GoalBar 语义），七方法状态机 + revision CAS +
/// roundsStarted 轮次计数（DshTurnRunner 受理钩子递增）+ activation 翻转事件（GoalChanged →
/// 宿主桥 $events waterfall 帧给前端 goal/activation-changed）。持久化 <c>~/.jcc/dsh_goals.json</c>。
/// <para>v1 宿主数据闭环：目标 CRUD/状态机/计数/事件全真；引擎节点图 goal 系统联动（每 N 轮自动
/// evaluate）属深化项，另行立项对齐。</para>
/// </summary>
internal sealed class DshGoalStore(IFileSystem fs, string? filePath = null) {
    private readonly string _filePath = filePath
        ?? Path.Combine(AppDataPaths.Default.JccDirectory, "dsh_goals.json");
    private readonly object _gate = new();
    private Dictionary<string, DshGoalDocument> _goals = new(StringComparer.Ordinal);

    /// <summary>activation 翻转事件（sessionId + 目标快照）— 宿主桥 $events 帧用</summary>
    internal event Action<string, DshGoalDocument>? GoalChanged;

    private bool _loaded;

    /// <summary>读会话目标（无目标返回 null）</summary>
    internal DshGoal? Get(string sessionId) {
        lock (_gate) {
            EnsureLoaded();
            return _goals.TryGetValue(sessionId, out var doc) ? Project(doc) : null;
        }
    }

    /// <summary>创建目标（armed/active，revision=1）；已存在目标返回 conflict 引导</summary>
    internal (DshGoalRef? Ref, DshRpcResult? Error) Create(string sessionId, string objective, int? maxGoalRounds) {
        if (string.IsNullOrWhiteSpace(objective)) {
            return (null, DshRpcResults.Fail("goals/rejected", "goals/create 需要非空 objective；在目标栏输入目标后重试"));
        }
        lock (_gate) {
            EnsureLoaded();
            if (_goals.ContainsKey(sessionId)) {
                var current = _goals[sessionId].Revision;
                return (null, DshRpcResults.Fail("goals/conflict",
                    $"会话 {sessionId} 已有目标（revision={current}）；先 clear 再创建新目标"));
            }
            var doc = new DshGoalDocument {
                Id = Guid.NewGuid().ToString("N"),
                Revision = 1,
                Objective = objective,
                MaxGoalRounds = maxGoalRounds ?? DefaultMaxGoalRounds,
                Phase = "active",
                Activation = "armed",
                RoundsStarted = 0,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            _goals[sessionId] = doc;
            Persist();
            return (new DshGoalRef { Id = doc.Id, Revision = doc.Revision }, null);
        }
    }

    /// <summary>编辑目标（CAS：expectedRevision 不匹配返回 conflict）；objective/maxGoalRounds 可改</summary>
    internal (DshGoal? Goal, DshRpcResult? Error) Edit(string sessionId, DshGoalRef reference, string? objective, int? maxGoalRounds) {
        lock (_gate) {
            EnsureLoaded();
            if (!_goals.TryGetValue(sessionId, out var doc) || doc.Id != reference.Id) {
                return (null, DshRpcResults.Fail("goals/not-found", $"会话 {sessionId} 无目标 {reference.Id}；先 goals/get 确认再操作"));
            }
            if (doc.Revision != reference.Revision) {
                return (null, DshRpcResults.Fail("goals/conflict",
                    $"目标版本 {reference.Revision} 已过期（当前 {doc.Revision}）；刷新目标栏后用新 revision 重试"));
            }
            if (doc.Phase == "complete") {
                return (null, DshRpcResults.Fail("goals/rejected", "目标已完成，不可编辑；clear 后创建新目标"));
            }
            if (!string.IsNullOrWhiteSpace(objective)) {
                doc = doc with { Objective = objective };
            }
            if (maxGoalRounds is { } rounds) {
                doc = doc with { MaxGoalRounds = rounds };
            }
            doc = doc with { Revision = doc.Revision + 1, UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            _goals[sessionId] = doc;
            Persist();
            return (Project(doc), null);
        }
    }

    /// <summary>状态迁移（pause→disarmed/resume→armed/complete→disarmed；均 CAS）— activation 翻转触发 GoalChanged</summary>
    private (DshGoal? Goal, DshRpcResult? Error) Transition(string sessionId, DshGoalRef reference, string phase, string activation) {
        lock (_gate) {
            EnsureLoaded();
            if (!_goals.TryGetValue(sessionId, out var doc) || doc.Id != reference.Id) {
                return (null, DshRpcResults.Fail("goals/not-found", $"会话 {sessionId} 无目标 {reference.Id}；先 goals/get 确认再操作"));
            }
            if (doc.Revision != reference.Revision) {
                return (null, DshRpcResults.Fail("goals/conflict",
                    $"目标版本 {reference.Revision} 已过期（当前 {doc.Revision}）；刷新目标栏后用新 revision 重试"));
            }
            if (doc.Phase == "complete") {
                return (null, DshRpcResults.Fail("goals/rejected", "目标已完成，状态不可再迁移；clear 后创建新目标"));
            }
            var activationFlipped = doc.Activation != activation;
            doc = doc with {
                Phase = phase,
                Activation = activation,
                Revision = doc.Revision + 1,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            _goals[sessionId] = doc;
            Persist();
            if (activationFlipped) {
                GoalChanged?.Invoke(sessionId, doc);
            }
            return (Project(doc), null);
        }
    }

    /// <summary>暂停（active→paused，armed→disarmed）</summary>
    internal (DshGoal? Goal, DshRpcResult? Error) Pause(string sessionId, DshGoalRef reference) =>
        Transition(sessionId, reference, "paused", "disarmed");

    /// <summary>恢复（paused→active，disarmed→armed）</summary>
    internal (DshGoal? Goal, DshRpcResult? Error) Resume(string sessionId, DshGoalRef reference) =>
        Transition(sessionId, reference, "active", "armed");

    /// <summary>完成（→complete，disarmed）</summary>
    internal (DshGoal? Goal, DshRpcResult? Error) Complete(string sessionId, DshGoalRef reference) =>
        Transition(sessionId, reference, "complete", "disarmed");

    /// <summary>清除（CAS；返回被清目标的 id/revision）</summary>
    internal (DshGoalRef? Ref, DshRpcResult? Error) Clear(string sessionId, DshGoalRef reference) {
        lock (_gate) {
            EnsureLoaded();
            if (!_goals.TryGetValue(sessionId, out var doc) || doc.Id != reference.Id) {
                return (null, DshRpcResults.Fail("goals/not-found", $"会话 {sessionId} 无目标 {reference.Id}；先 goals/get 确认再操作"));
            }
            if (doc.Revision != reference.Revision) {
                return (null, DshRpcResults.Fail("goals/conflict",
                    $"目标版本 {reference.Revision} 已过期（当前 {doc.Revision}）；刷新目标栏后用新 revision 重试"));
            }
            _goals.Remove(sessionId);
            Persist();
            GoalChanged?.Invoke(sessionId, doc);
            return (new DshGoalRef { Id = doc.Id, Revision = doc.Revision + 1 }, null);
        }
    }

    /// <summary>轮次计数（DshTurnRunner 受理钩子；无目标/未武装 no-op）</summary>
    internal void IncrementRounds(string sessionId) {
        lock (_gate) {
            EnsureLoaded();
            if (!_goals.TryGetValue(sessionId, out var doc) || doc.Activation != "armed") {
                return;
            }
            _goals[sessionId] = doc with {
                RoundsStarted = doc.RoundsStarted + 1,
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            Persist();
        }
    }

    /// <summary>投影落盘文档 → wire 形状（时间毫秒）</summary>
    private static DshGoal Project(DshGoalDocument doc) => new() {
        Id = doc.Id,
        Revision = doc.Revision,
        Objective = doc.Objective,
        Phase = doc.Phase,
        Activation = doc.Activation,
        RoundsStarted = doc.RoundsStarted,
        MaxGoalRounds = doc.MaxGoalRounds,
        CreatedAt = doc.CreatedAt,
        UpdatedAt = doc.UpdatedAt,
    };

    /// <summary>惰性加载（首次访问读盘；损坏/缺失退化为空表）</summary>
    private void EnsureLoaded() {
        if (_loaded) {
            return;
        }
        _loaded = true;
        try {
            if (!fs.FileExists(_filePath)) {
                return;
            }
            var json = fs.ReadAllTextAsync(_filePath, CancellationToken.None).GetAwaiter().GetResult();
            var document = RelaxedJsonSerializer.Deserialize(json, DshJsonContext.Default.DshGoalsDocument);
            if (document?.Goals is { Count: > 0 } goals) {
                _goals = new Dictionary<string, DshGoalDocument>(goals, StringComparer.Ordinal);
            }
        } catch (Exception ex) {
            GuiDiag.Log($"[DSH-GOALS] load failed: {ex.Message}");
        }
    }

    /// <summary>全量落盘（KB 级低频写；失败只记日志不回滚）</summary>
    private void Persist() {
        try {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !fs.DirectoryExists(directory)) {
                fs.CreateDirectory(directory);
            }
            var document = new DshGoalsDocument { Goals = _goals };
            _ = fs.WriteAllTextAsync(_filePath, RelaxedJsonSerializer.Serialize(document, DshJsonContext.Default), CancellationToken.None);
        } catch (Exception ex) {
            GuiDiag.Log($"[DSH-GOALS] persist failed: {ex.Message}");
        }
    }

    /// <summary>默认轮次上限（前端 zod 可选；v1 常量 10）</summary>
    private const int DefaultMaxGoalRounds = 10;
}
