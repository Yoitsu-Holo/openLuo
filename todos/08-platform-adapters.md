# 08 · 平台适配层与宿主

## 1. 平台适配层（D37）

平台适配层 = 外部世界与 Agent 内核之间的渲染/传输/IO 层，不含领域逻辑。

| 工程 | 职责 | 第一版验收 |
|---|---|---|
| `openLuo.Cli` | 终端输入解析、文本渲染、订阅输出队列即时推送 | 硬验收（D39） |
| `openLuo.Tui` | Terminal.Gui 界面 | 后续修复 |
| `openLuo.Gui` | Avalonia 桌面 | 后续修复 |
| `openLuo.Qqbot` | QQ bot（Milky 协议） | 后续修复 |

统一接入方式：

```csharp
// 每个平台适配层：
// 1. 解析输入 → TurnRequest
// 2. 调用 IAgentRuntime.RunTurnAsync / StreamTurnAsync
// 3. 订阅 IOutputQueue → 收到 OutputItem 立即推送（D50）
// 4. 回合结束后渲染最终状态
```

## 2. openLuo 组合根

```text
openLuo/
  Program.cs
  Composition/ServiceCollectionExtensions.cs
  Host/ExtensionHost.cs
  Infrastructure/SqliteConversationStore.cs
  LlmAdapter/LlmCapabilityDecisionModel.cs
```

组合根职责：

- 加载配置（config/app.jsonc、config/llm.jsonc、config/mcp-servers.jsonc）
- 注册基础设施：IClock(SystemClock)、IConversationStore(SQLite)、ILlmClient、IOutputQueue(内存)
- 注册内核：ICapabilityCatalog、ICapabilityDecisionLoop、IContextAssembler、ISkillService、IStateTransaction
- 注册桥接：LlmCapabilityDecisionModel（ICapabilityDecisionModel 的 LLM 实现）
- 注册 MCP：McpCapabilitySource（读 mcp-servers.jsonc，连接）
- 装配 Extension Host：扫描 extensions/ → 加载 5 个扩展 → 注册到内核
- 平台入口分发（--cli / --tui / --gui / --qqbot）

## 3. CLI 端到端验收流程（D39）

```text
启动 → 加载扩展 → 角色选择（companion 人格目录）
→ 对话（聊天回复）
→ /inventory /shop（world 能力）
→ 赠礼流程（world + companion + memory）
→ 状态查看（world:state.read）
→ 多角色（party:list_characters / ask）
→ 随机图片（media:fetch_random_image，媒体输出走 OutputItem）
→ MCP 工具（若配置了 server）
→ 记忆：多次对话后 memory:search 能召回
```

## 4. 输出订阅契约（平台侧）

```csharp
// 平台适配层订阅输出
var queue = services.GetRequiredService<IOutputQueue>();
await foreach (var item in queue.ReadAsync(ct))
{
    // 按 Sequence 顺序发送（后一条等前一条完成）
    await channel.SendAsync(item);
}
```

- 同一会话/频道内按序；不同会话并行互不阻塞
- 失败：重试 → 放弃 + 固定失败消息 → 继续（D5）

## 5. LLM 适配层（openLuo.Capabilities.Llm）

```csharp
public sealed class LlmCapabilityDecisionModel : ICapabilityDecisionModel
{
    // 输入：AgentDecisionContext + CapabilityCatalogSnapshot
    // 转换：Contributions → system/enhance 消息
    //       Conversation → 对话消息
    //       Catalog → 原生 tool declarations（ModelToolName）
    // 调用：ILlmClient.CompleteAsync
    // 输出：CapabilityDecision（FinalText / Calls / InternalText）
}
```

- 时间标记 `[TIME: ...]`、消息标签 `[TYPE: ...]` 在序列化点渲染（沿用现设计）
- 输出侧剥离标记（防模型复述）
- 双语言匹配规则保留（情绪/记忆关键词中英混合）

## 6. 宿主 SQLite 对话存储（D35）

```csharp
public sealed class SqliteConversationStore : IConversationStore
{
    // 表：conversation_turns
    //   session_id, turn_id, speaker_id, speaker_role,
    //   content, timestamp_utc, blocks_json, metadata_json
}
```

- 数据库文件路径：config/app.jsonc（沿用现有 DatabasePath 模式）
- 旧表结构不兼容 → 删库重跑（用户已确认，测试项目无迁移）
