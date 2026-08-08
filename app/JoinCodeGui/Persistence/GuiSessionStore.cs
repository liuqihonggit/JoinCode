using System.Text.Json;

using JoinCode.Abstractions.Configuration.AppData;
using JoinCode.Abstractions.Interfaces;

namespace JoinCode.Gui.Persistence;

/// <summary>
/// GUI 会话持久化存储 — 读写同一 sessions 目录（~/.jcc/sessions/{Id}.json），
/// JSON 形状对齐 CLI SessionData，使 CLI /resume 与 GUI 侧边栏共享同一会话文件。
/// 通过 IFileSystem 抽象注入，生产用 PhysicalFileSystem，测试用 InMemoryFileSystem。
/// </summary>
public sealed class GuiSessionStore
{
    private readonly IFileSystem _fs;
    private readonly string _sessionsDir;

    public GuiSessionStore(IFileSystem fs, string? sessionsDir = null)
    {
        _fs = fs;
        _sessionsDir = sessionsDir ?? WorkflowConstants.Paths.SessionsDirectory;
    }

    /// <summary>当前会话目录路径</summary>
    public string SessionsDirectory => _sessionsDir;

    /// <summary>
    /// 列出全部会话摘要（按最后修改时间降序，损坏文件跳过）— 供侧边栏快速加载。
    /// </summary>
    public IReadOnlyList<GuiSessionSummary> ListSessions()
    {
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

    /// <summary>读取指定会话的完整数据；不存在或损坏返回 null</summary>
    public GuiSessionData? Load(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
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

    /// <summary>保存会话到磁盘（目录不存在则创建），写入成功返回 true</summary>
    public bool Save(GuiSessionData session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.Id))
            throw new ArgumentException("会话 Id 不能为空", nameof(session));

        if (!_fs.DirectoryExists(_sessionsDir))
            _fs.CreateDirectory(_sessionsDir);

        var json = JsonSerializer.Serialize(session, GuiJsonContext.Default.GuiSessionData);
        _fs.WriteAllText(GetSessionPath(session.Id), json);
        return true;
    }

    /// <summary>删除指定会话文件；不存在返回 false</summary>
    public bool Delete(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var path = GetSessionPath(sessionId);
        if (!_fs.FileExists(path))
            return false;

        _fs.DeleteFile(path);
        return true;
    }

    private string GetSessionPath(string sessionId) => _fs.CombinePath(_sessionsDir, $"{sessionId}.json");
}
