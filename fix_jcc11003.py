import re
import sys
from pathlib import Path
from collections import defaultdict

ERROR_FILE = sys.argv[1] if len(sys.argv) > 1 else "/tmp/jcc_errors.txt"
DRY_RUN = "--apply" not in sys.argv

errors_by_file = defaultdict(set)
pattern = re.compile(r'^(.+?)\((\d+),(\d+)\): error JCC11003')
with open(ERROR_FILE, encoding='utf-8') as f:
    for line in f:
        m = pattern.match(line)
        if m:
            filepath = m.group(1).replace('\\', '/')
            lineno = int(m.group(2))
            errors_by_file[filepath].add(lineno)

# 只用访问修饰符和成员修饰符,不用类型名
MODIFIERS = {
    'public', 'private', 'internal', 'protected', 'file',
    'static', 'async', 'override', 'virtual', 'sealed',
    'abstract', 'extern', 'unsafe', 'readonly',
    'volatile', 'event', 'delegate', 'partial', 'new',
}

# 语句前缀(方法体内),跳过
STMT_PREFIXES = (
    'var ', 'if ', 'if(', 'for ', 'for(', 'foreach ', 'foreach(',
    'while ', 'while(', 'return', 'await ', 'using ', 'using(',
    'throw', 'try ', 'try{', 'catch', 'finally', 'switch ',
    'lock ', 'lock(', 'fixed ', 'fixed(', 'unchecked', 'checked',
    'do ', 'do{', 'else', 'new ', 'this.', 'base.',
    '=>', '}', '{', '//', '/*', '*', '///', 'else',
    'continue', 'break', 'goto', 'yield',
)

def is_modifier_line(stripped):
    """判断是否是成员声明第一行(以修饰符开头)"""
    if not stripped:
        return False
    words = stripped.split()
    if not words:
        return False
    return words[0] in MODIFIERS

def find_member_start(lines, error_lineno):
    """从错误行向上找成员声明的修饰符行(缩进<=4排除方法体内)"""
    idx = error_lineno - 1
    while idx >= 0:
        stripped = lines[idx].lstrip()
        if not stripped or stripped.startswith(STMT_PREFIXES):
            idx -= 1
            continue
        if is_modifier_line(stripped):
            indent = len(lines[idx]) - len(lines[idx].lstrip())
            if indent > 4:
                idx -= 1
                continue
            prev = idx - 1
            while prev >= 0 and not lines[prev].strip():
                prev -= 1
            if prev >= 0:
                ps = lines[prev].strip()
                if ps.endswith('(') or ps.endswith(','):
                    idx -= 1
                    continue
            return idx
        idx -= 1
    return None

def find_insert_point(lines, member_start_idx):
    """插入点: XML注释之后、特性块开头。顺序: ///注释 → [特性] → 声明"""
    idx = member_start_idx - 1
    while idx >= 0:
        stripped = lines[idx].strip()
        if stripped.startswith('['):
            idx -= 1
            continue
        break
    return idx + 1

SUPPRESS = '[SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]'

total_inserts = 0
warns = []
for filepath, error_lines in sorted(errors_by_file.items()):
    p = Path(filepath)
    if not p.exists():
        continue
    text = p.read_text(encoding='utf-8-sig')
    lines = text.split('\n')
    # 检测缩进: 用成员声明行的缩进
    insert_points = {}
    for el in sorted(error_lines):
        ms = find_member_start(lines, el)
        if ms is None:
            warns.append(f"  WARN: {filepath}:{el} cannot find member")
            continue
        ip = find_insert_point(lines, ms)
        if ip not in insert_points:
            insert_points[ip] = lines[ms].rstrip()[:len(lines[ms]) - len(lines[ms].lstrip())]
    if not insert_points:
        continue
    for ip in sorted(insert_points.keys(), reverse=True):
        indent = insert_points[ip] or '    '
        lines.insert(ip, indent + SUPPRESS)
        total_inserts += 1
    if not DRY_RUN:
        p.write_text('\n'.join(lines), encoding='utf-8')
    print(f"{'[DRY] ' if DRY_RUN else ''}{filepath}: {len(insert_points)} inserts")

for w in warns:
    print(w)
print(f"\nTotal: {total_inserts} inserts across {len(errors_by_file)} files ({'DRY RUN' if DRY_RUN else 'APPLIED'})")
