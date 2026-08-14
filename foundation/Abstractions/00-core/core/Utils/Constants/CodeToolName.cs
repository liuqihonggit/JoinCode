namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 代码相关工具名称枚举（CodeIndex + LSP + 代码执行 + 分析 + 生成）
/// </summary>
public enum CodeToolName
{
    [EnumValue("code_index_search")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexSearch,

    [EnumValue("code_index_search_comprehensive")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexSearchComprehensive,

    [EnumValue("code_index_find_definition")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexFindDefinition,

    [EnumValue("code_index_find_references")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexFindReferences,

    [EnumValue("code_index_get_callers")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexGetCallers,

    [EnumValue("code_index_get_callees")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexGetCallees,

    [EnumValue("code_index_get_call_chain")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexGetCallChain,

    [EnumValue("code_index_get_impact_scope")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexGetImpactScope,

    [EnumValue("code_index_get_inheritors")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexGetInheritors,

    [EnumValue("code_index_get_dependencies")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexGetDependencies,

    [EnumValue("code_index_get_affected_files")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexGetAffectedFiles,

    [EnumValue("code_index_rebuild")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    CodeIndexRebuild,

    [EnumValue("code_index_stats")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexStats,

    [EnumValue("code_index_explore")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexExplore,

    [EnumValue("code_index_get_project_deps")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexGetProjectDeps,

    [EnumValue("code_index_get_project_dependents")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexGetProjectDependents,

    [EnumValue("code_index_get_affected_projects")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexGetAffectedProjects,

    [EnumValue("code_index_get_project_nugets")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexGetProjectNuGets,

    [EnumValue("code_index_get_nuget_projects")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexGetNuGetProjects,

    [EnumValue("code_index_get_all_projects")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    CodeIndexGetAllProjects,

    [EnumValue("lsp_goto_definition")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    LspGotoDefinition,

    [EnumValue("lsp_find_references")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    LspFindReferences,

    [EnumValue("lsp_hover")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    LspHover,

    [EnumValue("lsp_completion")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    LspCompletion,

    [EnumValue("lsp_document_symbols")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    LspDocumentSymbols,

    [EnumValue("lsp_workspace_symbol")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    LspWorkspaceSymbol,

    [EnumValue("lsp_goto_implementation")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    LspGotoImplementation,

    [EnumValue("lsp_prepare_call_hierarchy")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    LspPrepareCallHierarchy,

    [EnumValue("lsp_incoming_calls")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    LspIncomingCalls,

    [EnumValue("lsp_outgoing_calls")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    LspOutgoingCalls,

    [EnumValue("LSP")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    LSP,

    [EnumValue("execute_csharp_code")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    ExecuteCsharpCode,

    [EnumValue("evaluate_expression")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    EvaluateExpression,

    [EnumValue("test_code_snippet")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    TestCodeSnippet,

    [EnumValue("analyze_csharp_code")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    AnalyzeCsharpCode,

    [EnumValue("find_bugs")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    FindBugs,

    [EnumValue("optimize_code")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    OptimizeCode,

    [EnumValue("security_audit")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    SecurityAudit,

    [EnumValue("generate_csharp_code")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GenerateCsharpCode,

    [EnumValue("generate_unit_test")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GenerateUnitTest,

    [EnumValue("generate_api_controller")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GenerateApiController,

    [EnumValue("graph_detect_communities")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GraphDetectCommunities,

    [EnumValue("graph_get_hub_nodes")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GraphGetHubNodes,

    [EnumValue("graph_detect_dead_code")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GraphDetectDeadCode,

    [EnumValue("graph_extract_subgraph")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GraphExtractSubgraph,

    [EnumValue("graph_analyze_change_impact")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GraphAnalyzeChangeImpact,

    [EnumValue("graph_save")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GraphSave,

    [EnumValue("graph_load")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GraphLoad,

    [EnumValue("graph_export_dot")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GraphExportDot,

    [EnumValue("graph_export_html")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GraphExportHtml,

    [EnumValue("graph_query")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GraphQuery,

    [EnumValue("graph_path")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GraphPath,

    [EnumValue("graph_explain")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GraphExplain,

    [EnumValue("graph_register")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GraphRegister,

    [EnumValue("graph_unregister")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GraphUnregister,

    [EnumValue("graph_repos")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    GraphRepos,

    [EnumValue("graph_export_wiki")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    GraphExportWiki,
}
