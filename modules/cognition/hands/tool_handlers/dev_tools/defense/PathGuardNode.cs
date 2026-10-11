namespace Tools.Handlers;

/// <summary>
/// 路径守卫 node — 独立公共对象，纯函数无依赖，提供路径格式检查。
/// 任意工具可注入此 node 检查 UNC 路径、Notebook 路径、keyword-sections 路径、doctor 允许路径等。
/// </summary>
[Register(typeof(PathGuardNode), ServiceLifetime.Singleton)]
public sealed class PathGuardNode {
    /// <summary>是否为 UNC 路径（以 \\ 或 // 开头）— 可能导致 NTLM 凭据泄露。</summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>UNC 路径返回 true，否则返回 false</returns>
    public static bool IsUncPath(string filePath)
        => filePath.StartsWith("\\\\", StringComparison.Ordinal) || filePath.StartsWith("//", StringComparison.Ordinal);

    /// <summary>是否为 Jupyter Notebook 文件（.ipynb）— 必须用 NotebookEdit 工具编辑。</summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>Notebook 文件返回 true，否则返回 false</returns>
    public static bool IsNotebookPath(string filePath)
        => filePath.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase);

    /// <summary>是否为 keyword-sections.json — 仅 keywordMaintenance Agent 可编辑。</summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>keyword-sections.json 返回 true，否则返回 false</returns>
    public static bool IsKeywordSectionsPath(string filePath)
        => !string.IsNullOrEmpty(filePath)
           && Path.GetFileName(filePath).Equals("keyword-sections.json", StringComparison.OrdinalIgnoreCase);

    /// <summary>doctor Agent 允许编辑的路径 — .jcc/diag/、.jcc/reflexion/、worktree 内文件。</summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>允许编辑的路径返回 true，否则返回 false</returns>
    public static bool IsDoctorAllowedEditPath(string filePath) {
        if (string.IsNullOrEmpty(filePath)) return false;
        var normalized = filePath.Replace('\\', '/');
        return normalized.Contains("/.jcc/diag/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/.jcc/reflexion/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/worktree/", StringComparison.OrdinalIgnoreCase);
    }
}