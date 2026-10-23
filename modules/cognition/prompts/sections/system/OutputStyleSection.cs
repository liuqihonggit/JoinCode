namespace Core.Prompts.Sections;

/// <summary>
/// 输出风格 Section — settings.current.outputStyle 键消费方（对齐 TS outputStyle 子系统）：
/// <para>内建名映射既有两 section（explanatory → <see cref="ExplanatoryStyleSection"/>、
/// learning → <see cref="LearningStyleSection"/>）；自定义名经 <see cref="OutputStyleLoader"/>
/// 从用户级+项目级 output-styles 目录加载（项目覆盖用户同名额）。</para>
/// <para>未配置/自定义名无对应文件 → null（section 不注入，零行为变化）。
/// 双目录路径与 FileSystem 经 <see cref="SystemPromptProviderOptions"/> 注入
///（PromptConfigSnapshot.Current — EnvironmentSection 同先例）。</para>
/// </summary>
[PromptSection(Name = "output_style", Order = 82, IsDynamic = true)]
public static class OutputStyleSection {
    /// <summary>内建风格名 — explanatory（对齐 TS 内建）。</summary>
    public const string BuiltinExplanatory = "explanatory";

    /// <summary>内建风格名 — learning（对齐 TS 内建）。</summary>
    public const string BuiltinLearning = "learning";

    /// <summary>
    /// 获取输出风格提示词内容 — 内建映射/自定义目录加载/未配置 null。
    /// </summary>
    /// <returns>风格 prompt 文本；未配置或找不到 null。</returns>
    public static string? GetContent() {
        var options = PromptConfigSnapshot.Current;
        var styleName = options.OutputStyle;
        if (string.IsNullOrWhiteSpace(styleName))
            return null;

        if (string.Equals(styleName, BuiltinExplanatory, StringComparison.OrdinalIgnoreCase))
            return ExplanatoryStyleSection.GetContent();

        if (string.Equals(styleName, BuiltinLearning, StringComparison.OrdinalIgnoreCase))
            return LearningStyleSection.GetContent();

        return LoadCustomStyle(options, styleName);
    }

    /// <summary>自定义风格 — OutputStyleLoader 双层目录查找同名额（项目覆盖用户）。</summary>
    private static string? LoadCustomStyle(SystemPromptProviderOptions options, string styleName) {
        if (options.FileSystem is null)
            return null;

        var userDir = options.OutputStyleUserDir ?? string.Empty;
        var projectDir = options.OutputStyleProjectDir ?? string.Empty;
        if (userDir.Length == 0 && projectDir.Length == 0)
            return null;

        var loader = new OutputStyleLoader(options.FileSystem, userDir, projectDir);
        var styles = loader.LoadAsync().GetAwaiter().GetResult();
        return styles.FirstOrDefault(s => string.Equals(s.Name, styleName, StringComparison.OrdinalIgnoreCase))?.Prompt;
    }

    /// <summary>
    /// 创建输出风格 Section 实例（动态 — 每轮重读当前配置，settings 热切换生效）。
    /// </summary>
    /// <returns>动态系统提示词部分。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("output_style", GetContent);
}
