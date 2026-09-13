"""分析 llm/agents 工程中缺少 XML 文档注释的 public/internal 成员。

统计每个文件中需要补全注释的位置（类、接口、记录、结构、枚举、方法、属性、事件、字段、委托）。
按目录分组输出，便于派发给子代理。
"""
from __future__ import annotations

import re
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path("D:/project/w2/llm/agents")

# 匹配公开成员声明的关键字
# 排除：override（继承的成员不强制）、explicit/implicit 接口实现、auto-property 中的 get/set
PUBLIC_PATTERN = re.compile(
    r'^\s*(?:public|internal)\s+'
    r'(?:static\s+|sealed\s+|abstract\s+|virtual\s+|async\s+|readonly\s+|partial\s+|new\s+|unsafe\s+|extern\s+|volatile\s+|const\s+)*'
    r'(?:class|interface|record|struct|enum|delegate|event|void|bool|byte|sbyte|short|ushort|int|uint|long|ulong|float|double|decimal|char|string|object|Task|ValueTask|Span|ReadOnlySpan|Memory|ReadOnlyMemory|IReadOnlyList|IReadOnlyCollection|IReadOnlyDictionary|IList|ICollection|IDictionary|IEnumerable|IEnumerator|IAsyncEnumerable|Func|Action|Dictionary|List|HashSet|Queue|Stack|SortedSet|SortedDictionary|ConcurrentDictionary|ConcurrentQueue|ConcurrentStack|ImmutableArray|ImmutableList|ImmutableDictionary|FrozenSet|FrozenDictionary|TimeSpan|DateTime|DateTimeOffset|Guid|Uri|Stream|Type|object|int|string|bool|byte|char|double|decimal|float|long|short|sbyte|uint|ulong|ushort)'
)

# 更简单：匹配 public/internal 修饰符开头，且不是字段赋值/using/namespace
DECL_PATTERN = re.compile(
    r'^\s*(?P<access>public|internal)\s+'
)

# 排除这些上下文（不需要注释的）
SKIP_PREFIXES = (
    "public const ", "public static readonly ", "internal const ",
    "public override ", "internal override ",
    "public const\t", "internal const\t",
)

# 成员类型关键字（用于识别需要注释的声明）
MEMBER_KEYWORDS = (
    "class", "interface", "record", "struct", "enum", "delegate",
    "event", "void",
)

# 类型/方法/属性返回类型识别：检测这些关键字或已知类型模式
TYPE_PATTERN = re.compile(
    r'(class|interface|record|struct|enum|delegate|event)\b'
)

# 方法/属性识别：标识符 + ( 或 { 或 => 
MEMBER_PATTERN = re.compile(
    r'\b([A-Za-z_][A-Za-z0-9_]*)\s*[(<{]'
)


def has_xml_comment_before(lines: list[str], idx: int) -> bool:
    """检查 idx 行之前是否有 /// XML 注释。"""
    j = idx - 1
    while j >= 0:
        stripped = lines[j].strip()
        if not stripped:
            j -= 1
            continue
        if stripped.startswith("///"):
            return True
        if stripped.startswith("[") or stripped.startswith("[assembly:"):
            # 特性 attribute，继续往上找
            j -= 1
            continue
        return False
    return False


def is_skippable(line: str) -> bool:
    """判断该行是否可跳过（不需要注释）。"""
    stripped = line.strip()
    if not stripped:
        return True
    # 排除：using、namespace、assembly attribute、override、const 字段、auto get/set
    if stripped.startswith("using "):
        return True
    if stripped.startswith("namespace "):
        return True
    if stripped.startswith("[assembly:"):
        return True
    if "override " in stripped:
        return True
    # const/static readonly 字段通常不需要注释（但用户要求全部，所以保留）
    # 排除：枚举值（枚举值不需要每个都注释，枚举本身需要）
    # 排除：自动属性的 get;set; 单行
    if stripped.endswith("{ get; }") or stripped.endswith("{ get; set; }") or stripped.endswith("{ set; }"):
        # 单行自动属性，仍需注释
        return False
    return False


def analyze_file(file_path: Path) -> list[tuple[int, str]]:
    """分析单个文件，返回缺少注释的 (行号, 行内容) 列表。"""
    try:
        text = file_path.read_text(encoding="utf-8-sig", errors="replace")
    except Exception:
        return []
    lines = text.splitlines()
    missing: list[tuple[int, str]] = []
    in_enum = False
    enum_brace_depth = 0
    for i, line in enumerate(lines):
        # 跟踪 enum 上下文（enum 值不需要注释）
        if re.match(r'\s*(?:public|internal)\s+(?:partial\s+|static\s+)*enum\s+', line):
            in_enum = True
            enum_brace_depth = 0
        if in_enum:
            if "{" in line:
                enum_brace_depth += line.count("{")
            if "}" in line:
                enum_brace_depth -= line.count("}")
                if enum_brace_depth <= 0:
                    in_enum = False
                    enum_brace_depth = 0
            continue  # enum 内部全部跳过（包括 enum 本身，因为 enum 声明行已处理）

        if is_skippable(line):
            continue

        m = DECL_PATTERN.match(line)
        if not m:
            continue

        # 检查是否是真正的类型/成员声明（不是表达式）
        # 排除：public MyClass x = new();  这种字段赋值如果是简单类型也要注释
        # 但 public void Method() 是方法
        # 简化：只要行里有成员关键字或标识符后跟 ( { < => 就算

        # 排除：lambda、local function、anonymous type
        if "=>" in line and "delegate" not in line:
            # 可能是 expression-bodied 成员，仍需注释
            pass

        # 排除：catch、finally 等不是声明
        if re.match(r'\s*(public|internal)\s+(catch|finally|else|return|throw|break|continue)\b', line):
            continue

        # 检查前一行是否有 /// 注释
        if not has_xml_comment_before(lines, i):
            missing.append((i + 1, line.rstrip()))
    return missing


def main() -> None:
    by_dir: dict[Path, list[tuple[Path, list[tuple[int, str]]]]] = defaultdict(list)
    total = 0
    files_with_missing = 0

    cs_files = sorted(ROOT.rglob("*.cs"))
    for f in cs_files:
        if "obj" in f.parts or "bin" in f.parts:
            continue
        missing = analyze_file(f)
        if missing:
            files_with_missing += 1
            total += len(missing)
            rel = f.relative_to(ROOT)
            # 按第一级目录分组
            top_dir = rel.parts[0] if len(rel.parts) > 1 else Path("")
            by_dir[Path(top_dir)].append((f, missing))

    print(f"=== 总计 ===")
    print(f"文件数: {len(cs_files)}")
    print(f"有缺漏的文件数: {files_with_missing}")
    print(f"缺漏位置总数: {total}")
    print()

    print(f"=== 按一级目录分组 ===")
    for d in sorted(by_dir.keys()):
        items = by_dir[d]
        count = sum(len(m) for _, m in items)
        print(f"  {d}: {len(items)} 文件, {count} 处缺漏")

    print()
    print(f"=== 详细（每目录前 3 文件）===")
    for d in sorted(by_dir.keys()):
        items = by_dir[d]
        print(f"\n[{d}]")
        for f, m in items[:3]:
            rel = f.relative_to(ROOT)
            print(f"  {rel} ({len(m)} 处)")
            for lineno, content in m[:2]:
                print(f"    L{lineno}: {content[:100]}")


if __name__ == "__main__":
    main()
