"""黑盒测试：Token 可视化数据表（统计面板）"""
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

# 测试1：验证统计按钮存在
try:
    buttons = win.descendants(control_type="Button")
    stats_btn = None
    for b in buttons:
        try:
            if "统计" in b.window_text():
                stats_btn = b
                break
        except:
            pass
    if stats_btn:
        results.append(("统计按钮存在", True))
    else:
        results.append(("统计按钮存在", False, "未找到统计按钮"))
except Exception as e:
    results.append(("统计按钮存在", False, str(e)))

# 测试2：点击统计按钮打开面板
try:
    if stats_btn:
        stats_btn.click_input()
        time.sleep(1)
        # 查找统计面板中的文本
        texts = win.descendants(control_type="Text")
        has_stats_title = False
        for t in texts:
            try:
                if "会话统计" in t.window_text():
                    has_stats_title = True
                    break
            except:
                pass
        if has_stats_title:
            results.append(("统计面板打开", True))
        else:
            results.append(("统计面板打开", False, "未找到'会话统计'标题"))
    else:
        results.append(("统计面板打开", False, "统计按钮不存在"))
except Exception as e:
    results.append(("统计面板打开", False, str(e)))

# 测试3：验证统计面板包含数据卡片
try:
    texts = win.descendants(control_type="Text")
    stat_labels = []
    for t in texts:
        try:
            txt = t.window_text()
            if txt in ("消息数", "会话数", "字符数", "估算 Token", "Token 使用量", "消息分布"):
                stat_labels.append(txt)
        except:
            pass
    if len(stat_labels) >= 4:
        results.append(("统计卡片显示", True, f"包含: {', '.join(stat_labels)}"))
    else:
        results.append(("统计卡片显示", False, f"仅找到: {stat_labels}"))
except Exception as e:
    results.append(("统计卡片显示", False, str(e)))

# 测试4：关闭统计面板
try:
    if stats_btn:
        stats_btn.click_input()
        time.sleep(0.5)
        results.append(("关闭统计面板", True))
except Exception as e:
    results.append(("关闭统计面板", False, str(e)))

# 输出结果
passed = sum(1 for r in results if r[1])
total = len(results)
print(f"\nToken可视化数据表测试: {passed}/{total} 通过")
for r in results:
    status = "✅" if r[1] else "❌"
    print(f"  {status} {r[0]}", f"({r[2]})" if len(r) > 2 else "")

app.kill()
print(f"\n总计: {passed}/{total} 通过")
