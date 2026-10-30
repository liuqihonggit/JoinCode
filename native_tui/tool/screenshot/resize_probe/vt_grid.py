#!/usr/bin/env python3
"""vt_grid — 忠实 conhost/Windows Terminal 语义的最小 VT 解码器（列级分析用）。

pyte 把 CJK 按 1 列解码（实测 '中文ab' 占 4 列，真实终端 6 列），列级分析不可信；
本解码器按 wcwidth 宽度推进光标，并模拟 conhost 的 pending-wrap（DECAWM）：
末列写 printable → 光标停在末列置 wrap 标记；下一个 printable 换行（底行滚动）；
CSI 光标移动（CUU/CUD/CUF/CUB/CUP/H）与 CR/LF 清除 wrap 标记 — 对齐 xterm/conhost 行为。

支持序列：CSI CUP/H、CUU/CUD/CUF/CUB、ED(2J/3J)、EL(0/1/2)、SGR(忽略)、DEC 2026(忽略)、
OSC(跳过)、DECAWM 模式位。网格 = wcwidth 宽字符占 2 格（尾格占位）。
"""

from __future__ import annotations

import re
from wcwidth import wcwidth


class VtGrid:
    def __init__(self, cols: int, rows: int) -> None:
        self.cols, self.rows = cols, rows
        self.cells: list[list[str]] = [[" "] * cols for _ in range(rows)]
        self.x = self.y = 0
        self.wrap_pending = False
        self.autowrap = True
        self.scrollback: list[list[str]] = []  # 3J 清空；滚出顶部的行留档

    # ---- 基础操作 ----
    def _scroll_up(self) -> None:
        self.scrollback.append(self.cells[0])
        self.cells = self.cells[1:] + [[" "] * self.cols for _ in range(1)]

    def _advance(self) -> None:
        self.x += 1
        if self.x >= self.cols:
            self.x = self.cols - 1
            self.wrap_pending = True

    def _put(self, ch: str) -> None:
        width = max(wcwidth(ch), 1)
        if width == 2 and self.x >= self.cols - 1 and self.autowrap:
            self.x = self.cols - 1
            self.wrap_pending = True
        if self.wrap_pending:
            self.wrap_pending = False
            self.x = 0
            self.y += 1
            if self.y >= self.rows:
                self.y = self.rows - 1
                self._scroll_up()
        self.cells[self.y][self.x] = ch
        for pad in range(1, width):
            if self.x + pad < self.cols:
                self.cells[self.y][self.x + pad] = ""  # 宽字符尾格占位
        self.x += width
        if self.x >= self.cols:
            self.x = self.cols - 1
            self.wrap_pending = True

    def _move(self, row: int, col: int) -> None:
        self.wrap_pending = False
        self.y = max(0, min(self.rows - 1, row))
        self.x = max(0, min(self.cols - 1, col))

    # ---- 输入入口 ----
    def feed(self, text: str) -> None:
        i, n = 0, len(text)
        while i < n:
            ch = text[i]
            if ch == "\x1b":
                i = self._escape(text, i)
                continue
            if ch == "\r":
                self.wrap_pending = False
                self.x = 0
            elif ch == "\n":
                self.wrap_pending = False
                self.y += 1
                if self.y >= self.rows:
                    self.y = self.rows - 1
                    self._scroll_up()
            elif ch == "\b":
                self.wrap_pending = False
                self.x = max(0, self.x - 1)
            elif ch == "\t":
                self.wrap_pending = False
                self.x = min(self.cols - 1, (self.x // 8 + 1) * 8)
            elif ord(ch) >= 32:
                self._put(ch)
            i += 1

    def _escape(self, text: str, i: int) -> int:
        n = len(text)
        if i + 1 >= n:
            return i + 1
        nxt = text[i + 1]
        if nxt == "]":  # OSC — BEL 或 ST 终止
            j = i + 2
            while j < n and text[j] not in ("\x07",):
                if text[j] == "\x1b" and j + 1 < n and text[j + 1] == "\\":
                    return j + 2
                j += 1
            return j + 1
        if nxt != "[":
            return i + 2  # ESC x 两字符序列（含 charset 等）— 忽略
        m = re.match(r"\x1b\[([\x30-\x3f]*)([\x20-\x2f]*)([\x40-\x7e])", text[i:])
        if not m:
            return i + 2
        params_raw, _, final = m.groups()
        params = [int(p) if p.isdigit() else 0 for p in params_raw.replace("?", "").split(";") if p != ""]
        p1 = params[0] if params else None
        if final == "H" or final == "f":  # CUP（1 起）
            row = (params[0] if len(params) > 0 else 1) - 1
            col = (params[1] if len(params) > 1 else 1) - 1
            self._move(row, col)
        elif final == "A":  # CUU
            self._move(self.y - (p1 or 1), self.x)
        elif final == "B":  # CUD
            self._move(self.y + (p1 or 1), self.x)
        elif final == "C":  # CUF
            self._move(self.y, self.x + (p1 or 1))
        elif final == "D":  # CUB
            self._move(self.y, self.x - (p1 or 1))
        elif final == "J":  # ED — 2=视口, 3=视口+scrollback
            if p1 in (2, 3):
                self.cells = [[" "] * self.cols for _ in range(self.rows)]
            if p1 == 3:
                self.scrollback.clear()
        elif final == "K":  # EL — 0=至行尾, 1=至行首, 2=整行
            if p1 in (0, 2):
                for c in range(self.x, self.cols):
                    self.cells[self.y][c] = " "
            if p1 in (1, 2):
                for c in range(0, min(self.x + 1, self.cols)):
                    self.cells[self.y][c] = " "
        elif final == "h" or final == "l":  # 模式位（?7 DECAWM 等）— 忽略/记录 autowrap
            if params_raw.startswith("?7") and final == "l":
                self.autowrap = False
            if params_raw.startswith("?7") and final == "h":
                self.autowrap = True
        # SGR(m)/DEC 2026/其他 — 忽略
        return i + m.end()

    # ---- 输出 ----
    def display(self) -> list[str]:
        out = []
        for row in self.cells:
            out.append("".join(ch for ch in row if ch != "").ljust(self.cols))
        return out

    def grid_text(self, cursor_marker: bool = False) -> str:
        lines = self.display()
        return "\n".join(lines)
