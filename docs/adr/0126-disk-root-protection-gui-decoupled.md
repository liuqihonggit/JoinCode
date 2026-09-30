# ADR 0126: 磁盘根保护 GUI 暴露与引擎解耦

- **状态**: accepted
- **日期**: 2026-09-30
- **决策者**: 用户 + AI

## 上下文

引擎层已有 4 层磁盘保护（PathSafetyValidator / SearchScopeValidator / DangerousPaths 字典 / DangerousCommandProtectionMiddleware），`rm -rf /` 等整盘操作即使 Bypass 也拒绝。但这些保护**未暴露到 GUI**，用户不可见、不可控。

用户需求：
1. GUI 上扫描磁盘盘号，默认勾选（勾选=保护），用户可取消勾选
2. 防止 AI 扫盘（遍历磁盘根目录）
3. 防止 AI 删除整个磁盘
4. 更多守卫，用户可开关，默认开启
5. **用开关形式，不要耦合两边**

## 决策

### 传递机制：IJccChatSession 新方法 + 引擎新守卫

```
[GUI] ProtectedDrives checkbox 变更
  ↓ MainViewModel.ApplyProtectedDrives()
  ↓ _session.UpdateProtectedDrives(protectedDrives)
  ↓
[Hosting] IJccChatSession.UpdateProtectedDrives
  ↓ JccChatSession.UpdateProtectedDrives
  ↓   委托到引擎 IToolHealthMonitor 或新 IPathProtectionService
  ↓
[Engine] DriveProtectionGuard (ICommandGuard, Priority=900)
  ↓   读取保护盘号集合
  ↓   命令涉及保护盘的根目录操作 → Deny
```

### 职责分离（不耦合两边）

| 层 | 职责 | 不做 |
|----|------|------|
| GUI | 扫描盘号 + checkbox + 收集保护盘号 + 传递 | 不做路径判断、不做命令拦截 |
| IJccChatSession | 接口隔离 | 不做业务逻辑 |
| 引擎 | DriveProtectionGuard 读取保护盘号 + 拦截 | 不做 UI 显示 |

### 盘号扫描

用 `DriveInfo.GetDrives().Where(d => d.IsReady)` 获取可用盘号（对齐 `DoctorCommand.cs:62` 现有模式），非 `Environment.GetLogicalDrives()`（无 IsReady 过滤）。

### 默认值

所有盘号默认勾选（保护开启）。用户可取消勾选个别盘号（放行该盘）。

## 替代方案

### 方案 B：settings.json 配置驱动

GUI 写保护盘号到 settings.json，引擎热重载读取。

- **优点**：更解耦，GUI 不调引擎 API
- **缺点**：热重载链路复杂（IConfigChangeNotifier → SearchScopeValidator.ReloadSearchScope），且现有 `UpdateToolBlacklist` 已是 GUI→引擎直接调用模式，不一致
- **否决**：与现有模式不一致，增加复杂度

### 方案 C：复用工具黑名单

把 `drive_scan:C` 当工具名加入黑名单。

- **优点**：零新接口
- **缺点**：语义不符（磁盘保护是路径级，非工具级），黑名单匹配工具名不匹配路径
- **否决**：语义错误

## 验证

- [ ] 引擎层：DriveProtectionGuard 拦截保护盘的扫盘/删盘操作
- [ ] GUI：盘号列表显示，默认勾选，可取消
- [ ] 传递：GUI 取消勾选 → 引擎放行该盘
- [ ] 编译 + 测试通过
