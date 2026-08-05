# DI架构改造任务清单

## 目标
- 每个服务有ObjectId追查生命周期
- 编译期就知道循环依赖（硬错误+白名单豁免）
- 减法简化DI机制

## 方案决策
- **方向**：方案C（减法简化+编译期检测）
- **ObjectId实现**：ServiceEntity中间基类（原方案"强制继承Entity"的优化实现）
- **编译期检测**：硬错误+[AllowCycle]白名单豁免
- **豁免机制**：78个基类冲突类用[AllowSkipEntity]豁免

## 阶段任务

### 阶段1：引入ServiceEntity中间基类 [completed]
- [x] 1a. 加ObjectType.Service枚举值
- [x] 1b. 创建ServiceEntity : Entity中间基类
- [x] 1c. 编译验证 + 单元测试通过 + 提交

### 阶段2：源码生成器加编译期环检测 [completed]
- [x] 2a. 创建[AllowCycle]白名单特性
- [x] 2b. ServiceRegistrationGenerator加DAG构建+DFS三色标记环检测
- [x] 2c. 修复ToolRegistryAdapter循环注册（显式指定IMcpToolRegistry接口）
- [x] 2d. 全层编译验证通过（Foundation→Infrastructure→Core→Services→Composition→App）

### 阶段3：RegisterAttributeAnalyzer加强 [pending]
- 编译期验证[Register]类必须继承ServiceEntity/Entity
- 除非标记[AllowSkipEntity]豁免

### 阶段4：531个[Register]类改继承ServiceEntity [pending]
- 分项目渐进式改
- 优先改核心层（Foundation → Infrastructure → Core → Services → Composition → App）

### 阶段5：删除ConstructorInjection.Generator [pending]
- 移除生成器项目（移到.xxx/）
- 452个[Inject]字段手动补构造函数

### 阶段6：78个基类冲突类处理 [pending]
- 用[AllowSkipEntity]豁免

## 决策记录

<!-- 🤖 Auto Decision: 2026-08-05 -->
<!-- 决策: 采用ServiceEntity中间基类而非直接继承Entity -->
<!-- 原因: Entity有抽象方法OnDispose()+ObjectType枚举限制，665个类直接继承代价巨大；78个类有基类冲突无法直接继承 -->
<!-- 替代方案: 源码生成器生成ObjectId属性（零侵入但用户选择强制继承）-->
<!-- 验证: 待阶段1完成后验证 -->
