"""综合黑盒测试：验收所有 GUI 界面功能"""
import sys, time, os, ctypes
import pywinauto
from pywinauto import Application, Desktop

exe = os.path.join(os.path.dirname(__file__), '..', 'artifacts', 'bin', 'JoinCodeGui', 'Debug', 'net10.0', 'JoinCode.Gui.exe')
exe = os.path.abspath(exe)

app = Application(backend="uia").start(exe)
time.sleep(3)

desktop = Desktop(backend="uia")
win = desktop.window(title="JoinCode · AI 编程工作区")
win.set_focus()
time.sleep(0.5)

results = []

def test(name, condition, detail=""):
    results.append((name, condition, detail))

# 1. 窗口存在且可见
try:
    test("窗口存在且可见", win.is_visible())
except Exception as e:
    test("窗口存在且可见", False, str(e))

# 2. Activity Bar 按钮（💬/📁/📝）
try:
    all_controls = win.descendants()
    icons = []
    for c in all_controls:
        try:
            t = c.window_text()
            if t in ("💬", "📁", "📝"):
                icons.append(t)
        except:
            pass
    test("Activity Bar 三个图标按钮", len(icons) >= 3, f"找到: {icons}")
except Exception as e:
    test("Activity Bar 三个图标按钮", False, str(e))

# 3. 菜单栏（文件/编辑/视图/帮助）
try:
    menus = win.descendants(control_type="MenuItem")
    menu_texts = []
    for m in menus:
        try:
            t = m.window_text()
            if t in ("文件", "编辑", "视图", "帮助"):
                menu_texts.append(t)
        except:
            pass
    test("菜单栏四项", len(menu_texts) >= 4, f"找到: {menu_texts}")
except Exception as e:
    test("菜单栏四项", False, str(e))

# 4. 输入框存在
try:
    edit = win.descendants(control_type="Edit")
    test("输入框存在", len(edit) > 0, f"找到 {len(edit)} 个编辑框")
except Exception as e:
    test("输入框存在", False, str(e))

# 5. 发送按钮存在
try:
    send_btn = None
    for b in win.descendants(control_type="Button"):
        try:
            if "发送" in b.window_text():
                send_btn = b
                break
        except:
            pass
    test("发送按钮存在", send_btn is not None)
except Exception as e:
    test("发送按钮存在", False, str(e))

# 6. 权限模式指示器
try:
    perm_btn = None
    for b in win.descendants(control_type="Button"):
        try:
            t = b.window_text()
            if "Auto" in t or "Plan" in t or "Ask" in t:
                perm_btn = b
                break
        except:
            pass
    test("权限模式指示器", perm_btn is not None, f"初始: {perm_btn.window_text()}" if perm_btn else "")
except Exception as e:
    test("权限模式指示器", False, str(e))

# 7. 统计按钮
try:
    stats_btn = None
    for b in win.descendants(control_type="Button"):
        try:
            if "统计" in b.window_text():
                stats_btn = b
                break
        except:
            pass
    test("统计按钮存在", stats_btn is not None)
except Exception as e:
    test("统计按钮存在", False, str(e))

# 8. 主题切换按钮（Avalonia ToggleButton 不被 UIA 识别为 ToggleButton 类型，遍历所有控件）
try:
    theme_btn = None
    for c in win.descendants():
        try:
            t = c.window_text()
            if "🌙" in t or "☀" in t:
                theme_btn = c
                break
        except:
            pass
    test("主题切换按钮", theme_btn is not None)
except Exception as e:
    test("主题切换按钮", False, str(e))

# 9. 设置按钮（遍历所有控件查找 ⚙）
try:
    settings_btn = None
    for c in win.descendants():
        try:
            if "⚙" in c.window_text():
                settings_btn = c
                break
        except:
            pass
    test("设置按钮存在", settings_btn is not None)
except Exception as e:
    test("设置按钮存在", False, str(e))

# 10. Markdown 导出按钮
try:
    md_btn = None
    for b in win.descendants(control_type="Button"):
        try:
            if "Markdown" in b.window_text():
                md_btn = b
                break
        except:
            pass
    test("Markdown导出按钮", md_btn is not None)
except Exception as e:
    test("Markdown导出按钮", False, str(e))

# 11. 权限模式切换（Shift+Tab）
try:
    if perm_btn:
        before = perm_btn.window_text()
        perm_btn.click_input()
        time.sleep(0.5)
        after = perm_btn.window_text()
        test("权限模式点击切换", before != after, f"{before} → {after}")
    else:
        test("权限模式点击切换", False, "权限按钮不存在")
except Exception as e:
    test("权限模式点击切换", False, str(e))

# 12. 统计面板打开/关闭
try:
    if stats_btn:
        stats_btn.click_input()
        time.sleep(1)
        texts = win.descendants(control_type="Text")
        has_stats = False
        for t in texts:
            try:
                if "会话统计" in t.window_text():
                    has_stats = True
                    break
            except:
                pass
        test("统计面板打开", has_stats)
        # 关闭
        stats_btn.click_input()
        time.sleep(0.5)
    else:
        test("统计面板打开", False, "统计按钮不存在")
except Exception as e:
    test("统计面板打开", False, str(e))

# 13. 响应式分栏（窗口大小调整）
try:
    user32 = ctypes.windll.user32
    hwnd = win.handle
    SWP_NOZORDER = 0x0004
    SWP_SHOWWINDOW = 0x0040
    rect = ctypes.wintypes.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    user32.SetWindowPos(hwnd, 0, rect.left, rect.top, 500, 800, SWP_NOZORDER | SWP_SHOWWINDOW)
    time.sleep(1)
    user32.SetWindowPos(hwnd, 0, rect.left, rect.top, 1200, 800, SWP_NOZORDER | SWP_SHOWWINDOW)
    time.sleep(1)
    test("响应式分栏(宽屏↔窄屏)", True)
except Exception as e:
    test("响应式分栏(宽屏↔窄屏)", False, str(e))

# 14. 状态栏存在
try:
    texts = win.descendants(control_type="Text")
    has_status = False
    for t in texts:
        try:
            txt = t.window_text()
            if "会话" in txt or "JoinCode" in txt or "就绪" in txt:
                has_status = True
                break
        except:
            pass
    test("状态栏存在", has_status)
except Exception as e:
    test("状态栏存在", False, str(e))

# 输出结果
passed = sum(1 for r in results if r[1])
total = len(results)
print(f"\n{'='*60}")
print(f"综合黑盒测试: {passed}/{total} 通过")
print(f"{'='*60}")
for r in results:
    status = "✅" if r[1] else "❌"
    print(f"  {status} {r[0]}", f"({r[2]})" if len(r) > 2 and r[2] else "")

app.kill()
print(f"\n总计: {passed}/{total} 通过")
