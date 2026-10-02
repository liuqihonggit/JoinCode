#!/usr/bin/env python3
"""批量替换工具名称 — code_index_* → search_*, graph_* → search_*"""

import re

FILE = "D:/project/w2/lib/abstractions/abs_core/core_utils/constants/tool_names/CodeToolName.cs"

# 替换映射（旧值 → 新值），按长度降序排列避免部分匹配
REPLACEMENTS = [
    ("code_index_search_comprehensive", "search_comprehensive"),
    ("code_index_search_semantic", "search_semantic"),
    ("code_index_get_project_dependents", "search_project_dependents"),
    ("code_index_get_affected_projects", "search_affected_projects"),
    ("code_index_get_project_nugets", "search_project_nugets"),
    ("code_index_get_nuget_projects", "search_nuget_projects"),
    ("code_index_get_all_projects", "search_all_projects"),
    ("code_index_get_project_deps", "search_project_deps"),
    ("code_index_get_affected_files", "search_affected_files"),
    ("code_index_get_impact_scope", "search_impact_scope"),
    ("code_index_get_inheritors", "search_inheritors"),
    ("code_index_get_dependencies", "search_dependencies"),
    ("code_index_get_call_chain", "search_call_chain"),
    ("code_index_get_callers", "search_callers"),
    ("code_index_get_callees", "search_callees"),
    ("code_index_find_definition", "search_definition"),
    ("code_index_find_references", "search_references"),
    ("code_index_search", "search_symbol"),
    ("code_index_explore", "search_explore"),
    ("code_index_rebuild", "create_index"),
    ("code_index_stats", "search_stats"),
    ("graph_extract_subgraph", "search_subgraph"),
    ("graph_query", "search_graph"),
    ("graph_explain", "search_explain"),
    ("graph_path", "search_path"),
]

with open(FILE, "r", encoding="utf-8") as f:
    content = f.read()

count = 0
for old, new in REPLACEMENTS:
    pattern = f'[EnumValue("{old}")]'
    replacement = f'[EnumValue("{new}")]'
    if pattern in content:
        content = content.replace(pattern, replacement)
        count += 1
        print(f"  {old} → {new}")
    else:
        print(f"  SKIP {old} (not found)")

with open(FILE, "w", encoding="utf-8") as f:
    f.write(content)

print(f"\n替换完成: {count}/{len(REPLACEMENTS)}")
