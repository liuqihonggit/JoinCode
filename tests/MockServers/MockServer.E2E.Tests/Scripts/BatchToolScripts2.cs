using System;

namespace MockServer.E2E.Tests.Scripts;

/// <summary>
/// 代码搜索 + LSP 工具批量 E2E 测试脚本
/// </summary>
public static class BatchCodeToolScripts
{
    public static ConversationScript CodeToolsBatch => new()
    {
        Name = "代码搜索工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我搜索代码库",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "Grep", Arguments = """{"pattern":"class ChatService"}""" },
                        new() { ToolName = "Glob", Arguments = """{"pattern":"**/*.cs"}""" },
                        new() { ToolName = "Read", Arguments = """{"file_path":"src/Program.cs"}""" },
                        new() { ToolName = "SearchCodebase", Arguments = """{"query":"ChatService"}""" },
                        new() { ToolName = "search_code", Arguments = """{"query":"ProcessUserInput"}""" },
                        new() { ToolName = "search_files", Arguments = """{"pattern":"*.cs"}""" },
                    ],
                    FollowUpText = "代码搜索已完成。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "Grep", Description = "应包含Grep" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "Glob", Description = "应包含Glob" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "Read", Description = "应包含Read" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "SearchCodebase", Description = "应包含SearchCodebase" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "search_code", Description = "应包含search_code" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "search_files", Description = "应包含search_files" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// Notebook 工具批量 E2E 测试脚本
/// </summary>
public static class BatchNotebookToolScripts
{
    public static ConversationScript NotebookToolsBatch => new()
    {
        Name = "Notebook工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我操作notebook",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "notebook_create", Arguments = """{"name":"test.ipynb"}""" },
                        new() { ToolName = "notebook_read", Arguments = """{"path":"test.ipynb"}""" },
                        new() { ToolName = "notebook_add_cell", Arguments = """{"path":"test.ipynb","cell_type":"code","source":"print(\"hello\")"}""" },
                        new() { ToolName = "notebook_delete_cell", Arguments = """{"path":"test.ipynb","index":0}""" },
                        new() { ToolName = "notebook_clear_outputs", Arguments = """{"path":"test.ipynb"}""" },
                    ],
                    FollowUpText = "Notebook操作已完成。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "notebook_create", Description = "应包含notebook_create" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "notebook_read", Description = "应包含notebook_read" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "notebook_add_cell", Description = "应包含notebook_add_cell" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "notebook_delete_cell", Description = "应包含notebook_delete_cell" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "notebook_clear_outputs", Description = "应包含notebook_clear_outputs" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// Worktree 工具批量 E2E 测试脚本
/// </summary>
public static class BatchWorktreeToolScripts
{
    public static ConversationScript WorktreeToolsBatch => new()
    {
        Name = "Worktree工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我管理worktree",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "worktree_list", Arguments = "{}" },
                        new() { ToolName = "worktree_status", Arguments = "{}" },
                        new() { ToolName = "worktree_find_git", Arguments = "{}" },
                        new() { ToolName = "worktree_list_all", Arguments = "{}" },
                    ],
                    FollowUpText = "Worktree操作已完成。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "worktree_list", Description = "应包含worktree_list" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "worktree_status", Description = "应包含worktree_status" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "worktree_find_git", Description = "应包含worktree_find_git" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "worktree_list_all", Description = "应包含worktree_list_all" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// Workflow 工具批量 E2E 测试脚本
/// </summary>
public static class BatchWorkflowToolScripts
{
    public static ConversationScript WorkflowToolsBatch => new()
    {
        Name = "Workflow工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我管理工作流",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "workflow", Arguments = "{}" },
                        new() { ToolName = "workflow_status", Arguments = """{"id":"test-001"}""" },
                        new() { ToolName = "workflow_execute", Arguments = "{}" },
                    ],
                    FollowUpText = "Workflow操作已完成。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "workflow", Description = "应包含workflow" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "workflow_status", Description = "应包含workflow_status" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "workflow_execute", Description = "应包含workflow_execute" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// Skill 工具批量 E2E 测试脚本
/// </summary>
public static class BatchSkillToolScripts
{
    public static ConversationScript SkillToolsBatch => new()
    {
        Name = "Skill工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我管理技能",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "skill_list", Arguments = "{}" },
                        new() { ToolName = "skill_search", Arguments = """{"query":"test"}""" },
                        new() { ToolName = "skill_execute", Arguments = """{"name":"test-skill"}""" },
                    ],
                    FollowUpText = "Skill操作已完成。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "skill_list", Description = "应包含skill_list" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "skill_search", Description = "应包含skill_search" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "skill_execute", Description = "应包含skill_execute" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// Team 工具批量 E2E 测试脚本
/// </summary>
public static class BatchTeamToolScripts
{
    public static ConversationScript TeamToolsBatch => new()
    {
        Name = "Team工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我管理团队",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "team_list", Arguments = "{}" },
                        new() { ToolName = "team_get", Arguments = """{"team_id":"test-001"}""" },
                        new() { ToolName = "team_get_messages", Arguments = """{"team_id":"test-001"}""" },
                        new() { ToolName = "TeamCreate", Arguments = """{"name":"test-team"}""" },
                        new() { ToolName = "TeamDelete", Arguments = """{"team_id":"test-001"}""" },
                    ],
                    FollowUpText = "Team操作已完成。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "team_list", Description = "应包含team_list" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "team_get", Description = "应包含team_get" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "team_get_messages", Description = "应包含team_get_messages" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "TeamCreate", Description = "应包含TeamCreate" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "TeamDelete", Description = "应包含TeamDelete" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// Memory 工具批量 E2E 测试脚本
/// </summary>
public static class BatchMemoryToolScripts
{
    public static ConversationScript MemoryToolsBatch => new()
    {
        Name = "Memory工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我管理记忆",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "memory_scan", Arguments = "{}" },
                        new() { ToolName = "memory_age", Arguments = "{}" },
                        new() { ToolName = "memory_cleanup", Arguments = "{}" },
                        new() { ToolName = "memory_health", Arguments = "{}" },
                        new() { ToolName = "memory_search_history", Arguments = """{"query":"test"}""" },
                    ],
                    FollowUpText = "Memory操作已完成。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "memory_scan", Description = "应包含memory_scan" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "memory_age", Description = "应包含memory_age" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "memory_cleanup", Description = "应包含memory_cleanup" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "memory_health", Description = "应包含memory_health" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "memory_search_history", Description = "应包含memory_search_history" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// Todo 工具批量 E2E 测试脚本
/// </summary>
public static class BatchTodoToolScripts
{
    public static ConversationScript TodoToolsBatch => new()
    {
        Name = "Todo工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我管理待办",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "todo_list", Arguments = "{}" },
                        new() { ToolName = "todo_create", Arguments = """{"title":"test-todo"}""" },
                        new() { ToolName = "todo_read", Arguments = """{"id":"test-001"}""" },
                        new() { ToolName = "TodoWrite", Arguments = """{"content":"test"}""" },
                        new() { ToolName = "todo_delete", Arguments = """{"id":"test-001"}""" },
                    ],
                    FollowUpText = "Todo操作已完成。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "todo_list", Description = "应包含todo_list" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "todo_create", Description = "应包含todo_create" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "todo_read", Description = "应包含todo_read" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "TodoWrite", Description = "应包含TodoWrite" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "todo_delete", Description = "应包含todo_delete" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}

/// <summary>
/// MCP 工具批量 E2E 测试脚本
/// </summary>
public static class BatchMcpToolScripts
{
    public static ConversationScript McpToolsBatch => new()
    {
        Name = "MCP工具批量测试",
        Mode = ConversationMode.Interactive,
        Turns =
        [
            new ConversationTurn
            {
                UserInput = "帮我管理MCP",
                AiResponse = new MockResponseScript
                {
                    Type = MockResponseType.WithToolCalls,
                    TextResponse = "",
                    ToolCalls =
                    [
                        new() { ToolName = "mcp_list_servers", Arguments = "{}" },
                        new() { ToolName = "mcp_list_tools", Arguments = "{}" },
                        new() { ToolName = "mcp_list_clients", Arguments = "{}" },
                        new() { ToolName = "mcp_auth_status", Arguments = "{}" },
                    ],
                    FollowUpText = "MCP操作已完成。"
                },
                Asserts =
                [
                    new() { Type = AssertType.ContainsToolCall, Expected = "mcp_list_servers", Description = "应包含mcp_list_servers" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "mcp_list_tools", Description = "应包含mcp_list_tools" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "mcp_list_clients", Description = "应包含mcp_list_clients" },
                    new() { Type = AssertType.ContainsToolCall, Expected = "mcp_auth_status", Description = "应包含mcp_auth_status" },
                    new() { Type = AssertType.HasAssistantResponse, Expected = "", Description = "应有回复" },
                ]
            }
        ]
    };
}