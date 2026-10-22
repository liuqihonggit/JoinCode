namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// Phase 6 E2E 脚本 — TASK065 第六波 P0 bug 回归锁定
/// 锁定第三/五波修复的 4 类 bug: /vendor catalog 崩溃降级、/mcp add 子命令识别、/model 别名透传直切、/effort max 降级
/// 断言文案全部对齐命令源码真实输出（VendorCommand/McpCommand/ModelCommand/EffortCommand）
/// </summary>
public static partial class ChatCommandConversationScripts {
    /// <summary>
    /// /vendor 命令（无参） — 列出可用供应商
    /// 对齐 VendorCommand.cs 无参分支: "=== 可用供应商 ===" + 枚举值逐行输出（"  openai ← 当前"）
    /// </summary>
    public static ConversationScript VendorCommand => new() {
        Name = "/vendor 命令（无参列表）",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/vendor",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "可用供应商", Description = "应输出可用供应商列表标题" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "openai", Description = "列表应包含 openai 供应商（VendorKind 枚举遍历）" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /vendor deepseek 命令 — 切换供应商
    /// 对齐 VendorCommand.cs 切换分支: "已切换供应商: openai → deepseek"（E2E 环境 JCC_VENDOR=openai，catalog 全装配不崩溃）
    /// </summary>
    public static ConversationScript VendorSwitchDeepSeekCommand => new() {
        Name = "/vendor deepseek 命令（切换供应商）",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/vendor deepseek",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "已切换供应商", Description = "应输出切换成功文案" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "deepseek", Description = "目标供应商 deepseek 应出现在切换输出中" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "未知供应商", Description = "deepseek 在 VendorKind 枚举内，不应触发未知供应商拒绝" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "切换不应有错误（catalog 崩溃修复回归锁定）" },
                ]
            }
        ]
    };

    /// <summary>
    /// /mcp add 命令（缺少 url/command 参数） — AddServerAsync 用法提示分支
    /// 对齐 McpCommand.cs AddServerAsync: args.Length&lt;3 时输出 "用法: /mcp add <name> <command|url> ..."
    /// 锁定 CrudAction.Add 识别修复 — 修复前 add 走 default 分支输出 "未知操作: add"
    /// </summary>
    public static ConversationScript McpAddUsageCommand => new() {
        Name = "/mcp add 命令（用法提示）",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/mcp add testsvr",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "用法: /mcp add", Description = "应输出 add 子命令用法提示（AddServerAsync 分支）" },
                    new OutputAssert { Type = AssertType.NotContainsText, Expected = "未知操作", Description = "add 应被 CrudAction.Add case 识别，不应走 default 未知操作分支（bug 回归锁定）" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /model 命令（带参直切） — 别名透传直切
    /// 对齐 ModelCommand.cs SwitchToModelAsync: "已切换模型: gpt-4o"（E2E settings.json openai 无别名表，ResolveAlias 返回 null 透传输入）
    /// </summary>
    public static ConversationScript ModelSwitchCommand => new() {
        Name = "/model gpt-4o 命令（带参直切）",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/model gpt-4o",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "已切换模型: gpt-4o", Description = "应输出直切成功文案（catalog 可用，别名透传）" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "切换不应有错误（catalog 崩溃修复回归锁定）" },
                ]
            }
        ]
    };

    /// <summary>
    /// /effort 命令（设置形态） — 设置推理力度 high
    /// 对齐 EffortCommand.cs 设置分支: "推理力度: high (全面，广泛测试)"
    /// </summary>
    public static ConversationScript EffortSetHighCommand => new() {
        Name = "/effort high 命令（设置推理力度）",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/effort high",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "推理力度: high", Description = "应输出 high 力度设置成功文案" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "全面，广泛测试", Description = "应输出 high 等级对应描述（GetEffortDescription）" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };
}
