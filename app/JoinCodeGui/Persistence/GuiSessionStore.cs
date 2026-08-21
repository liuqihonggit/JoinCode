using System.Text.Json;

using JoinCode.Abstractions.Configuration.AppData;
using JoinCode.Abstractions.Interfaces;

namespace JoinCode.Gui.Persistence;

/// <summary>
/// GUI 会话持久化存储 — 读写同一 sessions 目录。
/// 若注入 ITranscriptService,走统一入口(.jsonl + 每会话子目录,与 CLI --continue 共享);
/// 否则回退到旧扁平 .json(测试隔离兼容)。
/// 通过 IFileSystem 抽象注入,生产用 PhysicalFileSystem,测试用 InMemoryFileSystem。
/// </summary>
public sealed class GuiSessionStore
{
    private readonly IFileSystem _fs;
    private readonly string _sessionsDir;
    private ITranscriptService? _transcriptService;

    public GuiSessionStore(IFileSystem fs, string? sessionsDir = null, ITranscriptService? transcriptService = null)
    {
        _fs = fs;
        _sessionsDir = sessionsDir ?? WorkflowConstants.Paths.SessionsDirectory;
        _transcriptService = transcriptService;
    }

    /// <summary>当前会话目录路径</summary>
    public string SessionsDirectory => _sessionsDir;

    /// <summary>
    /// 后续注入 ITranscriptService — 引擎后台组装完成后由 AttachRealSession 调用,
    /// 切换到统一入口(.jsonl + 子目录)。注入后需重新 ListSessions 刷新侧边栏。
    /// </summary>
    public void SetTranscriptService(ITranscriptService transcriptService)
    {
        _transcriptService = transcriptService;
    }

    /// <summary>
    /// 列出全部会话摘要（按最后修改时间降序，损坏文件跳过）— 供侧边栏快速加载。
    /// </summary>
    public IReadOnlyList<GuiSessionSummary> ListSessions()
    {
        if (_transcriptService is not null)
            return ListSessionsViaTranscriptService();

        if (!_fs.DirectoryExists(_sessionsDir))
            return [];

        var summaries = new List<GuiSessionSummary>();
        foreach (var file in _fs.GetFiles(_sessionsDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = _fs.ReadAllText(file);
                var data = JsonSerializer.Deserialize(json, GuiJsonContext.Default.GuiSessionData);
                if (data is null || string.IsNullOrWhiteSpace(data.Id))
                    continue;

                summaries.Add(new GuiSessionSummary
                {
                    Id = data.Id,
                    Title = string.IsNullOrWhiteSpace(data.CustomTitle) ? data.Id : data.CustomTitle,
                    CreatedAt = data.CreatedAt,
                    LastModified = _fs.GetLastWriteTime(file),
                    MessageCount = data.Messages?.Count ?? 0
                });
            }
            catch (Exception)
            {
                // 损坏会话文件跳过，不阻塞列表
                System.Diagnostics.Debug.WriteLine($"[GuiSessionStore] 跳过损坏会话文件: {file}");
            }
        }

        return summaries.OrderByDescending(s => s.LastModified).ToList();
    }

    /// <summary>通过 ITranscriptService 列出会话(统一入口,.jsonl + 子目录)</summary>
    private GuiSessionSummary[] ListSessionsViaTranscriptService()
    {
        var summaries = _transcriptService!.ListTranscriptsAsync(200).GetAwaiter().GetResult();
        var result = new List<GuiSessionSummary>(summaries.Count);
        foreach (var s in summaries)
        {
            var title = s.SessionId;
            try
            {
                var custom = _transcriptService.GetCustomTitleAsync(s.SessionId).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(custom))
                    title = custom;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GuiSessionStore] ListSessions 读取 CustomTitle 失败 sid={s.SessionId}: {ex.Message}");
            }
            result.Add(new GuiSessionSummary
            {
                Id = s.SessionId,
                Title = title,
                CreatedAt = s.CreatedAt,
                LastModified = s.LastModifiedAt,
                MessageCount = s.MessageCount
            });
        }
        return result.OrderByDescending(s => s.LastModified).ToArray();
    }

    /// <summary>读取指定会话的完整数据；不存在或损坏返回 null</summary>
    public GuiSessionData? Load(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (_transcriptService is not null)
            return LoadViaTranscriptService(sessionId);

        var path = GetSessionPath(sessionId);
        if (!_fs.FileExists(path))
            return null;

        try
        {
            var json = _fs.ReadAllText(path);
            return JsonSerializer.Deserialize(json, GuiJsonContext.Default.GuiSessionData);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>通过 ITranscriptService 加载会话(统一入口,TranscriptEntry → GuiSessionMessage)</summary>
    private GuiSessionData? LoadViaTranscriptService(string sessionId)
    {
        try
        {
            var entries = _transcriptService!.LoadTranscriptAsync(sessionId).GetAwaiter().GetResult();
            if (entries.Count == 0)
                return null;

            var messages = new List<GuiSessionMessage>(entries.Count);
            foreach (var e in entries)
            {
                // 过滤 Type 非空的非消息条目(custom-title/agent-name 等元数据)
                if (!string.IsNullOrEmpty(e.Type))
                    continue;
                if (string.IsNullOrEmpty(e.Role))
                    continue;
                messages.Add(new GuiSessionMessage
                {
                    Role = e.Role,
                    Content = e.Content,
                    Timestamp = e.Timestamp
                });
            }

            var info = _transcriptService.GetSessionInfoAsync(sessionId).GetAwaiter().GetResult();
            string customTitle = string.Empty;
            try
            {
                customTitle = _transcriptService.GetCustomTitleAsync(sessionId).GetAwaiter().GetResult() ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GuiSessionStore] Load 读取 CustomTitle 失败 sid={sessionId}: {ex.Message}");
            }

            return new GuiSessionData
            {
                Id = sessionId,
                ProjectPath = info?.ProjectPath ?? string.Empty,
                CustomTitle = customTitle,
                CreatedAt = info?.CreatedAt ?? DateTime.UtcNow,
                Messages = messages
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>保存会话到磁盘（目录不存在则创建），写入成功返回 true</summary>
    public bool Save(GuiSessionData session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.Id))
            throw new ArgumentException("会话 Id 不能为空", nameof(session));

        if (_transcriptService is not null)
            return SaveViaTranscriptService(session);

        if (!_fs.DirectoryExists(_sessionsDir))
            _fs.CreateDirectory(_sessionsDir);

        var json = JsonSerializer.Serialize(session, GuiJsonContext.Default.GuiSessionData);
        _fs.WriteAllText(GetSessionPath(session.Id), json);
        return true;
    }

    /// <summary>
    /// 通过 ITranscriptService 保存会话(统一入口,覆盖语义)。
    /// 先 Delete 清空旧 transcript,再 AppendEntries 写入新消息,再 SaveSessionInfo + SaveCustomTitle。
    /// </summary>
    private bool SaveViaTranscriptService(GuiSessionData session)
    {
        try
        {
            // 先 Delete 清空旧(幂等,不存在不报错)
            _transcriptService!.DeleteTranscriptAsync(session.Id).GetAwaiter().GetResult();

            // 追加消息条目
            if (session.Messages is { Count: > 0 } messages)
            {
                var entries = new List<TranscriptEntry>(messages.Count);
                foreach (var m in messages)
                {
                    entries.Add(new TranscriptEntry
                    {
                        SessionId = session.Id,
                        Role = m.Role,
                        Content = m.Content,
                        Timestamp = m.Timestamp
                    });
                }
                _transcriptService.AppendEntriesAsync(session.Id, entries).GetAwaiter().GetResult();
            }

            // 保存会话元数据
            _transcriptService.SaveSessionInfoAsync(session.Id, new SessionInfo
            {
                Id = session.Id,
                ProjectPath = session.ProjectPath,
                ModelId = session.ModelId,
                Vendor = session.Vendor,
                CreatedAt = session.CreatedAt == default ? DateTime.UtcNow : session.CreatedAt
            }).GetAwaiter().GetResult();

            // 保存自定义标题(非空时)
            if (!string.IsNullOrWhiteSpace(session.CustomTitle))
                _transcriptService.SaveCustomTitleAsync(session.Id, session.CustomTitle).GetAwaiter().GetResult();

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GuiSessionStore] SaveViaTranscriptService 失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>删除指定会话文件；不存在返回 false</summary>
    public bool Delete(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (_transcriptService is not null)
            return _transcriptService.DeleteTranscriptAsync(sessionId).GetAwaiter().GetResult();

        var path = GetSessionPath(sessionId);
        if (!_fs.FileExists(path))
            return false;

        _fs.DeleteFile(path);
        return true;
    }

    private string GetSessionPath(string sessionId) => _fs.CombinePath(_sessionsDir, $"{sessionId}.json");
}
