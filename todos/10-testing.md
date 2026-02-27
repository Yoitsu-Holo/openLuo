# 10 · 测试策略

## 1. 测试项目（D38）

```text
tests/
  openLuo.Capabilities.Tests/
  openLuo.AgentContext.Tests/
  openLuo.Extensions.Tests/
  openLuo.Cli.Tests/
  openLuo.E2E.Tests/
```

## 2. openLuo.Capabilities.Tests（内核契约）

- 决策循环：FinalText 终止 / 空回复处理 / MaxDecisions / deadline
- 并行调度：前线快照、兄弟节点隔离、结果按序合并
- 非法并行：整批拒绝、结构化错误
- mutation：intent 整批校验、原子提交、冲突整批不提交
- 外部副作用：部分成功、逐项结果回填
- 幂等：稳定 key、NonIdempotent 风险提示
- 预算：默认值、按回合重置、多 Agent 隔离
- 输出管道：Sequence 有序、fingerprint 去重、失败重试/放弃/固定消息
- Workflow 运行时：黑盒执行、节点/guard
- CanonicalId ↔ ModelToolName 映射固定

## 3. openLuo.AgentContext.Tests

- ContextContributor：原子独立、串行调用、只读约束
- 失败降级：Unavailable 结构化状态、不伪造数据
- Assembler：合并/排序/预算裁剪/不可变快照
- 会话：CreateTurnSnapshot / ApplyToolResults / CommitTurn
- 标签渲染：白名单、输出剥离、扩展渲染器注册
- Skill：摘要注入、完整内容会话缓存、相关性淘汰

## 4. openLuo.Extensions.Tests（每扩展契约）

- 能力注册（canonical id、schema、元数据）
- 能力行为（正常/失败/幂等）
- ContextContributor 贡献内容
- StateMutationHandler 校验（mutable/clamp/maxDelta）
- 标签渲染器
- 依赖解析（缺失依赖 → 禁用该扩展，结构化诊断）
- 扩展清单 5 个逐个覆盖

## 5. openLuo.Cli.Tests

- 输入解析（命令/普通消息）
- 渲染（文本/输出项）
- 输出订阅（Sequence 顺序）
- 回合结果展示

## 6. openLuo.E2E.Tests（CLI 全流程，D39）

除多媒体内容外完整可执行：

```text
启动 → 扩展加载 → 角色选择 → 对话
→ 商店/背包 → 赠礼 → 状态查看 → 多角色 → 记忆召回
→ MCP（若有配置）
```

验收断言：

- 回复非空且为角色语言
- 赠礼流程：库存扣减 + 状态变更 + 关系变化可见
- 记忆：二次对话可召回前次事实
- 能力目录含扩展注册的 canonical id
- 预算生效（超限终止有明确原因）

## 7. Playground 承担部分 E2E（D49）

- `openLuo.Playground` 提供最小可运行 demo（能力注册/决策循环/扩展示例）
- 其演示流程可作为 E2E 的补充验证路径

## 8. 测试基础设施

- xUnit + NSubstitute（沿用）
- fake ICapabilityDecisionModel（决策可编程，不依赖真实 LLM）
- 内存 IConversationStore / IStateTransaction（测试隔离）
- 临时目录 data（沿用现模式）
