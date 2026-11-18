#!/usr/bin/env python3
"""将项目内文件头 using 收敛到 GlobalUsings.cs（TASK099 B7 S2）。

只处理文件头 using 区（注释/空行/# 指令之间的列 0 using），不触碰字符串字面量或 namespace 内部。

用法:
  python non_deliverables_tools/scripts/Migrate-FileUsingsToGlobal.py <项目目录> [--apply]
  省略 --apply 时只打印计划。
"""

import os
import re
import sys

USING_RE = re.compile(r'^(using\s+(static\s+)?([A-Za-z_][\w.]*)(\s*=\s*[^;]+)?;[ \t]*\r?)$', re.M)
USING_START_RE = re.compile(r'^using\s+')
ALIAS_RE = re.compile(r'^using\s+([A-Za-z_]\w*)\s*=\s*(.+);$')


def header_end(text):
    pos = 0
    length = len(text)
    in_block_comment = False
    while pos < length:
        end = text.find('\n', pos)
        if end < 0:
            end = length
        line = text[pos:end].strip()
        if in_block_comment:
            if '*/' in line:
                in_block_comment = False
            pos = end + 1
            continue
        if not line or line.startswith('//') or line.startswith('#') or USING_START_RE.match(line):
            pos = end + 1
            continue
        if line.startswith('/*'):
            in_block_comment = '*/' not in line
            pos = end + 1
            continue
        return pos
    return length


def collect(project_dir):
    files = []
    for dp, dns, fns in os.walk(project_dir):
        dns[:] = [d for d in dns if d not in {'obj', 'bin'}]
        for fn in fns:
            if fn.endswith('.cs') and fn != 'GlobalUsings.cs' and not fn.endswith('.g.cs'):
                files.append(os.path.join(dp, fn))
    return files


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    project_dir = sys.argv[1]
    apply = '--apply' in sys.argv
    directives = {}
    aliases = {}
    for path in collect(project_dir):
        text = open(path, encoding='utf-8-sig').read()
        header = text[:header_end(text)]
        for match in USING_RE.finditer(header):
            directive = 'using ' + (match.group(2) or '') + match.group(3) + (match.group(4) or '') + ';'
            directives.setdefault(directive, []).append(path)
            alias_match = ALIAS_RE.match(directive)
            if alias_match:
                aliases.setdefault(alias_match.group(1), set()).add(alias_match.group(2).strip())
    conflicts = {alias: targets for alias, targets in aliases.items() if len(targets) > 1}
    if conflicts:
        print('别名冲突，需人工处理:', conflicts)
        return 3
    global_path = os.path.join(project_dir, 'GlobalUsings.cs')
    existing = open(global_path, encoding='utf-8-sig').read() if os.path.exists(global_path) else ''
    additions = [d for d in sorted(directives) if ('global ' + d) not in existing and d not in existing]
    print('文件头 using 种类:', len(directives), '需要新增:', len(additions))
    for directive in additions:
        print('  +', directive, '  <-', len(directives[directive]), 'files')
    if not apply:
        return 0
    for path in collect(project_dir):
        text = open(path, encoding='utf-8-sig').read()
        end = header_end(text)
        stripped = USING_RE.sub('', text[:end]) + text[end:]
        if stripped != text:
            open(path, 'w', encoding='utf-8', newline='').write(stripped)
    if additions:
        prefix = '' if not existing or existing.endswith('\n') else '\n'
        block = prefix + ''.join('global ' + d + '\n' for d in additions)
        with open(global_path, 'a', encoding='utf-8', newline='') as fh:
            fh.write(block)
    print('已应用')
    return 0


if __name__ == '__main__':
    sys.exit(main())
