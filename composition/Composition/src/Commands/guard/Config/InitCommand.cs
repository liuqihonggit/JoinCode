
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
            var settingsJson = BuildDefaultSettingsJson();
            await fs.WriteAllTextAsync(settingsFile, settingsJson, context.CancellationToken).ConfigureAwait(false);
            TerminalHelper.WriteLine("  ✓ 创建 settings.json (骨架 — 模型列表将由 AutoFetchModels 启动时拉取)");
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
    /// 构建默认 settings.json 骨架 — 仅含供应商连接信息，不含模型列表
    /// 模型列表由启动时 AutoFetchModels 从 {endpoint}/{modelsEndpoint} 自动拉取填充
    /// 两条路径: deepseek (openai-compatible) + deepseek-anthropic (anthropic 协议, api.deepseek.com/anthropic)
    /// 配置大于代码: 具体模型信息由 API 拉取，代码只留基础结构
    /// </summary>
    private static string BuildDefaultSettingsJson()
    {
        return """
        {
          "vendor": {
            "deepseek": {
              "provider": "deepseek",
              "protocol": "openai-compatible",
              "endpoint": "https://api.deepseek.com",
              "apiKeyEnvVar": "DEEPSEEK_API_KEY",
              "modelsEndpoint": "models"
            },
            "deepseek-anthropic": {
              "provider": "deepseek",
              "protocol": "anthropic",
              "endpoint": "https://api.deepseek.com/anthropic",
              "apiKeyEnvVar": "DEEPSEEK_API_KEY",
              "modelsEndpoint": "v1/models"
            }
          },
          "autoFetchModels": true,
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
