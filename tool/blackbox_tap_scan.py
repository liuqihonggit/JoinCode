"""验证：单击文件夹 StackPanel 展开 + 双击文件打开编辑器。"""
import sys, time, os, subprocess, ctypes
sys.path.insert(0, os.path.join(os.path.dirname(__file__)))
from pywinauto import Application, mouse

EXE = r"D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"

def bring_to_front(win):
    win.set_focus()
    time.sleep(0.3)
    ctypes.windll.user32.SetForegroundWindow(win.handle)
    time.sleep(0.3)

def find_button(win, icon):
    for d in win.descendants():
        try:
            if icon in (d.window_text() or ""):
                return d
        except Exception:
            pass
    return None

subprocess.run(["taskkill", "/im", "JoinCode.Gui.exe", "/f"], capture_output=True)
time.sleep(1)

app = Application(backend="uia").start(EXE)
time.sleep(4)
win = app.top_window()
bring_to_front(win)

btn = find_button(win, "📁")
btn.click_input()
time.sleep(1.5)
print("✅ 点击 📁 打开目录树")

# 找到所有 TreeItem
tree_items = win.descendants(control_type="TreeItem")
print(f"  初始 TreeItem 数: {len(tree_items)}")

# 找到第一个文件夹 TreeItem 的文本位置
folder = tree_items[0]
rect = folder.rectangle()
print(f"  文件夹 rect={rect}")

# 尝试点击不同位置 — StackPanel 可能在 TreeItem 内部的某个位置
# TreeItem rect: L400 T234 R627 B266 → 宽227 高32
# StackPanel 包含图标+文本,可能在左侧偏右一点的位置
# 尝试从左到右扫描
print("\n--- 扫描点击位置 ---")
initial_count = len(tree_items)
for offset in range(5, 120, 5):
    cx = rect.left + offset
    cy = (rect.top + rect.bottom) // 2
    mouse.click(coords=(cx, cy))
    time.sleep(0.8)
    after = win.descendants(control_type="TreeItem")
    if len(after) > initial_count:
        print(f"✅ 偏移 {offset}px ({cx},{cy}) 展开成功! TreeItem={len(after)}")
        # 打印子节点
        for item in after[initial_count:initial_count+5]:
            parts = []
            try:
                for child in item.children():
                    t = child.window_text() or ""
                    if t:
                        parts.append(t)
            except Exception:
                pass
            print(f"    子节点: {' '.join(parts)}")
        # 再点一次收起
        mouse.click(coords=(cx, cy))
        time.sleep(0.5)
        collapsed = win.descendants(control_type="TreeItem")
        print(f"  再点收起: TreeItem={len(collapsed)}")
        break
    time.sleep(0.2)
else:
    print("❌ 所有位置都未展开")

app.kill()
print("\n=== 结束 ===")
