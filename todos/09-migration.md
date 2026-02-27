# 09 · 删除清单与迁移

> 一次性迁移（D1）：删除旧逻辑与旧数据兼容，不保留双轨。

## 1. 旧目录/模块删除清单

```text
openLuo/Modules/Agent/                全部重写（旧 flow/node/executor/context 删除）
openLuo/Modules/AgentCapabilities/    删除（能力统一进 openLuo.Capabilities）
openLuo/Modules/Executor/             删除（能力/executor 迁入扩展）
openLuo/Modules/Gameplay/             删除（领域迁入 world 扩展）
openLuo/Modules/WorldState/           删除（领域迁入 world 扩展；ITime 删除，D47）
openLuo/Modules/InterAgent/           删除（迁入 party 扩展）
openLuo/Modules/PluginRuntime/        整体废弃重写（D34；MCP 走官方 SDK）
openLuo/Modules/GameBridge/           删除（随 PluginRuntime）
openLuo/Modules/SessionRuntime/       删除（迁入 openLuo.AgentContext/Capabilities 内核）
openLuo/Modules/Commanding/           删除（命令路由迁入能力目录 + CLI）
openLuo/Modules/Content/              删除（内容加载迁入扩展数据）
openLuo/Modules/Assets/               保留基础设施，作为宿主端口（asset store 供 media 扩展）
openLuo/Modules/AppShell/             删除（DI 组合根迁入 Composition/）
```

## 2. 保留的基础设施

```text
openLuo.Foundation/          保留（Block/IGameLogger/Logger/PromptSanitizer/DB 工厂）
openLuo.Llm/                 保留（ILlmClient + providers + config POCO）
openLuo.Embedding/           保留（IEmbeddingClient）
openLuo.Memory/              保留（仓储/检索/写入；经端口暴露给 memory 扩展）
openLuo/Infrastructure/Config 保留（配置加载机制，模型按需更新）
openLuo/Interfaces/          语义保留（平台适配层拆工程后内容迁移）
openLuo/native/              保留（sqlite-vec 原生库）
publish/linux-x64/config/    保留（生产配置样例）
```

## 3. 删除的数据目录（随扩展迁移）

```text
openLuo/data/
  archetypes/     → companion/data/archetypes/
  item-packs/     → world/data/item-packs/
  commands/       → 删除（命令 = 能力目录）
  capabilities/   → 删除（能力 = 扩展注册）
  schedules/      → world/data/schedules/
  state-defs/     → world/data/state-defs/
  skills/         → companion/data/skills/
  subagents/      → 删除或迁移到 party
  tools/          → 删除（工具文档 = 能力摘要）
  plugins/        → 删除（PluginRuntime 废弃）
```

## 4. 删除的测试

```text
openLuo.Tests/                  整体删除（按新测试项目拆分，D38）
旧断言、旧契约、旧 E2E 全部移除
```

## 5. 保留并更新

```text
AGENTS.md        更新为新架构描述
README.md        更新（14 模块 → 内核 + 扩展）
design/          保留技术文档，新增本设计文档导航
Makefile         build/test/publish 目标更新
```

## 6. 不迁移清单（明确丢弃）

```text
旧 CharacterStandardChatFlow / CharacterAgentAskFlow（固定流程，D1）
goal_executor / tool_use 旧链路（早已删除，无残留）
ITime / TimeService / 虚拟时间（D47）
旧 CommandGate 时段逻辑（迁入 world 扩展能力 + 策略）
旧命令注册表/能力注册表/状态定义加载器（数据化机制保留，归属迁移）
旧 QQ bot 部署二进制占用（需用户停服后重新发布）
```

## 7. 迁移顺序总览

见 `11-implementation-steps.md` 的分步实施。
