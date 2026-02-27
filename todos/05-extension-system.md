# 05 · 扩展宿主（Extension Host）

## 1. 定位

Extension Host 负责扩展的发现、加载、依赖解析、装配与生命周期。

- 进程内 C# 扩展为主；外部进程经 MCP/A2A/JSON-RPC 接入（D22）
- 目录即信任边界；不做签名/哈希/进程内沙箱（D23）
- 启动扫描一次，不热加载（D25）

## 2. 扩展清单（第一版 5 个，D40/D46）

```text
memory/     openLuo.Extension.Memory     语义记忆（recall/search/write）
companion/  openLuo.Extension.Companion  角色人格 + 聊天回复 + Enhance 上下文
world/      openLuo.Extension.World      游戏状态/商店/物品/礼物/时段窗口
party/      openLuo.Extension.Party      多角色协作（list/switch/ask/chat_session）
media/      openLuo.Extension.Media      媒体能力（random_image）
```

依赖（D46）：

```text
world     → 无 time 依赖（直接用内核 IClock）
companion → memory
party     → companion
media     → 无
```

## 3. Manifest（D26/D27/D28）

```jsonc
{
  "id": "world",
  "version": "1.0.0",
  "displayName": "World",
  "description": "游戏状态、商店、礼物、时段",
  "assembly": "openLuo.Extension.World.dll",
  "entryType": "OpenLuo.Extensions.World.WorldExtension",
  "requires": [],
  "dataDir": "data",
  "tags": ["game", "state"]
}
```

- `id` 全局唯一；依赖它的扩展通过 `requires` 声明（minVersion）
- `entryType` 完整类型名；`dataDir` 相对扩展根目录（领域数据随扩展，D30 配套）
- 无需额外字段（已确认）

## 4. 入口接口

```csharp
public interface IAgentExtension
{
    void Configure(ExtensionBuilder builder);
}
```

- `Id`/`Version` 以 manifest 为准（单一事实源），接口不再声明，避免漂移（已确认）

## 5. ExtensionBuilder（注入通道分组）

```csharp
public sealed class ExtensionBuilder
{
    // 上下文通道
    ContextContributors AddContextContributor<TContributor>() where TContributor : IContextContributor;
    Skills AddSkill(string skillId, SkillSummary summary);

    // 能力通道
    Capabilities AddCapability(CapabilityDescriptor descriptor, ICapabilityInvoker invoker);
    Capabilities AddWorkflow(string workflowId, WorkflowDefinition definition);

    // 状态通道
    State AddStateMutationHandler(IStateMutationHandler handler);

    // 消息标签通道（EnhanceChat，D43）
    Tags AddMessageTagRenderer(IMessageTagRenderer renderer);

    // 内部依赖（D44）
    void Require<TService>();
}
```

## 6. 依赖解析（D27/D44）

```text
Extension A
  ├── requires: [ { id: "B", minVersion: "1.0.0" } ]

Extension Host
  ├── 扫描 extensions/ 目录
  ├── 跳过 *.disable 结尾目录（D24）
  ├── 读取 manifest
  ├── 构建依赖图（拓扑排序 + 循环检测）
  ├── 解析程序集 + entryType
  ├── 创建实例（A 依赖 B → 注入 B 实例）
  ├── 调用 Configure(ExtensionBuilder)
  └── 注册贡献/能力/工作流/技能/状态/标签
```

- 依赖失败（缺失/禁用/版本过低/循环）→ 只禁用该扩展，记录结构化诊断（D27）
- 其他扩展继续加载；Agent 内核继续启动（D27）
- A→B 依赖对 Agent 不可见（D44）

## 7. 禁用机制（D24）

```text
extensions/
  rpg/                    ← 加载
  world.disable/          ← 跳过（大小写不敏感后缀 .disable）
```

- 完全跳过：不读 manifest、不加载程序集、不解析依赖、不初始化
- 被禁用扩展不参与依赖图；依赖它的扩展解析失败（按 D27 只禁用依赖方）

## 8. 自动命名空间化（D28）

```text
扩展 world 注册 local id:
  inventory.read
  gift.accept

Host 注册后 canonical id:
  world:inventory.read
  world:gift.accept
```

- 不同扩展可用相同 local id，不冲突
- Agent 只见 canonical id
- `core:` 由内核保留，扩展不得使用（D28/D33）

## 9. 生命周期

```text
宿主启动
  → 配置加载（config/app.jsonc 指定扩展目录）
  → ExtensionHost.ScanAndLoadAsync()
  → 依赖图构建/校验
  → 装配（DI 注册）
  → 注册到 CapabilityCatalog / ContextAssembler / SkillService / StateTransaction
```

运行期不重扫、不热加载、不卸载（D25）。

## 10. 与宿主端口的交互

扩展通过宿主注入的端口访问基础设施：

```csharp
// 宿主提供（组合根注册）
IClock
IConversationStore
IMemoryService             （memory 扩展消费）
IStateTransaction          （world 扩展消费）
IOutputQueue
```

扩展不得绕过端口直接访问宿主内部实现（保持依赖方向可控）。

## 11. 配置

- 扩展自身的可配置项：`extensions/<id>/data/config.jsonc`（随扩展，D30 配套）
- 宿主级：`config/app.jsonc`（扩展目录路径、预算默认值、启停列表可选）
