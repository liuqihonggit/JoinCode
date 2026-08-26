using CommunityToolkit.Mvvm.Input;

namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel partial — 会话树形展示相关命令（需求11）。
/// 从 MainViewModel.cs 拆出以控制文件行数（JCC8001 ≤2000 行）。
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>需求11：子会话右键打开 worktree 文件夹 — 用资源管理器直达 worktree 目录</summary>
    [RelayCommand]
    private void OpenSessionWorktree(SessionItem? item)
    {
        if (item?.WorktreePath is not { Length: > 0 } path || !System.IO.Directory.Exists(path))
            return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
    }

    /// <summary>需求11：切换主会话展开/折叠（显示子会话列表）</summary>
    [RelayCommand]
    private void ToggleSessionExpanded(SessionItem? item)
    {
        if (item is not null)
            item.IsExpanded = !item.IsExpanded;
    }

    /// <summary>需求11：异步从引擎拉取子会话填充 Children — AttachRealSession 后调用（快照避免跨线程）</summary>
    public async Task PopulateSubSessionsAsync(SessionItem[] sessions)
    {
        try
        {
            foreach (var session in sessions)
            {
                var subs = await _session.GetSubSessionsAsync(session.Id).ConfigureAwait(false);
                if (subs.Count == 0)
                    continue;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    session.Children.Clear();
                    foreach (var sub in subs)
                    {
                        session.Children.Add(new SessionItem
                        {
                            Id = sub.Id,
                            Title = sub.Title,
                            ParentId = sub.ParentSessionId,
                            HasWorktree = sub.WorktreePath is not null,
                            WorktreePath = sub.WorktreePath,
                            SubSessionState = sub.State
                        });
                    }
                    session.IsExpanded = true;
                });
            }
        }
        catch (Exception ex)
        {
            WriteErrorLog(ex);
        }
    }
}
