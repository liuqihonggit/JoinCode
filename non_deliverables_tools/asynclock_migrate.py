#!/usr/bin/env python3
"""SemaphoreSlim(1,1) -> AsyncLock 批量迁移脚本

安全策略:
1. 先收集所有 SemaphoreSlim(1,1) 字段名
2. 只替换这些字段的 WaitAsync/Release(避免误匹配 Task.WaitAsync)
3. 只自动处理"简单"模式(无 catch,finally 只有 Release)
4. 复杂模式(finally 有其他内容/有 catch)报告但不自动处理

用法:
  python tools/asynclock_migrate.py --dry-run
  python tools/asynclock_migrate.py
  python tools/asynclock_migrate.py --filter infrastructure
"""

import re
import argparse
from pathlib import Path

EXCLUDE_PARTS = {'bin', 'obj', 'artifacts', 'tests', '.xxx', '.git'}
EXCLUDE_DIRS = [r'\AsyncLock\src']

def should_exclude(path: Path) -> bool:
    s = str(path)
    for part in path.parts:
        if part in EXCLUDE_PARTS:
            return True
    for d in EXCLUDE_DIRS:
        if d in s:
            return True
    return False

def find_brace_match(text: str, open_pos: int) -> int:
    depth = 0
    i = open_pos
    while i < len(text):
        c = text[i]
        if c == '{': depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0: return i
        i += 1
    return -1

def collect_semaphore_fields(content: str) -> set[str]:
    """收集所有 SemaphoreSlim(1,1) 字段名"""
    names = set()
    for m in re.finditer(r'private readonly SemaphoreSlim (\w+)\s*[=;]', content):
        names.add(m.group(1))
    return names

def replace_fields(content: str) -> tuple[str, list[str]]:
    changes = []
    pat1 = r'private readonly SemaphoreSlim (\w+) = new\(1,\s*1\);'
    content = re.sub(pat1, lambda m: (changes.append(f"字段内联: {m.group(1)}") or f'private readonly AsyncLock {m.group(1)} = new();'), content)
    pat2 = r'private readonly SemaphoreSlim (\w+) = new SemaphoreSlim\(1,\s*1\);'
    content = re.sub(pat2, lambda m: (changes.append(f"字段内联完整: {m.group(1)}") or f'private readonly AsyncLock {m.group(1)} = new();'), content)
    init_pat = r'(\n\s*)(\w+) = new SemaphoreSlim\(1,\s*1\);'
    to_process = []
    for m in re.finditer(init_pat, content):
        var_name = m.group(2)
        field_pat = r'private readonly SemaphoreSlim ' + re.escape(var_name) + r';'
        if re.search(field_pat, content):
            to_process.append(var_name)
    for var_name in to_process:
        init_line_pat = r'\n\s*' + re.escape(var_name) + r' = new SemaphoreSlim\(1,\s*1\);'
        content = re.sub(init_line_pat, '\n', content, count=1)
        field_pat = r'private readonly SemaphoreSlim ' + re.escape(var_name) + r';'
        content = re.sub(field_pat, f'private readonly AsyncLock {var_name} = new();', content)
        changes.append(f"字段分开+构造函数: {var_name}")
    return content, changes

def replace_waitasync(content: str, field_names: set[str]) -> tuple[str, list[str]]:
    changes = []
    if not field_names:
        return content, changes
    name_alt = '|'.join(re.escape(n) for n in field_names)
    wait_pat = rf'await ({name_alt})\.WaitAsync\(([^)]*)\)\.ConfigureAwait\(false\);'
    matches = list(re.finditer(wait_pat, content))
    for m in reversed(matches):
        var_name = m.group(1)
        args = m.group(2)
        start, end = m.start(), m.end()
        new_wait = f'using var guard = await {var_name}.LockAsync({args}).ConfigureAwait(false);'
        after = content[end:]
        try_match = re.match(r'\s*try\s*\{', after)
        if not try_match:
            content = content[:start] + new_wait + content[end:]
            changes.append(f"WaitAsync->LockAsync (无try): {var_name}")
            continue
        brace_open = end + try_match.end() - 1
        brace_close = find_brace_match(content, brace_open)
        if brace_close == -1:
            content = content[:start] + new_wait + content[end:]
            changes.append(f"WaitAsync->LockAsync (大括号不匹配): {var_name}")
            continue
        after_try = content[brace_close+1:]
        catch_match = re.match(r'\s*catch', after_try)
        fin_m = re.search(r'finally\s*\{', content[brace_close+1:])
        has_catch = bool(catch_match)
        if not fin_m:
            content = content[:start] + new_wait + content[end:]
            changes.append(f"WaitAsync->LockAsync (无finally): {var_name}")
            continue
        fin_start = brace_close + 1 + fin_m.start()
        fin_brace_open = content.index('{', fin_start)
        fin_brace_close = find_brace_match(content, fin_brace_open)
        if fin_brace_close == -1:
            content = content[:start] + new_wait + content[end:]
            changes.append(f"WaitAsync->LockAsync (finally不匹配): {var_name}")
            continue
        fin_content = content[fin_brace_open+1:fin_brace_close].strip()
        release_pat = rf'{re.escape(var_name)}\.Release\(\)\s*;?'
        if not re.fullmatch(release_pat, fin_content):
            changes.append(f"!! 需手动处理 (finally有其他内容): {var_name} @ line {content[:start].count(chr(10))+1}")
            continue
        if has_catch:
            line_start = content.rfind('\n', 0, fin_start) + 1
            content = content[:start] + new_wait + content[end:line_start] + content[fin_brace_close+1:]
            changes.append(f"WaitAsync->LockAsync + 删finally(保留catch): {var_name}")
        else:
            try_body = content[brace_open+1:brace_close]
            lines = try_body.split('\n')
            dedented = []
            for line in lines:
                if line.startswith('    '): dedented.append(line[4:])
                elif line.strip() == '': dedented.append('')
                else: dedented.append(line)
            content = content[:start] + new_wait + '\n' + '\n'.join(dedented) + content[fin_brace_close+1:]
            changes.append(f"WaitAsync->LockAsync + 删try-finally: {var_name}")
    return content, changes

def process_file(path: Path, dry_run: bool) -> list[str]:
    try:
        content = path.read_text(encoding='utf-8-sig')
    except Exception as e:
        return [f"ERROR: {path}: {e}"]
    original = content
    field_names = collect_semaphore_fields(content)
    content, changes1 = replace_fields(content)
    content, changes2 = replace_waitasync(content, field_names)
    all_changes = changes1 + changes2
    if content != original:
        if not dry_run:
            path.write_text(content, encoding='utf-8')
        return [f"{'[DRY] ' if dry_run else ''}{path}"] + [f"  - {c}" for c in all_changes]
    return []

def main():
    parser = argparse.ArgumentParser(description='SemaphoreSlim(1,1) -> AsyncLock')
    parser.add_argument('--dry-run', action='store_true')
    parser.add_argument('--filter', default='')
    parser.add_argument('root', nargs='?', default='.')
    args = parser.parse_args()
    root = Path(args.root)
    total_files = 0
    total_changes = 0
    manual = 0
    for cs_file in root.rglob('*.cs'):
        if should_exclude(cs_file): continue
        if args.filter and args.filter not in str(cs_file): continue
        results = process_file(cs_file, args.dry_run)
        if results:
            total_files += 1
            for line in results:
                if '!!' in line: manual += 1
                print(line)
            total_changes += len(results) - 1
    print(f"\n{'[DRY RUN] ' if args.dry_run else ''}修改 {total_files} 文件, {total_changes} 替换, {manual} 需手动")

if __name__ == '__main__':
    main()
