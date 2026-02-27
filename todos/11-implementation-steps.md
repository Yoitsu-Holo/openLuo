# 11 · 实施步骤

> 按依赖拓扑排序；每步结束保持可构建、可测试。
> 原则：内核先行 → 契约测试并行 → 扩展 → 平台 → 宿主 → E2E → 清理。

## Phase 0：基建准备

- [ ] 0.1 创建新工程骨架（slnx 更新、csproj 引用拓扑）
- [ ] 0.2 迁移/确认 Foundation/Llm/Embedding/Memory 依赖边界（已有）
- [ ] 0.3 建立测试项目骨架（5 个测试项目）
- [ ] 0.4 清理旧 Modules 目录（物理删除，git 保留历史）

## Phase 1：内核契约（openLuo.Capabilities + openLuo.AgentContext）

- [ ] 1.1 CapabilityDescriptor/CapabilityCall/CapabilityResult + 枚举（D19）
- [ ] 1.2 IClock（SystemClock 默认）+ 预算模型 DecisionBudgets（D21/D41）
- [ ] 1.3 IOutputQueue + OutputItem + 内存实现（D3-D6）
- [ ] 1.4 StateSnapshot/MutationIntent/IStateTransaction + 内存事务实现（D20）
- [ ] 1.5 ICapabilityCatalog + CapabilityCatalogSnapshot + 双向映射（D14/D29）
- [ ] 1.6 ICapabilityDecisionModel + CapabilityDecision（D2/D17）
- [ ] 1.7 ICapabilityDispatcher 并行调度 + 前线快照 + 非法并行拒绝（D7/D8）
- [ ] 1.8 mutation merge（intent 收集/冲突/原子提交）（D9）
- [ ] 1.9 幂等键管理（D11）
- [ ] 1.10 ICapabilityDecisionLoop 主循环 + 终止条件（D41）
- [ ] 1.11 WorkflowDefinition/IWorkflowRunner（D13）
- [ ] 1.12 IAgentRuntime 门面（OpenSession/RunTurn/StreamTurn）
- [ ] 1.13 执行轨迹 trace

## Phase 2：上下文系统（openLuo.AgentContext）

- [ ] 2.1 ContextBuildRequest/ContextContribution/AgentDecisionContext（D42）
- [ ] 2.2 IContextContributor + ContextSourceState（失败降级）（D42）
- [ ] 2.3 IContextAssembler（合并/排序/预算裁剪）（D42）
- [ ] 2.4 IAgentContextSession（快照推进）（D15/D7）
- [ ] 2.5 IConversationStore 端口（D35）
- [ ] 2.6 MessageTagPipeline + IMessageTagRenderer（白名单/剥离）（D43）
- [ ] 2.7 ISkillService（摘要/完整加载/会话缓存/淘汰）（D12/D31）
- [ ] 2.8 ContextRegion 定义（EnhanceMsg 映射）（D43）

## Phase 3：内核契约测试（与 Phase 1/2 并行）

- [ ] 3.1 openLuo.Capabilities.Tests（决策循环/并行/mutation/幂等/预算/输出）
- [ ] 3.2 openLuo.AgentContext.Tests（Contributor/Assembler/降级/标签/Skill）

## Phase 4：桥接层

- [ ] 4.1 openLuo.Capabilities.Llm：LlmCapabilityDecisionModel（D36）
  - Contributions → 消息；Catalog → 原生 tool declarations；[TIME]/[TYPE] 序列化点渲染
- [ ] 4.2 openLuo.Capabilities.Mcp：McpCapabilitySource + core:list_mcp_servers/tools（D34/D48）
  - 官方 ModelContextProtocol 2.1.0；转换映射；幂等映射（D11）

## Phase 5：扩展宿主

- [ ] 5.1 IAgentExtension + ExtensionBuilder（D26/D28）
- [ ] 5.2 Manifest 加载/校验（id/version/assembly/entryType/requires/dataDir）
- [ ] 5.3 依赖图（拓扑排序/循环检测/minVersion）（D27）
- [ ] 5.4 目录扫描 + `.disable` 跳过（D24）
- [ ] 5.5 程序集加载 + entryType 实例化 + Configure（D25/D26）
- [ ] 5.6 自动命名空间化（D28）+ `core:` 保留校验
- [ ] 5.7 依赖失败 → 禁用该扩展 + 结构化诊断（D27）

## Phase 6：领域扩展（5 个）

- [ ] 6.1 memory 扩展（recall/search/write + baseline Contributor）
- [ ] 6.2 companion 扩展（人格/chat/plan/state_propose + identity Contributor + skills）
- [ ] 6.3 world 扩展（状态/商店/物品/礼物/时段 + StateMutationHandler + data/）
- [ ] 6.4 party 扩展（list/switch/ask/chat_session + roster Contributor）
- [ ] 6.5 media 扩展（fetch_random_image）
- [ ] 6.6 openLuo.Extensions.Tests（每扩展契约）

## Phase 7：平台适配与宿主

- [ ] 7.1 openLuo.Cli（输入/渲染/输出订阅）——第一版硬验收入口
- [ ] 7.2 openLuo 组合根（配置/基础设施/内核/桥接/扩展装配/入口分发）
- [ ] 7.3 SqliteConversationStore（宿主实现 IConversationStore）
- [ ] 7.4 openLuo.Cli.Tests
- [ ] 7.5 openLuo.Playground 重写（D49）

## Phase 8：E2E 与验收

- [ ] 8.1 openLuo.E2E.Tests（CLI 全流程，除多媒体）
- [ ] 8.2 手动 E2E：扩展加载/对话/商店/赠礼/状态/多角色/记忆/MCP
- [ ] 8.3 预算生效验证（超限终止原因明确）
- [ ] 8.4 生产 publish 验证（/tmp 独立目录跑新二进制）

## Phase 9：清理与文档

- [ ] 9.1 删除旧 Modules/数据目录/旧测试（若 Phase 0.4 未全删）
- [ ] 9.2 AGENTS.md / README.md 更新（14 模块 → 内核 + 扩展）
- [ ] 9.3 design/ 导航更新
- [ ] 9.4 Makefile 目标更新
- [ ] 9.5 commit（分阶段提交：每 Phase 一个 commit）

## 风险与缓解

| 风险 | 缓解 |
|---|---|
| 扩展与内核契约漂移 | 契约测试先行（Phase 3 与实现并行） |
| LLM 决策模型转换丢失语义 | Phase 4.1 单独验证 [TIME]/[TYPE]/双语言规则 |
| 并行调度引入状态竞争 | 前线快照模型 + mutation 原子提交测试 |
| 扩展加载顺序耦合 | 依赖图拓扑排序 + 禁用策略测试 |
| 大范围删除误伤 | git 历史保留；分阶段 commit |
