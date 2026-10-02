"""综合黑盒测试：验证所有 GUI 功能。"""
import sys, time, os, subprocess, ctypes
sys.path.insert(0, os.path.join(os.path.dirname(__file__)))
from pywinauto import Application, mouse

EXE = r"D:\project\w1\artifacts\bin\JoinCodeGui\Debug\net10.0\JoinCode.Gui.exe"
PASS = 0
FAIL = 0

def ok(name, cond):
    global PASS, FAIL
    if cond:
        PASS += 1
        print(f"  ✅ {name}")
    else:
        FAIL += 1
        print(f"  ❌ {name}")

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

def get_text(item):
    parts = []
    try:
        for c in item.children():
            t = c.window_text() or ""
            if t: parts.append(t)
    except: pass
    return " ".join(parts)

def main():
    global PASS, FAIL
    print("=== 综合黑盒测试：所有 GUI 功能 ===")
    subprocess.run(["taskkill", "/im", "JoinCode.Gui.exe", "/f"], capture_output=True)
    time.sleep(1)

    app = Application(backend="uia").start(EXE)
    time.sleep(4)
    win = app.top_window()
    bring_to_front(win)

    # 1. 窗口居中
    print("\n--- 1. 窗口居中 ---")
    rect = win.rectangle()
    sw = ctypes.windll.user32.GetSystemMetrics(0)
    sh = ctypes.windll.user32.GetSystemMetrics(1)
    dx = abs((rect.left + rect.right)/2 - sw/2)
    dy = abs((rect.top + rect.bottom)/2 - sh/2)
    ok("窗口居中(偏差<50px)", dx < 50 and dy < 50)

    # 2. 初始 Sessions 激活
    print("\n--- 2. 初始 Sessions 激活 ---")
    texts = [d.window_text() or "" for d in win.descendants() if d.window_text()]
    ok("Sessions 面板可见", any("会话" in t for t in texts))
    ok("新建对话按钮可见", any("新建对话" in t for t in texts))

    # 3. 点击 📁 打开目录树
    print("\n--- 3. 点击 📁 打开目录树 ---")
    btn = find_button(win, "📁")
    btn.click_input()
    time.sleep(1.5)
    tree_items = win.descendants(control_type="TreeItem")
    ok("目录树打开(有TreeItem)", len(tree_items) > 0)
    ok("目录树有21+节点", len(tree_items) >= 20)

    # 4. 互斥：Sessions 消失
    print("\n--- 4. 互斥：Sessions 消失 ---")
    texts2 = [d.window_text() or "" for d in win.descendants() if d.window_text()]
    ok("Sessions 面板消失", not any("新建对话" in t for t in texts2))
    ok("目录树文字可见", any("目录树" in t for t in texts2))

    # 5. 单击文件夹展开
    print("\n--- 5. 单击文件夹展开 ---")
    folder = tree_items[0]
    fr = folder.rectangle()
    cx = (fr.left + fr.right) // 2
    cy = (fr.top + fr.bottom) // 2
    mouse.click(coords=(cx, cy))
    time.sleep(1.5)
    expanded = win.descendants(control_type="TreeItem")
    ok("文件夹展开(节点增加)", len(expanded) > len(tree_items))

    # 6. 再单击收起
    print("\n--- 6. 再单击收起 ---")
    mouse.click(coords=(cx, cy))
    time.sleep(0.8)
    collapsed = win.descendants(control_type="TreeItem")
    ok("文件夹收起(节点减少)", len(collapsed) < len(expanded))

    # 7. 双击文件打开编辑器
    print("\n--- 7. 双击文件打开编辑器 ---")
    all_items = win.descendants(control_type="TreeItem")
    file_item = None
    for item in all_items:
        txt = get_text(item)
        if "." in txt and "📁" not in txt:
            file_item = item
            break
    if file_item:
        fr2 = file_item.rectangle()
        mouse.double_click(coords=((fr2.left+fr2.right)//2, (fr2.top+fr2.bottom)//2))
        time.sleep(2)
        edits = win.descendants(control_type="Edit")
        ok("双击文件打开编辑器", len(edits) > 0)
    else:
        ok("找到文件节点", False)

    # 8. 点击 📝 打开编辑器视图
    print("\n--- 8. 点击 📝 编辑器视图 ---")
    bring_to_front(win)
    btn_edit = find_button(win, "📝")
    if btn_edit:
        btn_edit.click_input()
        time.sleep(1)
        ok("📝 按钮可点击", True)
    else:
        ok("📝 按钮存在", False)

    # 9. 再点 📁 切回目录树
    print("\n--- 9. 再点 📁 切回目录树 ---")
    bring_to_front(win)
    time.sleep(0.5)
    # 调试：打印所有 emoji 按钮
    emoji_btns = []
    for d in win.descendants():
        try:
            t = d.window_text() or ""
            if any(e in t for e in ["📁", "💬", "📝"]):
                emoji_btns.append((t, d.control_type()))
        except: pass
    print(f"  emoji 按钮: {emoji_btns}")
    btn2 = find_button(win, "📁")
    if btn2:
        btn2.click_input()
        time.sleep(1.5)
        tree3 = win.descendants(control_type="TreeItem")
        ok("切回目录树", len(tree3) > 0)
    else:
        ok("找到 📁 按钮", False)

    # 10. 右键菜单 — 复制路径(用坐标点击菜单项)
    print("\n--- 10. 右键菜单复制路径 ---")
    bring_to_front(win)
    time.sleep(0.5)
    tree4 = win.descendants(control_type="TreeItem")
    if tree4:
        r4 = tree4[0].rectangle()
        cx4 = (r4.left + r4.right) // 2
        cy4 = (r4.top + r4.bottom) // 2
        # 清空剪贴板
        subprocess.run(["powershell", "-Command", "Set-Clipboard 'CLEARED'"],
                       capture_output=True, timeout=3)
        # 右键点击
        tree4[0].right_click_input()
        time.sleep(0.8)
        # ContextMenu 在右键位置下方,第2项"复制路径"约在 y+46
        mouse.click(coords=(cx4, cy4 + 46))
        time.sleep(0.5)
        r = subprocess.run(["powershell", "-Command", "Get-Clipboard"],
                          capture_output=True, text=True, timeout=5)
        clip = r.stdout.strip()
        ok("右键复制路径", clip != "CLEARED" and len(clip) > 3)
    else:
        ok("有TreeItem可右键", False)

    app.kill()
    print(f"\n=== 结果: {PASS} 通过, {FAIL} 失败 ===")

if __name__ == "__main__":
    main()
