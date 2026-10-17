import os
import re
import glob

repo_root = r"D:\project\w1"
cs_files = glob.glob(os.path.join(repo_root, "modules", "**", "*.cs"), recursive=True)
cs_files += glob.glob(os.path.join(repo_root, "test", "**", "*.cs"), recursive=True)
cs_files += glob.glob(os.path.join(repo_root, "non_deliverables_tools", "**", "*.cs"), recursive=True)

modified_count = 0

for fpath in cs_files:
    with open(fpath, 'r', encoding='utf-8') as f:
        content = f.read()

    original = content

    # Pattern: #pragma warning disable JCC11003
    # Case 1: only JCC11003 -> remove whole line
    content = re.sub(r'^[ \t]*#pragma warning disable JCC11003[ \t]*\r?\n', '', content, flags=re.MULTILINE)

    # Case 2: JCC11003 among others -> remove just JCC11003
    # e.g. "#pragma warning disable JCC9001, JCC11003" -> "#pragma warning disable JCC9001"
    # e.g. "#pragma warning disable JCC11003, JCC9001" -> "#pragma warning disable JCC9001"
    # e.g. "#pragma warning disable JCC11003, JCC9001, JCC3010" -> "#pragma warning disable JCC9001, JCC3010"
    def remove_jcc11003(match):
        prefix = match.group(1)
        rules = match.group(2)
        rule_list = [r.strip() for r in rules.split(',')]
        rule_list = [r for r in rule_list if r != 'JCC11003']
        if not rule_list:
            return ''
        return f'{prefix}#pragma warning disable {", ".join(rule_list)}'

    content = re.sub(r'^([ \t]*)#pragma warning disable ([^\n]*JCC11003[^\n]*)$', remove_jcc11003, content, flags=re.MULTILINE)

    if content != original:
        with open(fpath, 'w', encoding='utf-8') as f:
            f.write(content)
        modified_count += 1

print(f"Modified {modified_count} files")
