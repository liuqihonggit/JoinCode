"""文档→域自动分类脚本

根据 DSG040 §4.2 的关键词映射规则，自动分类 docs/ 下的文档。
输出迁移计划 JSON，人工审查后执行。
"""

import os
import re
import json
from pathlib import Path

REPO_ROOT = Path(r"D:\project\w1")
DOCS_DIR = REPO_ROOT / "docs"

# 16 功能域
DOMAINS = [
    "generators", "collections", "persistence", "async_primitives",
    "abstractions", "guard", "storage", "plugins", "mcp", "llm",
    "cognition", "perception", "services", "composition", "apps", "tools",
]

# 关键词→域映射（按优先级，先匹配的优先）
KEYWORD_MAP = [
    # 明确关键词
    (r"structura|frozen_dict|frozen_set|hamt|swiss.?table|skip.?list|immutable.?dag", "collections"),
    (r"pithos|lsm.?tree|inmemory.?index.?store", "persistence"),
    (r"async.?lock|asynclock|bcl.?bridge|transport|mailbox|deadlock|actor.?mailbox|actor.?refactor|actor.?supervision|ring.?buffer|spsc", "async_primitives"),
    (r"abstractions|infrastructure|config.?loading|bcl", "abstractions"),
    (r"guard|command.?interception|safety|defense.?in.?depth|prefix.?tree|dangerous.?command", "guard"),
    (r"vault|scheduling|clock|downloader|range.?download|update.?source", "storage"),
    (r"plugin|effect.?scope|native.?dll", "plugins"),
    (r"mcp|tool.?dispatch|tool.?health|tool.?map|tool.?coverage|streamable.?http", "mcp"),
    (r"\bllm\b|reasoning|agent|subagent|sub.?agent|stall.?defense|output.?loop|small.?model|deepseek|responses.?api|jcc3016", "llm"),
    (r"prompt|brain|hand|cognition|context.?compaction|tool.?progressive|interaction.?tool|tool.?name.?cleanup|entropy|decision.?model|jev", "cognition"),
    (r"eye|vision|code.?index|vector.?index|vecidx|embedding|onnx|perception|rerank|hnsw|search.?engine|mmap.?span", "perception"),
    (r"bridge|browser|dream|sandbox|update|service|satellite|bridgemain", "services"),
    (r"composition|pipeline|slash|compose", "composition"),
    (r"gui|tui|desktop|computer.?use|window.?shake|dock|vscode.?style|topbar|inputbar|cli.?arg|startup.?param|workbench|rewind|panel|keybinding|search.?filter|interceptor.?icon|goal.?progress|output.?stream", "apps"),
    (r"generator|analyzer|aot|enum.?metadata|fsm|source.?gen|ctor.?scanner|simd.?vector|rg.?engine|wellknown.?param|line.?number|range.?reader|param.?metadata", "generators"),
    (r"onnx|ctor.?scanner|simd|scripts?", "tools"),
]

# 全局保留的文档（跨域或项目级）
GLOBAL_PATTERNS = [
    r"README\.md",
    r"DSG001-architecture-index",
    r"DSG002-technical-details",
    r"DSG004-flatten-restructure",
    r"DSG005-flatten-restructure",
    r"DSG013-UnifiedResilience",
    r"DSG023-worktree-merge",
    r"DSG040-colocation",
    r"condition-order-fix-tasks",
    r"refactor_data_structure_consolidation",
    r"defensive-programming",
    r"static-utility-logger",
    r"PLA003-using改造",
    r"PLA004-强迫症",
    r"PLA005-文件夹整理",
    r"PLA006-文件夹整理",
    r"PRF005-using-var",
    r"PRF008-架构优化",
    r"PRF009-架构选型",
    r"PRF010-架构选型",
    r"REF002-archive-dead",
    r"REF004-foreach-to-dict",
    r"REF008-field-merge",
    r"code_review_report",
    r"错误编码",
    r"dispose-timeout-violations",
    r"0134-layered-ci",
    r"TASK020-惯性错误",
    r"TASK023-覆盖全部功能",
    r"TASK025-代码审查",
    r"TASK026-文档遗漏",
    r"TASK028-严格语法",
    r"TASK030-交接文档-基建",
    r"TASK030-基建确定性",
    r"TASK031-冗余拷贝",
    r"TASK031-基建确定性",
    r"single_source_audit",
    r"pr356_ci_fix",
    r"00_summary",
    r"01_multi_source",
    r"02_hardcoded",
    r"03_duplicate",
    r"04_unified",
    r"05_enum_value",
    r"06_exceptions",
]

# DSG040 附录 B 的显式映射（覆盖关键词推断）
EXPLICIT_MAP = {
    "DSG003-small-model-strategy.md": "llm",
    "DSG006-ComputerUse-PRD.md": "apps",
    "DSG007-ComputerUse-P0-DesktopInput-Design.md": "apps",
    "DSG008-ComputerUse-P0-Acceptance.md": "apps",
    "DSG009-ComputerUse-P1-P5-Acceptance.md": "apps",
    "DSG010-desktop-scene-acceptance-criteria.md": "apps",
    "DSG011-desktop-ai-scene-disclosure-report.md": "apps",
    "DSG012-DesktopPulseOverlay-PRD.md": "apps",
    "DSG014-TransportFallbackChain.md": "async_primitives",
    "DSG015-ContextCompaction-Design.md": "cognition",
    "DSG016-LLM-OutputLoop-Detection-Design.md": "llm",
    "DSG017-GUI重构-subAgent运行期显示设计.md": "apps",
    "DSG018-工具渐进式暴露设计.md": "cognition",
    "DSG019-多模态隐喻显露工具-PRD.md": "perception",
    "DSG020-命令拦截架构改造.md": "guard",
    "DSG021-rg-engine-acceleration-and-unification.md": "tools",
    "DSG022-RangeDownloader-PRD.md": "storage",
    "DSG024-bridgemain-split-task.md": "services",
    "DSG025-fix-config-loading-bugs.md": "abstractions",
    "DSG026-window-shake-notification-design.md": "apps",
    "DSG027-cli_arg_io_unify_analysis.md": "apps",
    "DSG028-subagent-stall-defense.md": "llm",
    "DSG029-inmemory-index-store-immutable-cas-refactor.md": "persistence",
    "DSG030-vector-code-index-design.md": "perception",
    "DSG031-gui-topbar-inputbar-redesign.md": "apps",
    "DSG031-ownership-lifetime-leak-analyzer.md": "generators",
    "DSG032-vscode-style-gui-master-plan.md": "apps",
    "DSG033-actor-mailbox-leak-fix-and-supervision-tree-align-akka.md": "async_primitives",
    "DSG034-actor-refactor-and-memory-leak-fix.md": "async_primitives",
    "DSG035-dock-layout-fix-plan.md": "apps",
    "DSG036-vecidx5-paged-incremental-index.md": "perception",
    "DSG037-interaction-tool-name-cleanup.md": "cognition",
    "DSG038-deadlock-elimination-overhaul-master-plan.md": "async_primitives",
    "DSG039-startup-param-architecture-refactor.md": "apps",
    "analyzer-refactor-task.md": "generators",
    "gui_experience.md": "apps",
    "gui-improvement-1-subagent-output-stream.md": "apps",
    "gui-improvement-2-goal-progress.md": "apps",
    "gui-improvement-3-interceptor-icons.md": "apps",
    "gui-improvement-4-search-filter.md": "apps",
    "gui-improvement-5-keybindings.md": "apps",
    # unknown 文档的显式映射
    "2026-09-21-ci-flaky-tests-global-state-pollution.md": "async_primitives",
    "2026-10-01-ci-actor-dispose-inflight-fire-and-forget.md": "async_primitives",
    "async-thread-currentthread-leak-report.md": "async_primitives",
    "脚本超时与路径校验修复.md": "guard",
    "PLA001-Workflow断点续跑工作计划.md": "composition",
    "PLA002-gh-rest-api-rewrite-plan.md": "mcp",
    "PRF001-0-GC字符串效率审计报告.md": "collections",
    "PRF002-cache-break-detection-plan.md": "llm",
    "PRF003-prefix-cache-compression-plan.md": "llm",
    "PRF004-Spawn管道统一设计.md": "composition",
    "PRF006-会话隔离路由改造.md": "apps",
    "PRF007-提示词工具名绑定重构.md": "cognition",
    "PRF011-状态机重构任务清单.md": "generators",
    "PRF012-状态机重构计划.md": "generators",
    "PRF013-重构CLI.md": "apps",
    "REF003-file-watcher-unification.md": "plugins",
    "REF005-jcc-path-consolidation.md": "apps",
    "models.md": "llm",
    "TASK002-Worktree隔离缺陷修复任务.md": "guard",
    "TASK003-Actor模型推广改造任务清单.md": "async_primitives",
    "TASK014-会话存储统一与路径重构-规划.md": "storage",
    "TASK015-全局推广node插件.md": "plugins",
    "TASK016-前缀命令后续工作.md": "guard",
    "TASK017-启动参数完全重构任务清单.md": "apps",
    "TASK018-启动参数测试记录.md": "apps",
    "TASK019-子代理并发控制任务清单.md": "llm",
    "TASK021-文件工具统一安全防御改造.md": "guard",
    "TASK022-熵减器补齐任务.md": "guard",
    "TASK024-聊天室改造任务清单.md": "apps",
    "TASK027-管道缓冲区零导致写阻塞-bug修复记录.md": "async_primitives",
    "TASK028-全量异步化释放路径重构.md": "async_primitives",
    "TASK030-分析器增强与范围纠正.md": "generators",
    "TASK031-守卫补全与单数据源与测试分家.md": "guard",
    "TASK032-批次3-Immutable-CAS改造.md": "collections",
    "TASK034-统一检索接口-5模式枚举.md": "perception",
    "TASK035+面板统一可停靠计划.md": "apps",
    "TASK037-jcc启动开销优化.md": "apps",
    "gh_cli_param_alignment.md": "mcp",
    "gh_cli_param_alignment2.md": "mcp",
    "gh_pr_issue_edit_acceptance_report.md": "mcp",
    "h2_sync_block_audit.md": "async_primitives",
    "immutable_dict_cas_audit.md": "collections",
    "m1_public_mutable_collection_audit.md": "collections",
    "goal-unattended-linkage.md": "llm",
}


def classify_file(filename: str, subdir: str) -> str:
    """返回域名或 'global'（保留原位）或 'unknown'（需人工确认）"""
    # 1. 检查全局保留
    for pat in GLOBAL_PATTERNS:
        if re.search(pat, filename, re.IGNORECASE):
            return "global"

    # 2. 检查显式映射
    if filename in EXPLICIT_MAP:
        return EXPLICIT_MAP[filename]

    # 3. 关键词匹配
    for pat, domain in KEYWORD_MAP:
        if re.search(pat, filename, re.IGNORECASE):
            return domain

    # 4. 子目录线索
    if subdir:
        for pat, domain in KEYWORD_MAP:
            if re.search(pat, subdir, re.IGNORECASE):
                return domain

    return "unknown"


def scan_docs():
    """扫描 docs/ 下所有文档，分类并输出迁移计划"""
    plan = {"global": [], "unknown": []}
    for domain in DOMAINS:
        plan[domain] = []

    # 遍历 docs/ 下所有 .md 文件
    for root, dirs, files in os.walk(DOCS_DIR):
        # 跳过 adr/（全部保留）
        rel_root = os.path.relpath(root, DOCS_DIR)
        if rel_root == "adr" or rel_root.startswith("adr"):
            continue

        for f in files:
            if not f.endswith(".md"):
                continue

            filepath = os.path.join(root, f)
            rel_path = os.path.relpath(filepath, DOCS_DIR)

            # 跳过 zh-CN/（单独处理）
            if rel_path.startswith("zh-CN"):
                continue

            domain = classify_file(f, rel_root)

            plan[domain].append({
                "rel_path": rel_path.replace("\\", "/"),
                "filename": f,
                "subdir": rel_root,
            })

    return plan


def main():
    plan = scan_docs()

    # 输出统计
    print("=" * 60)
    print("文档→域分类结果")
    print("=" * 60)

    for domain in DOMAINS + ["global", "unknown"]:
        files = plan[domain]
        if files:
            print(f"\n## {domain} ({len(files)} 个)")
            for item in sorted(files, key=lambda x: x["rel_path"]):
                print(f"  {item['rel_path']}")

    # 输出 JSON
    output_path = REPO_ROOT / "modules" / "tools" / "scripts" / "doc_migration_plan.json"
    with open(output_path, "w", encoding="utf-8") as fp:
        json.dump(plan, fp, indent=2, ensure_ascii=False)
    print(f"\n迁移计划已写入: {output_path}")


if __name__ == "__main__":
    main()
