#!/usr/bin/env python3
"""风格门禁 — 全仓风格统一检查（TASK099 B7）。

规则:
  S1 pragma       生产代码/生成器源码禁止 #pragma warning disable；测试仅允许带理由的 JCC1018
  S2 file-using   禁止文件内 using（GlobalUsings.cs 集中声明）
  S3 null-bang    生产代码禁止 null-forgiving 运算符 "!"
  S4 async-void   生产代码禁止 async void（UI 事件处理器模式除外）
  S5 unbounded    生产代码禁止 Channel.CreateUnbounded（Actor 邮箱须 style-allow 标注）
  S6 sync-block   生产代码禁止同步阻塞异步（框架边界须 style-allow 标注）
  S7 namespace    禁止 namespace PithosDB.Core.Core
  S8 dir-case     源码目录名必须 lower_snake（项目根目录镜像 csproj 名豁免）

范围: git 跟踪的 .cs 文件；排除 non_deliverables_tools/（分析工具本身）、docs/、artifacts/。
豁免: 行内或前 3 行 `// style-allow: <rule> — <理由>`（注解理由必填）。

用法:
  python non_deliverables_tools/scripts/Verify-StyleGate.py            # 全量报告，零命中退出 0
  python non_deliverables_tools/scripts/Verify-StyleGate.py --rule S1  # 单规则
  python non_deliverables_tools/scripts/Verify-StyleGate.py --json .xxx/logs/b7/style.json
"""

import argparse
import json
import os
import re
import subprocess
import sys

BS = chr(92)
PROD_ROOTS = ('modules/', 'native_tui/', 'native_gui/')
SKIP_ROOTS = ('non_deliverables_tools/', 'docs/', 'artifacts/', '.xxx/')


def is_test_path(path):
    low = path.lower().replace(BS, '/')
    return '/.tests/' in low or '.tests/' in low or low.startswith('test/') or low.startswith('tests/')


def is_prod_path(path):
    low = path.lower().replace(BS, '/')
    return low.startswith(PROD_ROOTS) and not is_test_path(path)


def is_generator_rule_source(path):
    low = path.lower().replace(BS, '/')
    return '/rules/' in low or 'aot_safety.shared/ruledetectors/' in low


def tracked_cs_files():
    out = subprocess.run(['git', 'ls-files', '*.cs'], capture_output=True, text=True, check=True).stdout
    files = [line for line in out.splitlines() if line]
    return [f for f in files if not f.lower().replace(BS, '/').startswith(SKIP_ROOTS)]


def read_text(path):
    with open(path, encoding='utf-8-sig', errors='replace') as fh:
        return fh.read()


ALLOW_RE = re.compile(r'style-allow:\s*([a-z0-9-]+)\s*[—-]\s*(\S.*)')
USING_RE = re.compile(r'^\s*using\s+(static\s+)?[A-Za-z_][\w.]*\s*(=\s*[^;]+)?;', re.M)
PRAGMA_RE = re.compile(r'^\s*#pragma\s+warning\s+disable[^\n]*', re.M)
PRAGMA_JCC1018_RE = re.compile(r'^\s*#pragma\s+warning\s+disable\s+JCC1018\b')
NULL_BANG_RE = re.compile(r'(?<![\w\]\)])(?:[A-Za-z_][\w.]*|\]|\))!(?=[.\[(])')
ASYNC_VOID_DECL_RE = re.compile(r'\basync\s+void\s+([A-Za-z_]\w*)\s*\(([^)]*)\)')
UNBOUNDED_RE = re.compile(r'Channel\.CreateUnbounded')
SYNC_BLOCK_RES = (
    re.compile(r'GetAwaiter\(\)\.GetResult\(\)'),
    re.compile(r'\bTask\.Wait(All|Any)?\('),
    re.compile(r'\.Task\.Wait\('),
    re.compile(r'\.Wait\((?:TimeSpan|CancellationToken|TimeSpan\.|\d)'),
)
NS_RE = re.compile(r'namespace\s+PithosDB\.Core\.Core\b')
EVENT_HANDLER_RE = re.compile(r'EventArgs\b[\w?<>.]*\s+\w+\s*$')


def line_no(text, pos):
    return text.count('\n', 0, pos) + 1


def nearby_allowed(lines, index, rule):
    """当前行或前 3 行存在 `style-allow: <rule> — 理由` 注解则豁免。"""
    for i in range(max(0, index - 3), index + 1):
        match = ALLOW_RE.search(lines[i])
        if match and match.group(1) == rule:
            return True
    return False


def check_pragma(path, text, lines, violations):
    is_test = is_test_path(path)
    for match in PRAGMA_RE.finditer(text):
        idx = line_no(text, match.start()) - 1
        line = lines[idx]
        if nearby_allowed(lines, idx, 'pragma'):
            continue
        if is_test and PRAGMA_JCC1018_RE.match(line) and (
                '//' in line and re.search(r'Windows|路径|盘符|理由|JCC1018', line)):
            continue
        violations.append({'rule': 'S1', 'path': path, 'line': idx + 1, 'text': line.strip()})


def check_file_using(path, text, violations):
    name = os.path.basename(path)
    if name == 'GlobalUsings.cs':
        return
    for match in USING_RE.finditer(text):
        violations.append({'rule': 'S2', 'path': path, 'line': line_no(text, match.start()),
                           'text': match.group(0).strip()})


def check_null_bang(path, text, violations):
    for match in NULL_BANG_RE.finditer(text):
        violations.append({'rule': 'S3', 'path': path, 'line': line_no(text, match.start()),
                           'text': text[match.start():match.start() + 80].split('\n')[0].strip()})


def check_async_void(path, text, violations):
    for match in ASYNC_VOID_DECL_RE.finditer(text):
        if EVENT_HANDLER_RE.search(match.group(2).strip()):
            continue
        violations.append({'rule': 'S4', 'path': path, 'line': line_no(text, match.start()),
                           'text': match.group(0).strip()})


def check_unbounded(path, text, lines, violations):
    for match in UNBOUNDED_RE.finditer(text):
        idx = line_no(text, match.start()) - 1
        if nearby_allowed(lines, idx, 'unbounded'):
            continue
        violations.append({'rule': 'S5', 'path': path, 'line': idx + 1, 'text': lines[idx].strip()})


def check_sync_block(path, text, lines, violations):
    for regex in SYNC_BLOCK_RES:
        for match in regex.finditer(text):
            idx = line_no(text, match.start()) - 1
            if nearby_allowed(lines, idx, 'sync-block'):
                continue
            violations.append({'rule': 'S6', 'path': path, 'line': idx + 1, 'text': lines[idx].strip()})


def check_namespace(path, text, violations):
    for match in NS_RE.finditer(text):
        violations.append({'rule': 'S7', 'path': path, 'line': line_no(text, match.start()),
                           'text': match.group(0)})


def check_dirs(files, violations):
    seen = set()
    for path in files:
        low = path.lower().replace(BS, '/')
        if not low.startswith(PROD_ROOTS) or is_test_path(path):
            continue
        parts = low.split('/')[:-1]
        for depth in range(2, len(parts) + 1):
            rel = '/'.join(parts[:depth])
            if rel in seen:
                continue
            seen.add(rel)
            name = parts[depth - 1]
            if re.fullmatch(r'[a-z0-9_]+', name):
                continue
            directory = '/'.join(parts[:depth])
            abs_dir = directory.replace('/', BS)
            try:
                entries = os.listdir(abs_dir)
            except OSError:
                continue
            if any(entry.endswith('.csproj') for entry in entries):
                continue
            violations.append({'rule': 'S8', 'path': directory, 'line': 0,
                               'text': '目录名必须 lower_snake: ' + name})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--rule', action='append', help='只检查指定规则（可多次）')
    parser.add_argument('--json', help='输出 JSON 报告路径')
    args = parser.parse_args()
    wanted = {rule.upper() for rule in args.rule} if args.rule else None

    files = tracked_cs_files()
    violations = []
    for path in files:
        text = read_text(path)
        lines = text.split('\n')
        test = is_test_path(path)
        gen_rule = is_generator_rule_source(path)
        prod = is_prod_path(path)
        if (wanted is None or 'S1' in wanted) and not test:
            check_pragma(path, text, lines, violations)
        if (wanted is None or 'S2' in wanted) and (prod or (path.startswith('modules/generators/') and not test)):
            check_file_using(path, text, violations)
        if (wanted is None or 'S3' in wanted) and (prod or (path.startswith('modules/generators/') and not test and not gen_rule)):
            check_null_bang(path, text, violations)
        if (wanted is None or 'S4' in wanted) and prod and not gen_rule:
            check_async_void(path, text, violations)
        if (wanted is None or 'S5' in wanted) and prod:
            check_unbounded(path, text, lines, violations)
        if (wanted is None or 'S6' in wanted) and prod and not gen_rule:
            check_sync_block(path, text, lines, violations)
        if (wanted is None or 'S7' in wanted) and path.startswith('modules/'):
            check_namespace(path, text, violations)
    if wanted is None or 'S8' in wanted:
        check_dirs(files, violations)

    violations.sort(key=lambda v: (v['rule'], v['path'], v['line']))
    for violation in violations:
        location = f"{violation['path']}:{violation['line']}" if violation['line'] else violation['path']
        print(f"{violation['rule']} {location}: {violation['text']}")

    counts = {}
    for violation in violations:
        counts[violation['rule']] = counts.get(violation['rule'], 0) + 1
    print('\n== 汇总 ==')
    for rule in sorted(counts):
        print(f"{rule}: {counts[rule]}")
    print(f"总计: {len(violations)}")

    if args.json:
        os.makedirs(os.path.dirname(args.json), exist_ok=True)
        with open(args.json, 'w', encoding='utf-8') as fh:
            json.dump({'counts': counts, 'violations': violations}, fh, ensure_ascii=False, indent=2)

    return 1 if violations else 0


if __name__ == '__main__':
    sys.exit(main())
