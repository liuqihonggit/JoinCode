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
    seen = set()
    resolved = []
    for f in files:
        if f.lower().replace(BS, '/').startswith(SKIP_ROOTS) or not os.path.exists(f):
            continue
        actual = resolve_case(f)
        if actual in seen:
            continue
        seen.add(actual)
        resolved.append(actual)
    return resolved


def resolve_case(path):
    """git 索引大小写可能滞后于磁盘（目录改名后），按磁盘真实大小写还原报告路径。"""
    parts = path.replace(BS, '/').split('/')
    out = []
    for part in parts:
        parent = '/'.join(out) or '.'
        try:
            actual = next((entry for entry in os.listdir(parent) if entry.lower() == part.lower()), part)
        except OSError:
            actual = part
        out.append(actual)
    return '/'.join(out)


def skip_nested_string(text, start):
    """跳过内插 hole 内的嵌套普通字符串，返回闭合引号之后的位置。"""
    n = len(text)
    k = start
    while k < n:
        if text[k] == '\\':
            k += 2
            continue
        if text[k] == '"' or text[k] == '\n':
            return k + 1 if text[k] == '"' else k
        k += 1
    return k


def skip_nested_char(text, start):
    """跳过内插 hole 内的字符字面量，返回闭合单引号之后的位置。"""
    n = len(text)
    k = start
    while k < n:
        if text[k] == '\\':
            k += 2
            continue
        if text[k] == "'" or text[k] == '\n':
            return k + 1 if text[k] == "'" else k
        k += 1
    return k


def blank_code_noise(text):
    """把注释与字符串字面量内容替换为空格（保留换行与字符位置），避免正则误报。"""
    chars = list(text)
    n = len(text)

    def blank(start, end):
        for k in range(start, min(end, n)):
            if chars[k] not in '\r\n':
                chars[k] = ' '

    i = 0
    while i < n:
        ch = text[i]
        if ch == '/' and i + 1 < n and text[i + 1] == '/':
            end = text.find('\n', i)
            end = n if end < 0 else end
            blank(i, end)
            i = end
            continue
        if ch == '/' and i + 1 < n and text[i + 1] == '*':
            end = text.find('*/', i + 2)
            end = n if end < 0 else end + 2
            blank(i, end)
            i = end
            continue
        if ch == "'":
            end = i + 1
            while end < n:
                if text[end] == '\\':
                    end += 2
                    continue
                if text[end] == "'" or text[end] == '\n':
                    end += 1
                    break
                end += 1
            blank(i, end)
            i = end
            continue
        if ch in '@$':
            j = i
            verbatim = False
            interpolated = False
            while j < n and text[j] in '@$':
                verbatim = verbatim or text[j] == '@'
                interpolated = interpolated or text[j] == '$'
                j += 1
            if j < n and text[j] == '"':
                quote_start = j
                while j < n and text[j] == '"':
                    j += 1
                quote_count = j - quote_start
                if quote_count >= 3:
                    close = text.find('"' * quote_count, j)
                    end = n if close < 0 else close + quote_count
                else:
                    end = j
                    depth = 0
                    while end < n:
                        c = text[end]
                        if c == '\\' and not verbatim:
                            end += 2
                            continue
                        if interpolated and c == '{':
                            if depth == 0 and end + 1 < n and text[end + 1] == '{':
                                end += 2
                                continue
                            depth += 1
                            end += 1
                            continue
                        if interpolated and c == '}':
                            if depth == 0 and end + 1 < n and text[end + 1] == '}':
                                end += 2
                                continue
                            if depth > 0:
                                depth -= 1
                            end += 1
                            continue
                        if c == '"':
                            if verbatim and end + 1 < n and text[end + 1] == '"':
                                end += 2
                                continue
                            if depth > 0:
                                end = skip_nested_string(text, end + 1)
                                continue
                            end += 1
                            break
                        if c == "'" and depth > 0:
                            end = skip_nested_char(text, end + 1)
                            continue
                        end += 1
                blank(i, end)
                i = end
                continue
            i = j
            continue
        if ch == '"':
            j = i
            while j < n and text[j] == '"':
                j += 1
            quote_count = j - i
            if quote_count >= 3:
                close = text.find('"' * quote_count, j)
                end = n if close < 0 else close + quote_count
            elif quote_count == 2:
                end = j
            else:
                end = j
                while end < n:
                    if text[end] == '\\':
                        end += 2
                        continue
                    if text[end] == '"' or text[end] == '\n':
                        end += 1
                        break
                    end += 1
            blank(i, end)
            i = end
            continue
        i += 1
    return ''.join(chars)


def read_text(path):
    with open(path, encoding='utf-8-sig', errors='replace') as fh:
        return fh.read()


ALLOW_RE = re.compile(r'style-allow:\s*([a-z0-9-]+)\s*[—-]\s*(\S.*)')
USING_RE = re.compile(r'^\s*using\s+(static\s+)?[A-Za-z_][\w.]*\s*(=\s*[^;]+)?;', re.M)
PRAGMA_RE = re.compile(r'^\s*#pragma\s+warning\s+disable[^\n]*', re.M)
PRAGMA_JCC1018_RE = re.compile(r'^\s*#pragma\s+warning\s+disable\s+JCC1018\b')
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


def find_null_bangs(code):
    """返回 null-forgiving 后缀 `!` 的位置（代码已掩码注释/字符串；排除 `!=` 与前缀取反）。"""
    positions = []
    n = len(code)
    for i, ch in enumerate(code):
        if ch != '!' or i == 0:
            continue
        if i + 1 < n and code[i + 1] == '=':
            continue
        prev = code[i - 1]
        if prev.isalnum() or prev in '_)]':
            positions.append(i)
        elif prev == '>' and i + 1 < n and code[i + 1] in '.[(;,':
            positions.append(i)
    return positions


def check_null_bang(path, code, lines, violations):
    for pos in find_null_bangs(code):
        idx = line_no(code, pos) - 1
        if nearby_allowed(lines, idx, 'null-forgiving'):
            continue
        violations.append({'rule': 'S3', 'path': path, 'line': idx + 1, 'text': lines[idx].strip()})


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
        normalized = path.replace(BS, '/')
        low = normalized.lower()
        if not low.startswith(PROD_ROOTS) or is_test_path(path):
            continue
        parts = normalized.split('/')[:-1]
        for depth in range(2, len(parts) + 1):
            rel = '/'.join(parts[:depth])
            if rel in seen:
                continue
            seen.add(rel)
            name = parts[depth - 1]
            parent = '/'.join(parts[:depth - 1])
            actual = None
            try:
                for entry in os.listdir(parent or '.'):
                    if entry.lower() == name.lower():
                        actual = entry
                        break
            except OSError:
                continue
            if actual is None or re.fullmatch(r'[a-z0-9_]+', actual):
                continue
            directory = '/'.join(parts[:depth - 1] + [actual])
            try:
                entries = os.listdir(directory)
            except OSError:
                continue
            if any(entry.endswith('.csproj') for entry in entries):
                continue
            violations.append({'rule': 'S8', 'path': directory, 'line': 0,
                               'text': '目录名必须 lower_snake: ' + actual})


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
        code = blank_code_noise(text)
        lines = text.split('\n')
        test = is_test_path(path)
        gen_rule = is_generator_rule_source(path)
        prod = is_prod_path(path)
        if (wanted is None or 'S1' in wanted) and not test:
            check_pragma(path, code, lines, violations)
        if (wanted is None or 'S2' in wanted) and (prod or (path.startswith('modules/generators/') and not test)):
            check_file_using(path, code, violations)
        if (wanted is None or 'S3' in wanted) and (prod or (path.startswith('modules/generators/') and not test and not gen_rule)):
            check_null_bang(path, code, lines, violations)
        if (wanted is None or 'S4' in wanted) and prod and not gen_rule:
            check_async_void(path, code, violations)
        if (wanted is None or 'S5' in wanted) and prod:
            check_unbounded(path, code, lines, violations)
        if (wanted is None or 'S6' in wanted) and prod and not gen_rule:
            check_sync_block(path, code, lines, violations)
        if (wanted is None or 'S7' in wanted) and path.startswith('modules/'):
            check_namespace(path, code, violations)
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
