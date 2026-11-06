#!/usr/bin/env bash
# run_batches.sh — 外层循环调用 run_all.py --resume，每轮 ~25s，直到全部跑完。
# 用法: bash run_batches.sh [prefixes...]   例如 bash run_batches.sh cv2
set -u
MAX_ROUNDS=40
RUN_ALL="scripts/verification/task081/batch/run_all.py"
PREFIXES="$@"

for i in $(seq 1 $MAX_ROUNDS); do
  echo "=== 第 $i 轮 ==="
  output=$(python "$RUN_ALL" $PREFIXES --resume --max-batch-time 25 --timeout 25 2>&1) || true
  echo "$output"
  if echo "$output" | grep -q "无待跑文件"; then
    echo "=== 全部跑完 ==="
    break
  fi
done
