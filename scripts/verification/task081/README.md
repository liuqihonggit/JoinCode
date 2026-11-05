# TASK081 校验脚本

> **路径**：`scripts/verification/task081/`
> **文档**：`docs/proposed/task/TASK081-jcc-self-talk-testing-record.md`
> **总计**：28 个脚本，覆盖 32 基础测试 + 8 边缘场景 = 40/40 PASS

## 脚本分类

### D1-D4 格式修复测试

| 脚本 | 测试项 | 验证内容 |
|------|--------|----------|
| `test_d1_uuid.py` | D1 | uuid 为标准 UUID 格式（Guid.NewGuid()） |
| `test_d2_input.py` | D2 | tool_use.input 为 JSON 对象（JsonElement 序列化） |
| `test_d4_d3.py` | D3+D4 | stop_reason tool_calls→tool_use；is_error 非 nullable bool |

### Bug K/L/M 字段修复测试

| 脚本 | 测试项 | 验证内容 |
|------|--------|----------|
| `test_bugK.py` | Bug K | system/init model 优先从 WorkflowConfig.Provider.ModelId 获取 |
| `test_bugLM.py` | Bug L+M | result 含 modelUsage + permission_denials 字段 |
| `test_modelUsage.py` | modelUsage | 从 Usage+TotalCostUsd 按模型构造用量字典 |

### S 系列场景测试

| 脚本 | 测试项 | 验证内容 |
|------|--------|----------|
| `test_s3_format.py` | S3 | NDJSON 格式正确（system/assistant/user/result 事件） |
| `test_s7_long_text.py` | S7 | 长文本流式合并（12 chunk → 单 text block） |
| `test_s8_usage.py` | S8 | usage 统计传递（input/output/cache 字段） |
| `test_s9_continue.py` | S9 | --continue 会话恢复 + 历史消息 + Bug F |
| `test_s9_multiturn.py` | S9 | 多轮对话 transcript 完整性 |
| `test_s9_transcript.py` | S9 | transcript 文件格式验证 |
| `test_s9b_continue.py` | S9 | --continue 恢复后第二轮对话 |
| `test_s10_errors.py` | S10 | 401/429/500/503 错误处理 |
| `test_s11_empty.py` | S11 | 空响应体识别为错误 |
| `test_s12_partial.py` | S12 | 部分 SSE 流（无 [DONE]）正确处理 |
| `test_s15_timeout.py` | S15 | 超时处理 |
| `test_s16_edge_cases.py` | S16 | 边缘场景集合 |
| `test_s17_init.py` | S17 | system/init 事件字段完整性 |
| `test_s18_multi_turn.py` | S18 | 多轮对话事件序列 |
| `test_s19_result_fields.py` | S19 | result 事件字段完整性 |
| `test_s20_stop_reason.py` | S20 | stop_reason 映射正确性 |
| `test_s21_mixed.py` | S21 | 混合内容（text+tool_use） |
| `test_s21b_result.py` | S21b | 混合内容 result 验证 |
| `test_s22_concurrent.py` | S22 | 3 个 jcc 进程并发请求 |
| `test_s23_large.py` | S23 | 5000 字符超大上下文 |
| `test_s24_mcp.py` | S24 | MCP 协议完整验证（initialize/tools/list/tools/call） |
| `test_s25_permission.py` | S25 | 权限拒绝 + permission_denials 填充 |

## 运行方式

### 前置条件

1. MockServer 运行：`dotnet artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.dll --port 8093 --no-open-browser`
2. jcc 在 PATH 中或指定完整路径
3. 环境变量：`JCC_ENDPOINT=http://127.0.0.1:8093/v1`、`OPENAI_API_KEY=test-key`、`JCC_DISABLE_STREAMING_FALLBACK=1`

### 运行单个脚本

```bash
python3 scripts/verification/task081/test_s24_mcp.py <token>
```

### 运行全部脚本

```bash
for f in scripts/verification/task081/test_*.py; do
    echo "=== $(basename $f) ==="
    python3 "$f" 2>&1 | tail -5
    echo ""
done
```

## 注意事项

- S24 MCP 测试需要先启动 `jcc mcp_serve --transport http --port 9911`，并传入 token 参数
- S9 测试需要先运行第一轮对话生成 transcript，再用 `--continue` 恢复
- 部分脚本依赖 MockServer 注入特定响应，需按顺序运行
- 测试脚本不参与编译（纯 Python），不影响 CI
