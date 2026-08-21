
namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.Init, Description = "AI驱动初始化项目配置文件", Usage = "/init [quick]", Category = ChatCommandCategory.Config, ArgumentHint = "[quick]")]
public sealed class InitCommand(IModelConfigLoader? modelConfigLoader = null) : ChatCommandBase
{
    private readonly IModelConfigLoader? _modelConfigLoader = modelConfigLoader;
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var args = ChatCommandBase.GetNormalizedArgs(context).ToLowerInvariant();
        var isQuick = args is "quick" or "q";

        if (isQuick)
        {
            await QuickInitAsync(context).ConfigureAwait(false);
        }
        else
        {
            await AiDrivenInitAsync(context).ConfigureAwait(false);
        }

        return ChatCommandResult.Continue();
    }

    private static async Task AiDrivenInitAsync(ChatCommandContext context)
    {
        var fs = context.GetCommandServices().FileSystem;
        var cwd = fs.GetCurrentDirectory();
        // 修复: 统一使用 AppDataConstants.AppDataFolder,避免硬编码 ".jcc" 与 EnsureJccDirectory 路径不一致
        var rulesFile = Path.Combine(cwd, AppDataConstants.AppDataFolder, "project_rules.md");

        EnsureJccDirectory(cwd, fs);

        TerminalHelper.WriteLine("正在分析代码库以生成项目规则...");
        TerminalHelper.NewLine();

        var existingContent = string.Empty;
        if (fs.FileExists(rulesFile))
        {
            existingContent = await fs.ReadAllTextAsync(rulesFile, context.CancellationToken).ConfigureAwait(false);
        }

        var prompt = BuildInitPrompt(cwd, existingContent);

        var result = await context.GetCommandServices().ChatService.SendMessageAsync(prompt, context.CancellationToken).ConfigureAwait(false);

        if (result is not null)
        {
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine("项目规则已生成，请检查 .jcc/project_rules.md");
        }

        await RegisterProjectConfigAsync(context, cwd).ConfigureAwait(false);
    }

    private async Task QuickInitAsync(ChatCommandContext context)
    {
        var fs = context.GetCommandServices().FileSystem;
        var cwd = fs.GetCurrentDirectory();
        // 修复: 统一使用 AppDataConstants.AppDataFolder,避免硬编码 ".jcc" 与 EnsureJccDirectory 路径不一致
        var jccDir = Path.Combine(cwd, AppDataConstants.AppDataFolder);
        var rulesFile = Path.Combine(jccDir, "project_rules.md");
        var settingsFile = Path.Combine(jccDir, "settings.json");

        TerminalHelper.WriteLine($"快速初始化项目配置: {cwd}");
        TerminalHelper.NewLine();

        EnsureJccDirectory(cwd, fs);

        if (!fs.FileExists(rulesFile))
        {
            await fs.WriteAllTextAsync(rulesFile, "# 项目规则\n\n在此添加项目特定的规则和指导\n", context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine("  ✓ 创建 project_rules.md");
        }
        else
        {
            TerminalHelper.WriteLine("  · project_rules.md 已存在");
        }

        if (!fs.FileExists(settingsFile))
        {
            var defaultModel = _modelConfigLoader?.GetDefaultModelId("deepseek") is { Length: > 0 } m ? m : "deepseek-v4-flash";
            var settingsJson = BuildDefaultSettingsJson(defaultModel);
            await fs.WriteAllTextAsync(settingsFile, settingsJson, context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine("  ✓ 创建 settings.json");
        }
        else
        {
            TerminalHelper.WriteLine("  · settings.json 已存在");
        }

        await RegisterProjectConfigAsync(context, cwd).ConfigureAwait(false);

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("快速初始化完成");
        TerminalHelper.WriteLine("使用 /init (不带quick) 让AI分析代码库并生成详细规则");
    }

    /// <summary>
    /// 构建默认 settings.json — 含 DeepSeek 供应商预设和模型列表
    /// 模型: deepseek-v4-flash (0731) / deepseek-v4-pro (0813) / deepseek-v4-flash-vision-exp (视觉实验)
    /// 视觉模型支持 JPEG/PNG/GIF/WebP 图片输入，通过 OpenAI 兼容 image_url content block 发送
    /// </summary>
    private static string BuildDefaultSettingsJson(string defaultModel)
    {
        return $$"""
        {
          "vendor": {
            "deepseek": {
              "provider": "deepseek",
              "protocol": "openai-compatible",
              "model": "{{defaultModel}}",
              "endpoint": "https://api.deepseek.com",
              "apiKeyEnvVar": "DEEPSEEK_API_KEY",
              "models": [
                {
                  "id": "deepseek-v4-flash",
                  "displayName": "DeepSeek V4 Flash",
                  "contextWindow": 128000,
                  "description": "DeepSeek V4 Flash (0731) — 快速通用模型",
                  "capabilities": { "fastMode": true, "modalities": ["text", "toolUse"] }
                },
                {
                  "id": "deepseek-v4-pro",
                  "displayName": "DeepSeek V4 Pro",
                  "contextWindow": 128000,
                  "description": "DeepSeek V4 Pro (0813) — 深度推理模型",
                  "capabilities": { "thinkingMode": true, "modalities": ["text", "thinking", "toolUse"] }
                },
                {
                  "id": "deepseek-v4-flash-vision-exp",
                  "displayName": "DeepSeek V4 Flash Vision (Exp)",
                  "contextWindow": 128000,
                  "description": "DeepSeek V4 Flash 视觉实验模型 — 支持 JPEG/PNG/GIF/WebP 图片输入",
                  "capabilities": { "fastMode": true, "modalities": ["text", "readImage", "readGif", "toolUse"] }
                }
              ]
            }
          },
          "current": { "profile": "deepseek" }
        }
        """;
    }

    private static void EnsureJccDirectory(string cwd, IFileSystem fs)
    {
        var jccDir = Path.Combine(cwd, AppDataConstants.AppDataFolder);
        if (fs.DirectoryExists(jccDir))
        {
            DirectoryHelper.EnsureDirectoryExists(fs, jccDir);
            TerminalHelper.WriteLine("  ✓ 创建 .jcc/ 目录");
        }
    }

    private static async Task RegisterProjectConfigAsync(ChatCommandContext context, string cwd)
    {
        var configService = context.Services.GetService<IConfigurationService>();
        if (configService is not null)
        {
            try
            {
                var projectDir = cwd.Replace("\\", "/");
                await configService.SetAsync("project.directory", projectDir, context.CancellationToken).ConfigureAwait(false);
                await configService.SetAsync("project.initialized", "true", context.CancellationToken).ConfigureAwait(false);
                TerminalHelper.WriteLine("  ✓ 项目配置已注册到全局配置");
            }
            catch
            {
                TerminalHelper.WriteLine("  · 全局配置注册跳过（配置服务不可用）");
            }
        }
    }

    private static string BuildInitPrompt(string cwd, string existingContent)
    {
        var hasExisting = !string.IsNullOrEmpty(existingContent);
        var prompt = $"""
            Analyze the codebase at {cwd} and generate a comprehensive project rules file.

            ## Instructions

            1. Explore the codebase structure (directory layout, key files, configuration files)
            2. Identify the tech stack, build system, test framework, and key dependencies
            3. Read existing documentation (README, AGENTS.md, CLAUDE.md, etc.)
            4. Generate a project_rules.md that includes:
               - Build and run commands
               - Test commands
               - Lint/format commands
               - High-level architecture overview
               - Key conventions and patterns
               - Important constraints (e.g., AOT compatibility, specific coding rules)

            ## Rules for the generated file

            - Only include information that, if removed, would cause an AI assistant to make mistakes
            - Do NOT include obvious instructions or generic advice
            - Merge important content from existing AI rule files (Cursor rules, Copilot rules, etc.)
            - Do NOT fabricate sections like "Common Development Tasks" unless they contain non-obvious information
            - Be concise and specific to THIS project

            {(hasExisting ? $"## Existing project_rules.md content\n\nThe file already exists. Suggest improvements based on the codebase analysis:\n\n{existingContent}" : "Write the new file to .jcc/project_rules.md")}

            After analysis, write the complete project_rules.md file.
            """;
        return prompt;
    }
}
