namespace JoinCode.ChatCommands;

/// <summary>
/// /install-github-app 命令 — 对齐 TS install-github-app.tsx
/// 设置 Claude GitHub Actions 工作流，包含多步分支交互
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.InstallGitHubApp, Description = "设置 Claude GitHub Actions 工作流", Usage = "/install-github-app", Category = ChatCommandCategory.Tools)]
public sealed class InstallGitHubAppCommand : IChatCommand
{
    public string Name => ChatCommandNameConstants.InstallGitHubApp;
    public string Description => "设置 Claude GitHub Actions 工作流";
    public string Usage => "/install-github-app";
    public string[] Aliases => [];
    public string ArgumentHint => string.Empty;
    public bool IsHidden => false;

    public async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var ct = context.CancellationToken;

        // Step 1: 检查 GitHub CLI
        var checkResult = await CheckGitHubCliAsync(ct).ConfigureAwait(false);
        if (!checkResult.Success)
        {
            ShowError(checkResult.ErrorMessage!, checkResult.FixHint);
            return ChatCommandResult.Continue();
        }

        // Step 2: 显示警告（如果有）
        if (checkResult.Warnings.Count > 0)
        {
            ShowWarnings(checkResult.Warnings);
            var confirmed = await Confirmation.ConfirmAsync("继续安装？", ct).ConfigureAwait(false);
            if (!confirmed)
            {
                TerminalHelper.WriteLine("安装已取消");
                return ChatCommandResult.Continue();
            }
        }

        // Step 3: 选择仓库
        var repoName = await ChooseRepoAsync(checkResult.CurrentRepo, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(repoName))
        {
            TerminalHelper.WriteLine("安装已取消");
            return ChatCommandResult.Continue();
        }

        // Step 4: 安装 GitHub App（提示用户在浏览器中安装）
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"{TerminalColors.Accent}正在打开浏览器安装 Claude GitHub App...{AnsiStyleConstants.Reset}");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"  手动访问: {TerminalColors.Accent}https://github.com/apps/claude{AnsiStyleConstants.Reset}");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"请为仓库 {TerminalColors.Accent}{repoName}{AnsiStyleConstants.Reset} 安装 App 并授予访问权限。");

        var installed = await Confirmation.ConfirmAsync("已安装 GitHub App？", ct).ConfigureAwait(false);
        if (!installed)
        {
            TerminalHelper.WriteLine("安装已取消");
            return ChatCommandResult.Continue();
        }

        // Step 5: 选择工作流类型
        var workflows = await SelectWorkflowsAsync(ct).ConfigureAwait(false);

        // Step 6: 选择 API Key 方式
        var (secretName, secretValue, authType) = await ChooseApiKeyMethodAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(secretValue))
        {
            TerminalHelper.WriteLine("安装已取消");
            return ChatCommandResult.Continue();
        }

        // Step 7: 执行创建
        var createResult = await SetupGitHubActionsAsync(repoName, workflows, secretName, secretValue, authType, ct).ConfigureAwait(false);

        // Step 8: 显示结果
        if (createResult.Success)
        {
            ShowSuccess(repoName, workflows.Count > 0);
        }
        else
        {
            ShowError(createResult.ErrorMessage!, createResult.FixHint);
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 检查 GitHub CLI 是否安装且已认证
    /// </summary>
    private static async Task<GitHubCheckResult> CheckGitHubCliAsync(CancellationToken ct)
    {
        TerminalHelper.WriteLine("正在检查 GitHub CLI 安装...");

        // 检查 gh 是否安装
        var ghCheck = await RunShellCommandAsync("gh --version", ct).ConfigureAwait(false);
        if (!ghCheck.Success)
        {
            return GitHubCheckResult.Fail(
                "GitHub CLI 未安装",
                "请安装 GitHub CLI: https://cli.github.com/");
        }

        // 检查认证状态
        var authCheck = await RunShellCommandAsync("gh auth status", ct).ConfigureAwait(false);
        if (!authCheck.Success)
        {
            return GitHubCheckResult.Fail(
                "GitHub CLI 未认证",
                "请运行: gh auth login");
        }

        // 获取当前仓库
        var currentRepo = await GetCurrentRepoAsync(ct).ConfigureAwait(false);

        var warnings = new List<string>();
        if (string.IsNullOrEmpty(currentRepo))
        {
            warnings.Add("当前目录不是 Git 仓库或没有配置 GitHub remote");
        }

        return GitHubCheckResult.Ok(currentRepo ?? string.Empty, warnings);
    }

    /// <summary>
    /// 获取当前 Git 仓库的 GitHub 仓库名
    /// </summary>
    private static async Task<string?> GetCurrentRepoAsync(CancellationToken ct)
    {
        var result = await RunShellCommandAsync("git remote get-url origin", ct).ConfigureAwait(false);
        if (!result.Success) return null;

        var url = result.Output.Trim();
        // 支持 https://github.com/owner/repo.git 和 git@github.com:owner/repo.git
        if (url.Contains("github.com"))
        {
            var parts = url.Split("github.com");
            if (parts.Length > 1)
            {
                var path = parts[1].TrimStart(':', '/', '.');
                path = path.TrimEnd('.', 'g', 'i', 't'); // 移除 .git
                if (path.EndsWith('/')) path = path[..^1];
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// 显示警告列表
    /// </summary>
    private static void ShowWarnings(List<string> warnings)
    {
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"{TerminalColors.Warning}⚠ 警告:{AnsiStyleConstants.Reset}");
        foreach (var warning in warnings)
        {
            TerminalHelper.WriteLine($"  • {warning}");
        }
        TerminalHelper.NewLine();
    }

    /// <summary>
    /// 选择仓库
    /// </summary>
    private static async Task<string?> ChooseRepoAsync(string currentRepo, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(currentRepo))
        {
            var items = new[]
            {
                ($"使用当前仓库: {currentRepo}", currentRepo),
                ("输入其他仓库", ""),
            };

            var selector = new Selector<(string Display, string Value)>(
                "选择仓库",
                items,
                i => i.Display);

            var result = await selector.ShowAsync(ct).ConfigureAwait(false);
            if (result.Cancelled) return null;

            if (!string.IsNullOrEmpty(result.Selected.Value))
            {
                return result.Selected.Value;
            }
        }

        // 手动输入仓库名
        // 非交互模式或测试环境返回 null，避免无限等待
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            return null;
        }
        else
        {
            TerminalHelper.WriteRaw("输入仓库名 (owner/repo): ");
            var input = TerminalHelper.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input)) return null;

            // 支持 URL 格式提取
            if (input.Contains("github.com"))
            {
                var parts = input.Split("github.com");
                if (parts.Length > 1)
                {
                    input = parts[1].TrimStart('/', ':');
                    input = input.TrimEnd('.', 'g', 'i', 't');
                    if (input.EndsWith('/')) input = input[..^1];
                }
            }

            return input;
        }
    }

    /// <summary>
    /// 选择工作流类型
    /// </summary>
    private static async Task<List<string>> SelectWorkflowsAsync(CancellationToken ct)
    {
        var items = new[]
        {
            ("claude - PR/Issue 评论触发的 Claude 助手", "claude"),
            ("claude-review - PR 创建时自动 Code Review", "claude-review"),
        };

        var selected = new List<string> { "claude", "claude-review" };

        var selector = new Selector<(string Display, string Value)>(
            "选择工作流类型",
            items,
            i => i.Display);

        var result = await selector.ShowAsync(ct).ConfigureAwait(false);
        if (!result.Cancelled && result.Selected.Value is not null)
        {
            selected = [result.Selected.Value];
        }

        return selected;
    }

    /// <summary>
    /// 选择 API Key 方式
    /// </summary>
    private static async Task<(string SecretName, string SecretValue, string AuthType)> ChooseApiKeyMethodAsync(CancellationToken ct)
    {
        var items = new List<(string Display, string SecretName, string AuthType)>
        {
            ("输入新的 API Key", ProviderEnvVarConstants.AnthropicApiKey, "api_key"),
            ("使用 OAuth Token", "CLAUDE_CODE_OAUTH_TOKEN", "oauth_token"),
        };

        // 检查是否已有本地 API Key
        var existingKey = Environment.GetEnvironmentVariable(ProviderEnvVar.AnthropicApiKey.ToValue())
            ?? Environment.GetEnvironmentVariable(JccEnvVar.ApiKey.ToValue());
        if (!string.IsNullOrEmpty(existingKey))
        {
            // 使用 Add + 反转构建顺序，避免 Insert(0, item) 的 O(n) 移动
            items.Add(("使用本地已有的 API Key", ProviderEnvVarConstants.AnthropicApiKey, "api_key"));
            items.Reverse();
        }

        var selector = new Selector<(string Display, string SecretName, string AuthType)>(
            "选择 API Key 方式",
            items.ToArray(),
            i => i.Display);

        var result = await selector.ShowAsync(ct).ConfigureAwait(false);
        if (result.Cancelled) return default;

        var choice = result.Selected;

        if (choice.AuthType == "api_key" && string.IsNullOrEmpty(existingKey))
        {
            // 需要手动输入 API Key
            TerminalHelper.WriteRaw("输入 API Key: ");
            var key = ReadMaskedInput();
            if (string.IsNullOrEmpty(key)) return default;
            return (choice.SecretName, key, choice.AuthType);
        }

        if (choice.AuthType == "oauth_token")
        {
            TerminalHelper.NewLine();
            TerminalHelper.WriteLine($"{TerminalColors.Accent}OAuth 认证流程:{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine("  1. 浏览器将打开 Claude 授权页面");
            TerminalHelper.WriteLine("  2. 授权后复制 Token 粘贴到此处");
            TerminalHelper.NewLine();
            TerminalHelper.WriteRaw("输入 OAuth Token: ");
            var token = ReadMaskedInput();
            if (string.IsNullOrEmpty(token)) return default;
            return (choice.SecretName, token, choice.AuthType);
        }

        // 使用已有 Key
        return (choice.SecretName, existingKey!, choice.AuthType);
    }

    /// <summary>
    /// 读取掩码输入（密码风格）
    /// </summary>
    private static string ReadMaskedInput()
    {
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            return string.Empty;
        }
        else
        {
            var input = new StringBuilder();
            while (true)
            {
                var key = TerminalHelper.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (input.Length > 0)
                    {
                        input.Remove(input.Length - 1, 1);
                        TerminalHelper.WriteRaw("\b \b");
                    }
                }
                else if (key.Key == ConsoleKey.Escape)
                {
                    return string.Empty;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    input.Append(key.KeyChar);
                    TerminalHelper.WriteRaw('*');
                }
            }
            TerminalHelper.NewLine();
            return input.ToString();
        }
    }

    /// <summary>
    /// 执行 GitHub Actions 设置
    /// </summary>
    private static async Task<GitHubSetupResult> SetupGitHubActionsAsync(
        string repoName,
        List<string> workflows,
        string secretName,
        string secretValue,
        string authType,
        CancellationToken ct)
    {
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine("正在设置 GitHub Actions...");

        var steps = workflows.Count > 0
            ? new[] { "获取仓库信息", "创建分支", "创建工作流文件", $"设置 {secretName} Secret", "打开 Pull Request" }
            : new[] { "获取仓库信息", $"设置 {secretName} Secret" };

        for (var i = 0; i < steps.Length; i++)
        {
            ct.ThrowIfCancellationRequested();

            TerminalHelper.WriteLine($"  {TerminalColors.Muted}...{AnsiStyleConstants.Reset} {steps[i]}");

            var success = i switch
            {
                0 => await VerifyRepoAsync(repoName, ct).ConfigureAwait(false),
                1 when workflows.Count > 0 => await CreateWorkflowBranchAsync(repoName, workflows, secretName, authType, ct).ConfigureAwait(false),
                2 when workflows.Count > 0 => true, // 工作流文件在创建分支时一起处理
                _ => true,
            };

            // 设置 Secret
            if ((workflows.Count > 0 && i == 3) || (workflows.Count == 0 && i == 1))
            {
                success = await SetSecretAsync(repoName, secretName, secretValue, ct).ConfigureAwait(false);
            }

            // 打开 PR
            if (workflows.Count > 0 && i == 4)
            {
                await OpenPullRequestAsync(repoName, ct).ConfigureAwait(false);
                success = true;
            }

            if (!success)
            {
                return GitHubSetupResult.Fail($"{steps[i]}失败", "请检查 GitHub CLI 认证状态和仓库权限");
            }

            TerminalHelper.WriteLine($"  {TerminalColors.Success}✓{AnsiStyleConstants.Reset} {steps[i]}");
        }

        return GitHubSetupResult.Ok();
    }

    /// <summary>
    /// 验证仓库是否存在且有权限
    /// </summary>
    private static async Task<bool> VerifyRepoAsync(string repoName, CancellationToken ct)
    {
        var result = await RunShellCommandAsync($"gh api repos/{repoName} --jq .permissions.admin", ct).ConfigureAwait(false);
        return result.Success && result.Output.Trim() == "true";
    }

    /// <summary>
    /// 创建工作流分支和文件
    /// </summary>
    private static async Task<bool> CreateWorkflowBranchAsync(
        string repoName,
        List<string> workflows,
        string secretName,
        string authType,
        CancellationToken ct)
    {
        // 获取默认分支
        var branchResult = await RunShellCommandAsync($"gh api repos/{repoName} --jq .default_branch", ct).ConfigureAwait(false);
        if (!branchResult.Success) return false;

        var defaultBranch = branchResult.Output.Trim();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var newBranch = $"add-claude-github-actions-{timestamp}";

        // 获取默认分支的 SHA
        var shaResult = await RunShellCommandAsync(
            $"gh api repos/{repoName}/git/ref/heads/{defaultBranch} --jq .object.sha",
            ct).ConfigureAwait(false);
        if (!shaResult.Success) return false;

        var sha = shaResult.Output.Trim();

        // 创建新分支
        var createBranch = await RunShellCommandAsync(
            $"gh api repos/{repoName}/git/refs -f ref=refs/heads/{newBranch} -f sha={sha}",
            ct).ConfigureAwait(false);
        if (!createBranch.Success) return false;

        // 创建工作流文件
        foreach (var workflow in workflows)
        {
            var (fileName, content) = GetWorkflowContent(workflow, secretName, authType);
            var base64Content = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));

            var createFile = await RunShellCommandAsync(
                $"gh api repos/{repoName}/contents/.github/workflows/{fileName} -X PUT -f message=\"Add {workflow} workflow\" -f content=\"{base64Content}\" -f branch={newBranch}",
                ct).ConfigureAwait(false);

            if (!createFile.Success) return false;
        }

        return true;
    }

    /// <summary>
    /// 获取工作流文件内容
    /// </summary>
    private static (string FileName, string Content) GetWorkflowContent(string workflow, string secretName, string authType)
    {
        var secretRef = authType == "oauth_token"
            ? "claude_code_oauth_token: ${{ secrets.CLAUDE_CODE_OAUTH_TOKEN }}"
            : $"anthropic_api_key: ${{ secrets.{secretName} }}";

        return workflow switch
        {
            "claude" => ("claude.yml", $"""
name: Claude
on:
  issue_comment:
    types: [created]
  pull_request_review_comment:
    types: [created]
  issues:
    types: [opened, assigned]
  pull_request_review:
    types: [submitted]

jobs:
  claude:
    if: |
      (github.event_name == 'issue_comment' && contains(github.event.comment.body, '@claude')) ||
      (github.event_name == 'pull_request_review_comment' && contains(github.event.comment.body, '@claude')) ||
      (github.event_name == 'pull_request_review' && contains(github.event.review.body, '@claude')) ||
      (github.event_name == 'issues' && contains(github.event.issue.body, '@claude'))
    runs-on: ubuntu-latest
    steps:
      - uses: anthropics/claude-code-base-action@v1
        with:
          {secretRef}
"""),
            "claude-review" => ("claude-review.yml", $"""
name: Claude Review
on:
  pull_request:
    types: [opened, synchronize, ready_for_review, reopened]

jobs:
  claude-review:
    if: github.event.pull_request.draft == false
    runs-on: ubuntu-latest
    steps:
      - uses: anthropics/claude-code-base-action@v1
        with:
          {secretRef}
"""),
            _ => ("claude.yml", ""),
        };
    }

    /// <summary>
    /// 设置 GitHub Secret
    /// </summary>
    private static async Task<bool> SetSecretAsync(string repoName, string secretName, string secretValue, CancellationToken ct)
    {
        var result = await RunShellCommandAsync(
            $"gh secret set {secretName} --body \"{secretValue}\" --repo {repoName}",
            ct).ConfigureAwait(false);
        return result.Success;
    }

    /// <summary>
    /// 打开 Pull Request 页面
    /// </summary>
    private static async Task OpenPullRequestAsync(string repoName, CancellationToken ct)
    {
        _ = await RunShellCommandAsync(
            $"gh browse repos/{repoName}/compare/add-claude-github-actions",
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 显示成功信息
    /// </summary>
    private static void ShowSuccess(string repoName, bool hasWorkflow)
    {
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"{TerminalColors.Success}✓ GitHub Actions 设置成功！{AnsiStyleConstants.Reset}");
        TerminalHelper.NewLine();

        if (hasWorkflow)
        {
            TerminalHelper.WriteLine("后续步骤:");
            TerminalHelper.WriteLine($"  1. 在浏览器中查看并合并 Pull Request");
            TerminalHelper.WriteLine($"  2. 确保已安装 Claude GitHub App: {TerminalColors.Accent}https://github.com/apps/claude{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"  3. 合并 PR 后工作流将自动启用");
        }
        else
        {
            TerminalHelper.WriteLine("后续步骤:");
            TerminalHelper.WriteLine($"  1. 确保已安装 Claude GitHub App: {TerminalColors.Accent}https://github.com/apps/claude{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"  2. API Key 已配置到仓库 Secret");
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"仓库: {TerminalColors.Accent}{repoName}{AnsiStyleConstants.Reset}");
    }

    /// <summary>
    /// 显示错误信息
    /// </summary>
    private static void ShowError(string message, string? fixHint)
    {
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"{TerminalColors.Error}✗ 设置失败: {message}{AnsiStyleConstants.Reset}");
        if (!string.IsNullOrEmpty(fixHint))
        {
            TerminalHelper.WriteLine($"  修复: {fixHint}");
        }
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine($"手动设置文档: {TerminalColors.Accent}https://docs.anthropic.com/en/docs/claude-code/github-actions{AnsiStyleConstants.Reset}");
    }

    /// <summary>
    /// 执行 Shell 命令
    /// </summary>
    private static async Task<ShellResult> RunShellCommandAsync(string command, CancellationToken ct, IProcessService? processService = null)
    {
        try
        {
            if (processService is not null)
            {
                var options = new ProcessOptions
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}"
                };

                var result = await processService.ExecuteAsync(options, ct).ConfigureAwait(false);
                return result.Success
                    ? ShellResult.Ok(result.StandardOutput)
                    : ShellResult.Fail(result.StandardError);
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return ShellResult.Fail("无法启动进程");

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            return process.ExitCode == 0
                ? ShellResult.Ok(output)
                : ShellResult.Fail(error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ShellResult.Fail(ex.Message);
        }
    }

    private sealed record ShellResult(bool Success, string Output, string Error)
    {
        public static ShellResult Ok(string output) => new(true, output, "");
        public static ShellResult Fail(string error) => new(false, "", error);
    }

    private sealed record GitHubCheckResult(bool Success, string? ErrorMessage, string? FixHint, string CurrentRepo, List<string> Warnings)
    {
        public static GitHubCheckResult Ok(string currentRepo, List<string> warnings) => new(true, null, null, currentRepo, warnings);
        public static GitHubCheckResult Fail(string errorMessage, string fixHint) => new(false, errorMessage, fixHint, "", []);
    }

    private sealed record GitHubSetupResult(bool Success, string? ErrorMessage, string? FixHint)
    {
        public static GitHubSetupResult Ok() => new(true, null, null);
        public static GitHubSetupResult Fail(string errorMessage, string fixHint) => new(false, errorMessage, fixHint);
    }
}
