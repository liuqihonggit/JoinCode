#!/usr/bin/env python3
"""resize_probe — TUI resize 物理终端视角感知探针（L3）。

与 L2 像素比对（compare_reference.py，进程内 80×24 固定场景）互补：
本工具从 **物理终端视角** 感知渲染结果 — ConPTY 承载真实目标 exe（jcc.exe /
NativeTui.Demo），按相位序列执行「尺寸 A → settle → 尺寸 B → settle → …」，
每相位从 ConPTY 下游字节流（= Windows Terminal 所见的同一流）重放到
pyte 独立 VT 解释器，转储可见网格 + 标记行定位 + 光标位，回答一个此前无法
观测的问题：**resize 之后，banner/输入框到底落在视口第几行**。

流程：
  1. spawn 目标 exe 于 ConPTY（初始尺寸 = 首相位）；
  2. 后台线程持续读 ConPTY 下游流并累计（原始流保存 stream.bin — 原始素材留存）；
  3. 每相位按 settle 毫秒静置后记录字节偏移快照；
  4. 相位 i = 字节流 [0, offset_i) 在 pyte 全量重放（自包含、确定性、可重复），
     dump 网格 + 标记（--mark 正则）所在行 + 光标位置；
  5. 输出 report.md（各相位网格 + 跨相位标记行汇总表）+ 各相位网格 txt
     （--png 时附 PNG 截图，需 Pillow + Cascadia Mono/consolas）。

用法：
  python resize_probe.py --target D:/project/w1/artifacts/bin/JoinCode/Debug/net10.0/jcc.exe ^
    --phases 120x30@800,80x24@900,140x40@900 --out .xxx/resize_probe/jcc ^
    --mark "jcc v" --mark "❯"

依赖：pip install pyte pywinpty（--png 另需 Pillow）。
"""

from __future__ import annotations

import argparse
import codecs
import dataclasses
import json
import re
import shutil
import sys
import threading
import time
from pathlib import Path

import pyte
from winpty import PtyProcess

DEFAULT_MARKS = ["jcc v", "❯", "查看快捷键"]


@dataclasses.dataclass
class Phase:
    """一个观察相位：目标尺寸 + 静置毫秒 + 字节流结束偏移。"""

    cols: int
    rows: int
    settle_ms: int
    stream_offset: int = 0
    alive: bool = True


@dataclasses.dataclass
class MarkHit:
    """标记（正则）在相位网格中的定位结果。"""

    pattern: str
    row: int | None  # 0-based；None = 视口内不可见（滚入 scrollback 或未渲染）
    line: str = ""


class ConptyRecorder:
    """ConPTY 下游字节流持续录制（后台线程 + 锁保护累计缓冲）。"""

    def __init__(self, proc: PtyProcess) -> None:
        self._proc = proc
        self._buf = bytearray()
        self._lock = threading.Lock()
        self._decoder = codecs.getincrementaldecoder("utf-8")("replace")
        self._thread = threading.Thread(target=self._pump, daemon=True)
        self._thread.start()

    def _pump(self) -> None:
        while self._proc.isalive():
            try:
                chunk = self._proc.read(4096)
            except (EOFError, OSError):
                break
            if not chunk:
                continue
            with self._lock:
                self._buf.extend(chunk.encode("utf-8", errors="replace"))

    def offset(self) -> int:
        with self._lock:
            return len(self._buf)

    def snapshot(self) -> bytes:
        with self._lock:
            return bytes(self._buf)

    def stop(self) -> None:
        self._thread.join(timeout=2.0)


def parse_phases(spec: str) -> list[Phase]:
    """解析 "120x30@800,80x24@900" → 相位列表（@settle 缺省 800ms）。"""
    phases: list[Phase] = []
    for item in spec.split(","):
        item = item.strip()
        if not item:
            continue
        size, _, settle = item.partition("@")
        match = re.fullmatch(r"(\d+)x(\d+)", size.strip())
        if not match:
            raise argparse.ArgumentTypeError(f"相位尺寸格式错误: {item!r}（应为 WxH[@ms]，如 120x30@800）")
        phases.append(
            Phase(cols=int(match.group(1)), rows=int(match.group(2)), settle_ms=int(settle or 800))
        )
    if not phases:
        raise argparse.ArgumentTypeError("至少需要一个相位")
    return phases


def replay_grid(stream: bytes, cols: int, rows: int) -> tuple[list[str], tuple[int, int]]:
    """字节流在 pyte（独立 VT 解释器）全量重放 → 可见网格 + 光标位。

    每相位全新 Screen 重放至该相位偏移 — 自包含、确定性，同一输入永远同一网格。
    ConPTY 的 resize reflow 已体现在它发给下游的字节流里，pyte 只负责忠实解码。
    """
    screen = pyte.Screen(cols, rows)
    screen.set_mode(pyte.modes.LNM)
    vt = pyte.Stream(screen)
    vt.feed(stream.decode("utf-8", errors="replace"))
    return list(screen.display), (screen.cursor.y, screen.cursor.x)


def find_marks(lines: list[str], patterns: list[str]) -> list[MarkHit]:
    hits: list[MarkHit] = []
    for pattern in patterns:
        hit = MarkHit(pattern=pattern, row=None)
        for row, line in enumerate(lines):
            if re.search(pattern, line):
                hit.row, hit.line = row, line.rstrip()
                break
        hits.append(hit)
    return hits


def render_grid(lines: list[str], cursor: tuple[int, int]) -> str:
    """网格 → 带行列标尺的文本（标尺与 tmux capture-pane 风格一致）。"""
    cols = len(lines[0]) if lines else 0
    tens = "".join(str((col // 10) % 10) if col % 10 == 0 else " " for col in range(cols))
    units = "".join(str(col % 10) for col in range(cols))
    body = [f"    {tens}", f"    {units}"]
    for row, line in enumerate(lines):
        pointer = " *" if row == cursor[0] else "  "
        body.append(f"{row:>3}|{line.rstrip()}{pointer}")
    if cursor[0] < len(lines):
        col_pointer = ["   |"] + [" "] * cols
        col_pointer[4 + min(cursor[1], cols - 1)] = "^"
        body.append("".join(col_pointer))
    return "\n".join(body)


def save_png(lines: list[str], path: Path) -> bool:
    """网格 → PNG 截图（Pillow + Cascadia Mono/consolas；缺依赖静默降级 False）。"""
    try:
        from PIL import Image, ImageDraw, ImageFont
    except ImportError:
        return False

    font = None
    for name in ("CascadiaMono.ttf", "CascadiaCode.ttf", "consola.ttf"):
        candidate = Path("C:/Windows/Fonts") / name
        if candidate.exists():
            font = ImageFont.truetype(str(candidate), 16)
            break
    if font is None:
        return False

    left, top = 8, 4
    cell_w = max(font.getbbox("M")[2] for _ in [0])
    cell_h = font.getbbox("Ag")[3] + 2
    cols = max((len(line) for line in lines), default=1)
    img = Image.new("RGB", (left + cols * cell_w + 8, top + len(lines) * cell_h + 6), (16, 16, 20))
    draw = ImageDraw.Draw(img)
    draw.text((left, top), "\n".join(line.rstrip() for line in lines), font=font, fill=(220, 220, 225))
    img.save(path)
    return True


def run(args: argparse.Namespace) -> int:
    phases = parse_phases(args.phases)
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)
    marks = args.mark or DEFAULT_MARKS

    target_argv = [args.target, *args.target_args]
    print(f"[probe] spawn: {' '.join(target_argv)} @ {phases[0].cols}x{phases[0].rows}", flush=True)
    proc = PtyProcess.spawn(
        target_argv,
        cwd=args.cwd or None,
        dimensions=(phases[0].rows, phases[0].cols),
    )
    recorder = ConptyRecorder(proc)
    exitstatus: int | None = None

    try:
        for index, phase in enumerate(phases):
            if index > 0:
                if not proc.isalive():
                    phase.alive = False
                    break
                proc.setwinsize(phase.rows, phase.cols)
                print(f"[probe] resize → {phase.cols}x{phase.rows}", flush=True)
            time.sleep(phase.settle_ms / 1000.0)
            phase.stream_offset = recorder.offset()
            phase.alive = proc.isalive()
            if not phase.alive:
                exitstatus = proc.exitstatus
    finally:
        if proc.isalive():
            proc.terminate(force=True)
            time.sleep(0.3)
            if proc.isalive():
                proc.kill(1)
        stream = recorder.snapshot()
        recorder.stop()

    Path(out_dir / "stream.bin").write_bytes(stream)
    Path(out_dir / "phases.json").write_text(
        json.dumps([dataclasses.asdict(p) for p in phases], ensure_ascii=False, indent=2),
        encoding="utf-8",
    )

    report: list[str] = [
        "# resize_probe 感知报告",
        "",
        f"- 目标：`{' '.join(target_argv)}`",
        f"- 相位序列：{args.phases}",
        f"- 原始流：stream.bin（{len(stream)} 字节）— pyte 全量重放至各相位偏移",
        f"- 退出状态：{'运行中被终止' if exitstatus is None else exitstatus}",
        "",
        "## 标记行汇总（0-based，None = 视口内不可见）",
        "",
        "| 相位 | 尺寸 | " + " | ".join(f"`{m}`" for m in marks) + " | 光标行 |",
        "|---|---|" + "---|" * (len(marks) + 1),
    ]

    summaries: list[tuple[Phase, list[MarkHit], tuple[int, int], list[str]]] = []
    for index, phase in enumerate(phases):
        if phase.stream_offset == 0:
            summaries.append((phase, [], (0, 0), []))
            continue
        lines, cursor = replay_grid(stream[: phase.stream_offset], phase.cols, phase.rows)
        summaries.append((phase, find_marks(lines, marks), cursor, lines))

    for index, (phase, hits, cursor, _) in enumerate(summaries):
        if not hits:
            report.append(
                f"| {index} | {phase.cols}x{phase.rows} | " + " | ".join(["（无输出）"] * (len(marks) + 1)) + " |"
            )
            continue
        report.append(
            f"| {index} | {phase.cols}x{phase.rows} | "
            + " | ".join(str(h.row) for h in hits)
            + f" | {cursor[0]} |"
        )

    for index, (phase, hits, cursor, lines) in enumerate(summaries):
        if phase.stream_offset == 0:
            report += ["", f"## 相位 {index} — {phase.cols}×{phase.rows}（无输出）", ""]
            continue
        grid_txt = render_grid(lines, cursor)
        grid_path = out_dir / f"{index:02d}_{phase.cols}x{phase.rows}.txt"
        grid_path.write_text(grid_txt + "\n", encoding="utf-8")
        if args.png:
            png_path = grid_path.with_suffix(".png")
            if save_png(lines, png_path):
                report.append(f"![phase {index}]({png_path.name})")
        report += [
            "",
            f"## 相位 {index} — {phase.cols}×{phase.rows}（settle {phase.settle_ms}ms，"
            f"流偏移 {phase.stream_offset}，存活={phase.alive}）",
            "",
            "```text",
            grid_txt,
            "```",
            "",
        ]
        for hit in hits:
            where = f"第 {hit.row} 行：{hit.line!r}" if hit.row is not None else "视口内不可见"
            report.append(f"- 标记 `{hit.pattern}` → {where}")
        report.append("")

    report_path = out_dir / "report.md"
    report_path.write_text("\n".join(report) + "\n", encoding="utf-8")
    print(f"[probe] 报告: {report_path}", flush=True)
    print(f"[probe] 原始流: {out_dir / 'stream.bin'}（{len(stream)} 字节）", flush=True)
    return 0


def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--target", required=True, help="目标 exe 路径（jcc.exe / NativeTui.Demo.exe）")
    parser.add_argument("--target-args", nargs="*", default=[], help="目标 exe 附加参数")
    parser.add_argument("--cwd", help="目标工作目录（缺省继承探针进程）")
    parser.add_argument("--phases", required=True, help='相位序列 "120x30@800,80x24@900"（WxH@settle_ms）')
    parser.add_argument("--out", required=True, help="输出目录（原始流/网格/报告）")
    parser.add_argument("--mark", action="append", help=f"定位标记正则（可重复；缺省 {DEFAULT_MARKS}）")
    parser.add_argument("--png", action="store_true", help="每相位附 PNG 截图（需 Pillow + 系统等宽字体）")
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    sys.exit(main())
