namespace NativeTui.Host;

/// <summary>
/// 外部编辑器启动器（TASK064 P3 — 基准 defaultBindings.ts:83-84 ctrl+g/ctrl+x ctrl+e →
/// chat:externalEditor → $EDITOR）：输入文本写 temp 文件 → 启动 $EDITOR（未设置时
/// Windows 兜底 notepad.exe、其余兜底 vi）→ 无超时等待退出（交互等待非资源释放 —
/// 用户编辑时长不限，对齐 AGENTS 释放禁超时的反向面：等待是功能本体）→ 读回编辑后文本。
/// 编辑器启动失败/结果读取失败返回 null（ReplShell 侧降级丢弃不打扰输入框）。
/// </summary>
public static class ExternalEditorLauncher
{
    /// <summary>编辑输入文本 — 返回编辑后文本（含空串 = 用户清空的合法编辑形态）；null = 启动/读取失败。</summary>
    /// <param name="text">当前输入文本（写入 temp 文件供编辑器打开）。</param>
    public static async Task<string?> EditAsync(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nativetui-editor-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(path, text).ConfigureAwait(false);
        try
        {
            using var process = StartEditor(path);
            if (process is null)
            {
                return null;
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return await File.ReadAllTextAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 编辑器通道容错 — 启动失败/等待中断/读回失败统一 null 降级（调用方静默丢弃）
            return null;
        }
        finally
        {
            // temp 文件资源清理（非仓库文件 — AGENTS 禁删铁律针对仓库文件；temp 归位清理
            // 是进程资源卫生，删除失败静默：文件留在系统 temp 由 OS 清理策略兜底）
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>解析并启动编辑器进程 — $EDITOR 环境变量（支持带参数形态如 "code -w"，
    /// 按空白拆分首段为可执行名 + 余段前置参数 + temp 文件路径尾参）；未设置时平台兜底
    ///（Windows notepad.exe / 其余 vi）。启动失败返回 null。</summary>
    private static Process? StartEditor(string filePath)
    {
        var editor = Environment.GetEnvironmentVariable("EDITOR");
        var fileName = string.IsNullOrWhiteSpace(editor)
            ? (OperatingSystem.IsWindows() ? "notepad.exe" : "vi")
            : editor.Trim().Split(' ', 2)[0];
        var prefixArguments = string.IsNullOrWhiteSpace(editor) || !editor.Contains(' ')
            ? string.Empty
            : editor[(editor.IndexOf(' ') + 1)..].Trim() + " ";

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = prefixArguments + filePath,
                    UseShellExecute = false,
                },
            };
            if (process.Start())
            {
                return process;
            }

            process.Dispose();
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
