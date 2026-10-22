"""把 DSH 前端快照打包为 GUI 嵌入资源（dsh_web.zip）。

用法: python capture/package_dsh_web.py <snapshotDir> <outZip>

过滤规则:
  - 保留 dsh-web-frontend/dist 全量（assets/fonts/favicon/manifest）
  - 保留客户端插件的全部 JS（client.js + require.async 懒加载 chunk）：
    终端面板 client.terminal.js、文档预览 client.pdf.js / client.excel.js
    缺任一分块都会让对应 GUI 组件在打开时静默失败（TASK065）
  - 用宿主注入后的首页（snapshot/index.html）覆盖 dist/index.html
zip 布局: index.html + assets/** + plugins/** + meta.json
"""

import json
import sys
import zipfile
from pathlib import Path

LAZY_CHUNKS = ("client.pdf.js", "client.excel.js", "client.terminal.js")
SKIP_TOP = {"index-root.html", "open-in-app"}


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: python capture/package_dsh_web.py <snapshotDir> <outZip>")
        return 1
    snapshot = Path(sys.argv[1]).resolve()
    out_zip = Path(sys.argv[2]).resolve()
    www = snapshot / "www"
    if not www.is_dir():
        print(f"snapshot www not found: {www}")
        return 1

    root_index = snapshot / "index.html"
    if not root_index.is_file():
        print(f"snapshot index not found: {root_index}")
        return 1

    files = []
    for path in sorted(www.rglob("*")):
        if not path.is_file():
            continue
        rel = path.relative_to(www).as_posix()
        if rel.split("/")[0] in SKIP_TOP:
            continue
        if rel == "index.html":
            continue
        files.append((rel, path))

    included = {rel for rel, _ in files}
    missing = [chunk for chunk in LAZY_CHUNKS if not any(rel.endswith("/" + chunk) for rel in included)]
    if missing:
        print(f"missing lazy chunks: {missing}")
        return 1

    boot = json.loads((snapshot / "boot.json").read_text(encoding="utf-8"))
    out_zip.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(out_zip, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        zf.writestr("index.html", root_index.read_bytes())
        for rel, path in files:
            zf.write(path, rel)
        zf.writestr("meta.json", json.dumps({
            "dshVersion": "0.2.0-rc.2",
            "entries": len(boot.get("entries", [])),
            "batches": len(boot.get("batches", [])),
            "files": len(files) + 1,
            "includedLazyChunks": list(LAZY_CHUNKS),
        }, indent=2))
    print(f"zip={out_zip} files={len(files) + 1} sizeMB={out_zip.stat().st_size / 1024 / 1024:.2f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
