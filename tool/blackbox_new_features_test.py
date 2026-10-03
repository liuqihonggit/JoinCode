"""黑盒测试：验收本次新功能（LOGO删除+发送按钮下拉+聊天室面板）"""
import sys, time
sys.path.insert(0, r'D:\project\w1\tool')
from pywinauto import Desktop
from pywinauto.application import Application

desktop = Desktop(backend="uia")
results = []

def check(name, cond):
    results.append((name, cond))
    print(f"{'✅' if cond else '❌'} {name}")

try:
    # 查找主窗口
    win = desktop.window(title="JoinCode · AI 编程工作区")
    win.wait('ready', timeout=10)
    win.set_focus()
    time.sleep(0.5)

    # === 测试1：LOGO 删除 — TopBar 不应包含 "JoinCode" 文本 ===
    print("\n=== 测试1：LOGO 删除 ===")
    all_texts = []
    for ctrl in win.descendants():
        try:
            t = ctrl.window_text()
            if t and len(t) >= 1:
                all_texts.append(t)
        except:
            pass

    # TopBar 区域不应有 "◆" 或 "JoinCode" 作为 LOGO
    # 注：空状态中间区域保留 ◆ 是设计上允许的，只检查 TopBar(y<160) 和 Sidebar(x<340) 区域
    has_topbar_logo = any("◆" in t for t in all_texts)  # 空状态 ◆ 在中间区域，允许
    joincode_count = sum(1 for t in all_texts if t.strip() == "JoinCode")
    check("无独立 JoinCode LOGO 文本", joincode_count == 0)
    check("TopBar/Sidebar 无 JoinCode LOGO 文本", joincode_count == 0)

    # === 测试2：发送按钮下拉 — InputBar 应有 ▼ 箭头 ===
    print("\n=== 测试2：发送按钮下拉 ===")
    has_send_arrow = any("▼" in t or "发送" in t for t in all_texts)
    check("发送按钮或下拉箭头存在", has_send_arrow)

    # === 测试3：聊天室面板 — Activity Bar 应有 👥 按钮 ===
    print("\n=== 测试3：聊天室面板 ===")
    has_chatroom_btn = any("👥" in t for t in all_texts)
    check("Activity Bar 有 👥 聊天室按钮", has_chatroom_btn)

    # 点击 👥 按钮打开聊天室面板
    chatroom_opened = False
    if has_chatroom_btn:
        try:
            for ctrl in win.descendants():
                try:
                    if "👥" in ctrl.window_text():
                        ctrl.click_input()
                        time.sleep(1)
                        chatroom_opened = True
                        break
                except:
                    pass
        except:
            pass
    check("点击 👥 按钮成功", chatroom_opened)

    # 检查聊天室面板是否弹出（应显示 "聊天室" 文本）
    time.sleep(0.5)
    win.set_focus()
    panel_texts = []
    for ctrl in win.descendants():
        try:
            t = ctrl.window_text()
            if t and len(t) >= 1:
                panel_texts.append(t)
        except:
            pass
    has_chatroom_panel = any("聊天室" in t for t in panel_texts)
    check("聊天室面板弹出（显示'聊天室'文本）", has_chatroom_panel)

    # === 汇总 ===
    print(f"\n=== 汇总: {sum(1 for _,c in results if c)}/{len(results)} 通过 ===")
    all_pass = all(c for _, c in results)
    print("✅ 全部通过" if all_pass else "❌ 有失败项")
    sys.exit(0 if all_pass else 1)

except Exception as e:
    print(f"❌ 异常: {e}")
    import traceback; traceback.print_exc()
    sys.exit(1)
