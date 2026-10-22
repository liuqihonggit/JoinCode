namespace JoinCode.Gui.WebHost;

/// <summary>
/// 工作区路径打开（TASK066 C4，对应 DSH session/openWorkspacePath 与 session/workspacePathApplications）：
/// 运行在真实桌面上，因此只暴露「资源管理器打开/选中」一种处理器，避免把前端参数当任意程序执行。
/// </summary>
internal static class DshWorkspacePathService {
    /// <summary>唯一处理器 ID（与 workspacePathApplications 返回值一致）</summary>
    internal const string ExplorerId = "explorer";

    /// <summary>当前宿主是否能把路径交给桌面文件管理器（Windows 资源管理器）</summary>
    internal static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>列出路径可用的打开方式；路径不存在时返回空表（前端隐藏打开入口）</summary>
    internal static IReadOnlyList<DshWorkspacePathApp> ListApplications(IFileSystem fileSystem, string path) {
        var resolved = Resolve(fileSystem, path);
        if (resolved is null) {
            return [];
        }
        return [
            new DshWorkspacePathApp {
                Id = ExplorerId,
                Name = "文件资源管理器",
                Default = true,
                Icon = null,
            },
        ];
    }

    /// <summary>
    /// 打开/选中路径。失败返回带引导的错误说明（为什么失败 + 正确传参），
    /// <paramref name="action"/> 仅接受 <c>reveal</c>，<paramref name="application"/> 仅接受已公布的处理器。
    /// </summary>
    internal static (bool Opened, string? Error) Open(
        IFileSystem fileSystem,
        string path,
        string? action,
        string? application) {
        if (action is not null && !string.Equals(action, "reveal", StringComparison.Ordinal)) {
            return (false, $"session/openWorkspacePath 的 action 只支持 \"reveal\"（在资源管理器中选中）；收到 \"{action}\"。省略 action 表示直接打开路径。");
        }
        if (application is not null && !string.Equals(application, ExplorerId, StringComparison.Ordinal)) {
            return (false, $"session/openWorkspacePath 的 application 只支持 \"{ExplorerId}\"（workspacePathApplications 当前公布的处理方式）；收到 \"{application}\"。省略 application 使用系统默认方式。");
        }
        if (Resolve(fileSystem, path) is not { } resolved) {
            return (false, $"session/openWorkspacePath 的 path 必须是已存在的绝对路径（当前 Desktop 宿主可直接访问）；收到 \"{path}\"。请传回 workspaceFiles/* 返回的路径。");
        }
        try {
            var startInfo = new ProcessStartInfo {
                FileName = "explorer.exe",
                UseShellExecute = true,
            };
            startInfo.ArgumentList.Add(action == "reveal" ? $"/select,{resolved}" : resolved);
            using var process = Process.Start(startInfo);
            return (true, null);
        } catch (Exception ex) {
            return (false, $"session/openWorkspacePath 启动资源管理器失败：{ex.Message}。请确认桌面会话可交互，或改用 workspaceFiles/read 预览文件。");
        }
    }

    /// <summary>规范化并校验绝对路径（存在才返回；相对路径/不存在返回 null）</summary>
    private static string? Resolve(IFileSystem fileSystem, string path) {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) {
            return null;
        }
        string resolved;
        try {
            resolved = Path.GetFullPath(path);
        } catch (Exception) {
            return null;
        }
        return fileSystem.FileExists(resolved) || fileSystem.DirectoryExists(resolved) ? resolved : null;
    }
}
