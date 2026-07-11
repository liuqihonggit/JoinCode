namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// Phase 3 E2E 脚本 — Platform/Social/Other 类命令
/// 包含: chrome/ide/desktop/mobile/btw/feedback/share/voice/stickers/simple
/// </summary>
public static partial class ChatCommandConversationScripts
{
    /// <summary>
    /// /chrome 命令 — Chrome 浏览器集成
    /// 服务未注册时无输出,仅断言 NoErrors
    /// </summary>
    public static ConversationScript ChromeCommand => new()
    {
        Name = "/chrome 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/chrome",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "Chrome状态"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /ide 命令 — IDE 集成管理
    /// 服务未注册时无输出,仅断言 NoErrors
    /// </summary>
    public static ConversationScript IdeCommand => new()
    {
        Name = "/ide 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/ide",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "IDE状态"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /desktop 命令 — 转移到桌面应用
    /// 服务未注册时无输出,仅断言 NoErrors
    /// </summary>
    public static ConversationScript DesktopCommand => new()
    {
        Name = "/desktop 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/desktop",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "桌面转移"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /mobile 命令 — 移动端连接
    /// 服务未注册时无输出,仅断言 NoErrors
    /// </summary>
    public static ConversationScript MobileCommand => new()
    {
        Name = "/mobile 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/mobile",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "移动端状态"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /btw 命令 — 侧边提问
    /// 无参数时输出用法提示 "用法: /btw <question>"
    /// </summary>
    public static ConversationScript BtwCommand => new()
    {
        Name = "/btw 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/btw",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "侧边提问"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "用法", Description = "/btw 无参数应输出用法提示" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /feedback 命令 — 提交反馈
    /// 无参数时 FeedbackRenderer 输出 "反馈" 标题和输入提示
    /// </summary>
    public static ConversationScript FeedbackCommand => new()
    {
        Name = "/feedback 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/feedback",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "反馈提示"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "反馈", Description = "/feedback 应输出反馈相关内容" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /share 命令 — 生成分享内容
    /// 输出 "生成分享内容..." 并展示对话历史预览
    /// </summary>
    public static ConversationScript ShareCommand => new()
    {
        Name = "/share 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/share",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "分享内容"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "分享", Description = "/share 应输出分享相关内容" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /voice 命令 — 语音输入模式
    /// 服务未注册时无输出,仅断言 NoErrors
    /// </summary>
    public static ConversationScript VoiceCommand => new()
    {
        Name = "/voice 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/voice",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "语音状态"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /stickers 命令 — 获取贴纸
    /// 服务未注册时无输出,仅断言 NoErrors
    /// </summary>
    public static ConversationScript StickersCommand => new()
    {
        Name = "/stickers 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/stickers",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "贴纸页面"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /simple 命令 — 切换精简模式
    /// 服务不可用时输出 "精简模式服务不可用"; 可用时切换并输出状态
    /// </summary>
    public static ConversationScript SimpleCommand => new()
    {
        Name = "/simple 命令",
        Mode = ConversationMode.NonInteractive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/simple",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = "精简模式切换"
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "精简模式", Description = "/simple 应输出精简模式状态" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };
}
