"""黑盒测试：Shift+Tab 切换 AI 权限模式"""
import sys, time, os
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

# 测试1：验证权限模式按钮存在
try:
    buttons = win.descendants(control_type="Button")
    perm_btn = None
    for b in buttons:
        try:
            text = b.window_text()
            if "Auto" in text or "Plan" in text or "Ask" in text:
                perm_btn = b
                break
        except:
            pass
    if perm_btn:
        results.append(("权限模式按钮存在", True, f"初始: {perm_btn.window_text()}"))
    else:
        results.append(("权限模式按钮存在", False, "未找到权限模式按钮"))
except Exception as e:
    results.append(("权限模式按钮存在", False, str(e)))

# 测试2：点击权限模式按钮切换
try:
    if perm_btn:
        initial = perm_btn.window_text()
        perm_btn.click_input()
        time.sleep(0.5)
        after = perm_btn.window_text()
        if after != initial:
            results.append(("点击切换权限模式", True, f"{initial} → {after}"))
        else:
            results.append(("点击切换权限模式", False, f"点击后文本未变: {after}"))
    else:
        results.append(("点击切换权限模式", False, "权限按钮不存在"))
except Exception as e:
    results.append(("点击切换权限模式", False, str(e)))

# 测试3：再次点击切换
try:
    if perm_btn:
        before = perm_btn.window_text()
        perm_btn.click_input()
        time.sleep(0.5)
        after = perm_btn.window_text()
        if after != before:
            results.append(("再次点击切换", True, f"{before} → {after}"))
        else:
            results.append(("再次切换", False, f"文本未变: {after}"))
    else:
        results.append(("再次切换", False, "权限按钮不存在"))
except Exception as e:
    results.append(("再次切换", False, str(e)))

# 测试4：Shift+Tab 快捷键切换
try:
    win.set_focus()
    time.sleep(0.3)
    before = perm_btn.window_text() if perm_btn else ""
    win.type_keys("+{TAB}")
    time.sleep(0.5)
    after = perm_btn.window_text() if perm_btn else ""
    if after != before:
        results.append(("Shift+Tab快捷键切换", True, f"{before} → {after}"))
    else:
        results.append(("Shift+Tab快捷键切换", False, f"文本未变: {after}"))
except Exception as e:
    results.append(("Shift+Tab快捷键切换", False, str(e)))

# 输出结果
passed = sum(1 for r in results if r[1])
total = len(results)
print(f"\nShift+Tab权限切换测试: {passed}/{total} 通过")
for r in results:
    status = "✅" if r[1] else "❌"
    print(f"  {status} {r[0]}", f"({r[2]})" if len(r) > 2 else "")

app.kill()
print(f"\n总计: {passed}/{total} 通过")
