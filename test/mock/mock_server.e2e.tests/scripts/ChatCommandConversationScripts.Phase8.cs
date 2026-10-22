namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// Phase 8 E2E 脚本 — P2 隔离写命令补覆盖（TASK065 第六波）
/// 包含: /reset-config all（JCC_APP_DATA_FOLDER 隔离目录重置）、/code-index（小 fixture 目录控索引耗时）
/// </summary>
public static partial class ChatCommandConversationScripts {
    /// <summary>
    /// /code-index fixture 目录 — 由测试类创建/清理，脚本属性消费
    /// 指向 %TEMP% 下含 2 个小 .cs 文件的临时目录，控制 RebuildIndexAsync 耗时
    /// </summary>
    internal static string? CodeIndexFixtureDirectory { get; set; }

    /// <summary>
    /// /reset-config all 命令 — 重置隔离 AppData 配置（单轮）
    /// 隔离链路: DualRoleConversationRunner 设 JCC_APP_DATA_FOLDER=%TEMP%\jcc_test_{guid}，
    /// AppDataPaths.Default 解析为绝对路径后 ResetConfigCommand 的 Path.Combine(UserProfile, 绝对路径) 直接采用绝对路径 → 写入隔离目录，不碰真实 ~/.jcc
    /// 断言 ResetAllAsync 固定输出: "重置所有配置文件..."（首行）、"配置树已重置为默认骨架"（settings 重置）、"✓ 所有配置已重置"（收尾，颜色码不影响子串匹配）
    /// </summary>
    public static ConversationScript ResetConfigAllCommand => new() {
        Name = "/reset-config all 命令",
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/reset-config all",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "重置所有配置文件", Description = "应包含重置开始提示（ResetAllAsync 首行）" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "配置树已重置为默认骨架", Description = "应包含配置树重置输出（settings.json + current/ + providers_llm/ + providers_search/）" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "所有配置已重置", Description = "应包含重置完成提示（✓ 所有配置已重置）" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };

    /// <summary>
    /// /code-index 命令 — 显式重建 fixture 目录代码索引（单轮）
    /// WorkingDirectory 指向小 fixture（2 个 .cs 文件）控制索引耗时（Ast 集成测试 WorkingDirectory 先例）；
    /// 断言 CodeIndexCommand 固定输出: "正在构建代码索引..."（无条件首行）、"代码索引构建完成"（成功路径）、"文件数"（GetStatsAsync 统计行）
    /// </summary>
    /// <exception cref="InvalidOperationException">CodeIndexFixtureDirectory 未就绪时抛出 — 测试类须先设置 fixture 目录再访问本脚本</exception>
    public static ConversationScript CodeIndexCommand => new() {
        Name = "/code-index 命令",
        WorkingDirectory = CodeIndexFixtureDirectory
            ?? throw new InvalidOperationException(
                "[E2E-FIXTURE] /code-index 脚本的 CodeIndexFixtureDirectory 未就绪。" +
                "触发原因: 测试类未先创建 fixture 目录。" +
                "拦截位置: ChatCommandConversationScripts.CodeIndexCommand 属性 getter。" +
                "正确写法: 先在 ChatCommandP2IsolatedCoverageTests 中调用 CreateCodeIndexFixtureAsync() 并赋值 CodeIndexFixtureDirectory，再访问本脚本。"),
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "/code-index",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.TextOnly,
                    TextResponse = ""
                },
                Asserts =
                [
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "正在构建代码索引", Description = "应包含索引构建开始提示（无条件输出）" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "代码索引构建完成", Description = "应包含索引构建完成提示（含耗时 ms）" },
                    new OutputAssert { Type = AssertType.ContainsText, Expected = "文件数", Description = "应包含索引统计输出（文件数/符号数/调用边/依赖边/项目数）" },
                    new OutputAssert { Type = AssertType.NoErrors, Expected = "", Description = "不应有错误" },
                ]
            }
        ]
    };
}
