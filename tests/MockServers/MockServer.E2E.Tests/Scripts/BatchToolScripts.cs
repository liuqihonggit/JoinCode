using System;

namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// 文件操作工具批量 E2E 测试脚本
/// </summary>
public static class BatchFileToolScripts
{
    public static ConversationScript FileToolsBatch => new()
    {
        Name = "文件工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我执行多个文件操作",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "Read", Arguments = """{"file_path":"test.txt"}""" },
                        new() { ToolName = "Write", Arguments = """{"file_path":"test.txt","content":"hello"}""" },
                        new() { ToolName = "Edit", Arguments = """{"file_path":"test.txt","old_string":"hello","new_string":"world"}""" },
                        new() { ToolName = "file_edit_regex", Arguments = """{"file_path":"test.txt","pattern":"world","replacement":"test"}""" },
                        new() { ToolName = "file_insert_lines", Arguments = """{"file_path":"test.txt","line":1,"content":"line1"}""" },
                        new() { ToolName = "file_delete_lines", Arguments = """{"file_path":"test.txt","start":1,"end":1}""" },
                        new() { ToolName = "file_batch_edit", Arguments = """{"file_path":"test.txt","edits":[{"pattern":"test","replacement":"final"}]}""" },
                        new() { ToolName = "directory_list", Arguments = """{"path":"."}""" },
                        new() { ToolName = "file_list", Arguments = """{"pattern":"*.txt"}""" },
                        new() { ToolName = "file_snip_lines", Arguments = """{"file_path":"test.txt","start":1,"end":5}""" },
                    ],
                    FollowUpText = "10个文件操作已全部执行。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "应包含Read" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "Write", Description = "应包含Write" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "Edit", Description = "应包含Edit" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "file_edit_regex", Description = "应包含file_edit_regex" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "file_insert_lines", Description = "应包含file_insert_lines" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "file_delete_lines", Description = "应包含file_delete_lines" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "file_batch_edit", Description = "应包含file_batch_edit" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "directory_list", Description = "应包含directory_list" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "file_list", Description = "应包含file_list" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "file_snip_lines", Description = "应包含file_snip_lines" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// Shell 工具批量 E2E 测试脚本
/// </summary>
public static class BatchShellToolScripts
{
    public static ConversationScript ShellToolsBatch => new()
    {
        Name = "Shell工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我执行多个shell操作",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "Bash", Arguments = """{"command":"echo hello"}""" },
                        new() { ToolName = "PowerShell", Arguments = """{"command":"Get-Process"}""" },
                        new() { ToolName = "shell_check", Arguments = """{"command":"echo test"}""" },
                        new() { ToolName = "shell_background_get", Arguments = """{"id":"test-001"}""" },
                        new() { ToolName = "shell_background_list", Arguments = "{}" },
                        new() { ToolName = "shell_background_cancel", Arguments = """{"id":"test-001"}""" },
                    ],
                    FollowUpText = "Shell操作已执行。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "Bash", Description = "应包含Bash" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "PowerShell", Description = "应包含PowerShell" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "shell_check", Description = "应包含shell_check" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "shell_background_get", Description = "应包含shell_background_get" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "shell_background_list", Description = "应包含shell_background_list" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "shell_background_cancel", Description = "应包含shell_background_cancel" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// Git 工具批量 E2E 测试脚本
/// </summary>
public static class BatchGitToolScripts
{
    public static ConversationScript GitToolsBatch => new()
    {
        Name = "Git工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我执行多个git操作",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "git_status", Arguments = "{}" },
                        new() { ToolName = "git_log", Arguments = """{"max_count":3}""" },
                        new() { ToolName = "git_diff", Arguments = "{}" },
                        new() { ToolName = "git_branch", Arguments = "{}" },
                        new() { ToolName = "git_add", Arguments = """{"files":["test.txt"]}""" },
                        new() { ToolName = "git_reset", Arguments = """{"hard":false}""" },
                        new() { ToolName = "git_clean", Arguments = """{"dry_run":true}""" },
                    ],
                    FollowUpText = "Git操作已执行。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "git_status", Description = "应包含git_status" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "git_log", Description = "应包含git_log" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "git_diff", Description = "应包含git_diff" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "git_branch", Description = "应包含git_branch" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "git_add", Description = "应包含git_add" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "git_reset", Description = "应包含git_reset" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "git_clean", Description = "应包含git_clean" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// 交互+配置工具批量 E2E 测试脚本
/// </summary>
public static class BatchInteractionToolScripts
{
    public static ConversationScript InteractionToolsBatch => new()
    {
        Name = "交互工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我查询配置和权限",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "Config", Arguments = "{}" },
                        new() { ToolName = "config_get", Arguments = """{"key":"model"}""" },
                        new() { ToolName = "auth_get_status", Arguments = "{}" },
                        new() { ToolName = "ask_user", Arguments = """{"question":"请确认？","options":[{"label":"是","description":"确认"}]}""" },
                    ],
                    FollowUpText = "配置和权限已查询。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "Config", Description = "应包含Config" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "config_get", Description = "应包含config_get" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "auth_get_status", Description = "应包含auth_get_status" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "ask_user", Description = "应包含ask_user" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// Agent 管理工具批量 E2E 测试脚本
/// </summary>
public static class BatchAgentToolScripts
{
    public static ConversationScript AgentToolsBatch => new()
    {
        Name = "Agent工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我管理agent",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "agent_list", Arguments = "{}" },
                        new() { ToolName = "agent_status", Arguments = """{"agent_id":"test-001"}""" },
                        new() { ToolName = "agent_get_messages", Arguments = """{"agent_id":"test-001"}""" },
                        new() { ToolName = "agent_stats", Arguments = "{}" },
                        new() { ToolName = "agent_history", Arguments = """{"agent_id":"test-001"}""" },
                        new() { ToolName = "agent_running", Arguments = "{}" },
                        new() { ToolName = "agent_system_stats", Arguments = "{}" },
                        new() { ToolName = "list_agents", Arguments = "{}" },
                    ],
                    FollowUpText = "Agent操作已执行。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "agent_list", Description = "应包含agent_list" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "agent_status", Description = "应包含agent_status" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "agent_get_messages", Description = "应包含agent_get_messages" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "agent_stats", Description = "应包含agent_stats" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "agent_history", Description = "应包含agent_history" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "agent_running", Description = "应包含agent_running" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "agent_system_stats", Description = "应包含agent_system_stats" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "list_agents", Description = "应包含list_agents" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// 搜索工具批量 E2E 测试脚本
/// </summary>
public static class BatchSearchToolScripts
{
    public static ConversationScript SearchToolsBatch => new()
    {
        Name = "搜索工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我搜索代码",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "search_code", Arguments = """{"query":"class"}""" },
                        new() { ToolName = "search_text", Arguments = """{"pattern":"TODO"}""" },
                        new() { ToolName = "search_files", Arguments = """{"pattern":"*.cs"}""" },
                        new() { ToolName = "SearchCodebase", Arguments = """{"query":"ChatService"}""" },
                        new() { ToolName = "code_search", Arguments = """{"query":"interface"}""" },
                        new() { ToolName = "symbol_search", Arguments = """{"symbol":"Main"}""" },
                    ],
                    FollowUpText = "搜索已完成。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ToolCallSucceeded, Expected = "search_code", Description = "search_code应执行成功" },
                    new() { Type = AssertType.ToolCallSucceeded, Expected = "search_text", Description = "search_text应执行成功" },
                    new() { Type = AssertType.ToolCallSucceeded, Expected = "search_files", Description = "search_files应执行成功" },
                    new() { Type = AssertType.ToolCallSucceeded, Expected = "SearchCodebase", Description = "SearchCodebase应执行成功" },
                    new() { Type = AssertType.ToolCallSucceeded, Expected = "code_search", Description = "code_search应执行成功" },
                    new() { Type = AssertType.ToolCallSucceeded, Expected = "symbol_search", Description = "symbol_search应执行成功" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// Plan 模式工具批量 E2E 测试脚本
/// </summary>
public static class BatchPlanToolScripts
{
    public static ConversationScript PlanToolsBatch => new()
    {
        Name = "Plan工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我管理计划模式",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "plan_mode_status", Arguments = "{}" },
                        new() { ToolName = "get_plan_status", Arguments = "{}" },
                        new() { ToolName = "add_plan_step", Arguments = """{"step":"step1"}""" },
                        new() { ToolName = "get_plan_history", Arguments = "{}" },
                    ],
                    FollowUpText = "Plan操作已执行。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "plan_mode_status", Description = "应包含plan_mode_status" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "get_plan_status", Description = "应包含get_plan_status" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "add_plan_step", Description = "应包含add_plan_step" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "get_plan_history", Description = "应包含get_plan_history" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// System 工具批量 E2E 测试脚本
/// </summary>
public static class BatchSystemToolScripts
{
    public static ConversationScript SystemToolsBatch => new()
    {
        Name = "System工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我执行系统操作",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "Brief", Arguments = "{}" },
                        new() { ToolName = "brief_status", Arguments = "{}" },
                        new() { ToolName = "Sleep", Arguments = """{"ms":100}""" },
                        new() { ToolName = "TaskOutput", Arguments = """{"task_id":"test-001"}""" },
                        new() { ToolName = "ToolSearch", Arguments = """{"query":"read"}""" },
                        new() { ToolName = "StructuredOutput", Arguments = """{"schema_name":"test"}""" },
                        new() { ToolName = "goal_get", Arguments = "{}" },
                        new() { ToolName = "send_user_file", Arguments = """{"path":"test.txt","content":"test"}""" },
                    ],
                    FollowUpText = "系统操作已执行。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "Brief", Description = "应包含Brief" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "brief_status", Description = "应包含brief_status" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "Sleep", Description = "应包含Sleep" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "TaskOutput", Description = "应包含TaskOutput" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "ToolSearch", Description = "应包含ToolSearch" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "StructuredOutput", Description = "应包含StructuredOutput" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "goal_get", Description = "应包含goal_get" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "send_user_file", Description = "应包含send_user_file" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}