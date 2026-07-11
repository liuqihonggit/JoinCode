namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// Phase 5 E2E 脚本 — 补齐剩余未覆盖命令
/// 包含: generate/peers/install-github-app
/// </summary>
public static partial class ChatCommandConversationScripts
{
    /// <summary>
    /// /generate 命令 — 生成代码
    /// 无参数时 LogWarning 无终端输出,使用 NonInteractive 模式避免等待 AI 响应
    /// </summary>
    public static ConversationScript GenerateCommand => new()
    {
        Name = "/generate 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/generate",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /peers 命令 — 列出对等节点
    /// 服务未注册时输出"对等节点发现服务未初始化"
    /// </summary>
    public static ConversationScript PeersCommand => new()
    {
        Name = "/peers 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/peers",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "对等节点状态"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "对等节点", Description = "应包含对等节点相关信息" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /install-github-app 命令 — Interactive 模式
    /// CI 环境无 gh CLI，命令输出"GitHub CLI 未安装"后返回
    /// </summary>
    public static ConversationScript InstallGitHubAppCommand => new()
    {
        Name = "/install-github-app 命令",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/install-github-app",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "GitHub", Description = "应包含 GitHub 相关信息" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };
}
