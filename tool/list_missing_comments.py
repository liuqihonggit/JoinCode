"""输出剩余缺漏的详细列表，便于主代理逐个补全。"""
from __future__ import annotations
import re
from pathlib import Path

ROOT = Path("D:/project/w2/llm/agents")
DECL_PATTERN = re.compile(r'^\s*(?P<access>public|internal)\s+')

def has_xml_comment_before(lines, idx):
    j = idx - 1
    while j >= 0:
        s = lines[j].strip()
        if not s:
            j -= 1
            continue
        if s.startswith("///"):
            return True
        if s.startswith("["):
            j -= 1
            continue
        return False
    return False

def is_skippable(line):
    s = line.strip()
    if not s: return True
    if s.startswith("using ") or s.startswith("namespace ") or s.startswith("[assembly:"): return True
    if "override " in s: return True
    return False

for f in sorted(ROOT.rglob("*.cs")):
    if "obj" in f.parts or "bin" in f.parts: continue
    text = f.read_text(encoding="utf-8-sig", errors="replace")
    lines = text.splitlines()
    in_enum = False
    depth = 0
    for i, line in enumerate(lines):
        if re.match(r'\s*(?:public|internal)\s+(?:partial\s+|static\s+)*enum\s+', line):
            in_enum = True; depth = 0
        if in_enum:
            if "{" in line: depth += line.count("{")
            if "}" in line:
                depth -= line.count("}")
                if depth <= 0: in_enum = False; depth = 0
            continue
        if is_skippable(line): continue
        if not DECL_PATTERN.match(line): continue
        if re.match(r'\s*(public|internal)\s+(catch|finally|else|return|throw|break|continue)\b', line): continue
        if not has_xml_comment_before(lines, i):
            rel = f.relative_to(ROOT)
            print(f"{rel}:{i+1}: {line.rstrip()[:120]}")
