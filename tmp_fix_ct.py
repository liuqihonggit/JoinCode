"""
把错误文件的 cancellationToken → ct 统一参数名
"""
import os
import hashlib

FILES = [
    r"D:\project\w1\lib\plugins.infrastructure\services\PluginHotReloader.cs",
    r"D:\project\w1\lib\plugins.infrastructure\services\PluginManager.cs",
    r"D:\project\w1\lib\vault\memdir\sync\core\TeamMemorySyncService.cs",
    r"D:\project\w1\lib\scheduling\cron\CronTaskStore.cs",
    r"D:\project\w1\lib\scheduling\tasks\core\InProcessTeammateTask.cs",
    r"D:\project\w1\server\bridge\client\BridgeClient.cs",
    r"D:\project\w1\kit\brain\summary\AwaySummaryService.cs",
    r"D:\project\w1\kit\brain\context\services\chat\core\StreamingToolExecutorActor.cs",
]

def main():
    total = 0
    for path in FILES:
        if not os.path.exists(path):
            print(f"[跳过] {path} 不存在")
            continue
        with open(path, "r", encoding="utf-8-sig") as f:
            content = f.read()
        original_hash = hashlib.md5(content.encode("utf-8")).hexdigest()
        count = content.count("cancellationToken")
        content = content.replace("cancellationToken", "ct")
        new_hash = hashlib.md5(content.encode("utf-8")).hexdigest()
        if original_hash != new_hash:
            with open(path, "w", encoding="utf-8-sig") as f:
                f.write(content)
            rel = path.replace("D:\\project\\w1\\", "").replace("\\", "/")
            print(f"[已替换] {rel}: {count}处  hash {original_hash[:8]}→{new_hash[:8]}")
            total += count
        else:
            print(f"[未变化] {path}")
    print(f"\n总计替换: {total} 处")

if __name__ == "__main__":
    main()
