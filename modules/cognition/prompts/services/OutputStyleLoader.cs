namespace Core.Prompts;

/// <summary>
/// 输出风格来源 — 对齐 TS OutputStyleConfig.source（裁剪：plugin/policy 属插件与企业策略生态，后续批次）。
/// </summary>
public enum OutputStyleSource {
    /// <summary>用户级（~/.jcc/output-styles/）</summary>
    [EnumValue("userSettings")] UserSettings,
    /// <summary>项目级（{cwd}/.jcc/output-styles/，同名覆盖用户级）</summary>
    [EnumValue("projectSettings")] ProjectSettings,
}

/// <summary>
/// 输出风格配置 — 对齐 TS OutputStyleConfig：文件名即样式名、markdown 内容即 prompt。
/// </summary>
public sealed record OutputStyleConfig(string Name, string Prompt, OutputStyleSource Source);

/// <summary>
/// 输出风格加载器 — 对齐 TS loadOutputStylesDir.ts：双层目录扫描 *.md
/// <para>用户级 ~/.jcc/output-styles/ + 项目级 {cwd}/.jcc/output-styles/（项目覆盖用户同名额）；
/// 文件名（去 .md）即样式名、内容即 prompt；缺目录空清单、非 .md 忽略、坏文件记警告跳过不阻断。</para>
/// <para>消费链：settings.current.outputStyle 键选中样式名 → OutputStyleSection 注入 prompt
///（内建名 explanatory/learning 映射既有两 section）。不持有 fs 所有权（DI 管理，本类无释放）。</para>
/// </summary>
public sealed class OutputStyleLoader {
    private readonly IFileSystem _fs;
    private readonly string _userDir;
    private readonly string _projectDir;
    private readonly ILogger? _logger;

    /// <summary>
    /// 初始化加载器 — 注入文件系统、双层目录（用户级/项目级）与可选日志。
    /// </summary>
    /// <param name="fs">文件系统抽象（所有权归 DI/调用方，本类不释放）。</param>
    /// <param name="userDir">用户级 output-styles 目录（如 ~/.jcc/output-styles/）。</param>
    /// <param name="projectDir">项目级 output-styles 目录（如 {cwd}/.jcc/output-styles/）。</param>
    /// <param name="logger">可选日志（坏文件警告）。</param>
    public OutputStyleLoader(IFileSystem fs, string userDir, string projectDir, ILogger? logger = null) {
        _fs = fs;
        _userDir = userDir;
        _projectDir = projectDir;
        _logger = logger;
    }

    /// <summary>
    /// 加载全部输出风格 — 用户级先入、项目级覆盖同名额（对齐 TS 优先序 built-in &lt; user &lt; project）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>风格清单（可能为空）。</returns>
    public async Task<IReadOnlyList<OutputStyleConfig>> LoadAsync(CancellationToken cancellationToken = default) {
        var styles = new Dictionary<string, OutputStyleConfig>(StringComparer.OrdinalIgnoreCase);

        await MergeDirAsync(styles, _userDir, OutputStyleSource.UserSettings, cancellationToken).ConfigureAwait(false);
        await MergeDirAsync(styles, _projectDir, OutputStyleSource.ProjectSettings, cancellationToken).ConfigureAwait(false);

        return [.. styles.Values];
    }

    /// <summary>按层扫描合并 — 非目录跳过；仅 *.md；坏文件记警告跳过（风格加载不阻断启动）。</summary>
    private async Task MergeDirAsync(Dictionary<string, OutputStyleConfig> styles, string dir, OutputStyleSource source, CancellationToken ct) {
        if (!_fs.DirectoryExists(dir))
            return;

        foreach (var filePath in _fs.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly)) {
            var name = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrEmpty(name))
                continue;

            try {
                var prompt = (await _fs.ReadAllTextAsync(filePath, ct).ConfigureAwait(false)).Trim();
                if (prompt.Length == 0)
                    continue;

                styles[name] = new OutputStyleConfig(name, prompt, source);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                _logger?.LogWarning(ex, "跳过损坏的输出风格文件: {FilePath}", filePath);
            }
        }
    }
}
