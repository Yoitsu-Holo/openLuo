# 架构使用指南

## 1. 读代码入口

如果你要理解当前实现，建议按下面顺序看：

1. `openLuo/CLI/Program.cs`
2. `openLuo/Modules/AppShell/Application/ServiceCollectionExtensions.cs`
3. `openLuo/Modules/Gameplay/Application/Services/GameEngine.cs`
4. `openLuo/Modules/Agent/Application/Orchestration/AgentInvocationRouter.cs`
5. `openLuo/Modules/Agent/Application/Orchestration/CompanionOrchestrator.cs`
6. `openLuo/Modules/Agent/Application/Runtime/DefaultAgentMessageHandler.cs`
7. `openLuo/Modules/Agent/Application/Agents/CharacterAgent.cs`
8. `openLuo/Modules/Agent/Application/Turns/CharacterTurnCoordinator.cs`
9. `openLuo/Modules/Executor/`
10. `openLuo/Modules/Memory/`
11. `openLuo/Modules/PluginRuntime/Infrastructure/McpPluginHost.cs`
12. `openLuo/Modules/GameBridge/Infrastructure/GameApiHandler.cs`

## 2. 常见改动应该落在哪

### 加新宿主服务

- 修改 `ServiceCollectionExtensions`
- 必要时补对应模块接口和测试

### 加新玩家命令

- 如果是 companion 内建命令：优先改 `CompanionCommandCatalog` 与 `CompanionOrchestrator`
- 如果是插件命令：改对应插件 `tools/list` / `tools/call`

### 加新 Agent 能力

- 角色对话链路优先落到 `CharacterAgent` 内部的 turn stage。
- 稳定的推理步骤应下沉到 `Executor` 模块，而不是在 Agent 内部新增大段 LLM 调用逻辑。
- 插件或外部能力通过 tool-use 上下文暴露给角色链路，不要重新引入旧 reasoning chain。

### 加新 `game/*` API

1. 在对应 handler 类的方法上加 `[GameApi("game/xxx/yyy")]` 属性，使用强类型参数
2. `GameApiDispatcher` 启动时自动扫描并注册路由，无需手动改 switch
3. 如需前端调用，在 `ISessionGameApi` 接口和 `SessionScopedGameApi` 中添加委托方法
4. 更新本节下方的 API 一览表

示例：
```csharp
// 在对应的 Handler 中加方法
[GameApi("game/new/feature", Description = "新功能")]
public async Task<JsonNode?> NewFeatureAsync(string gameId, string param1, int param2 = 0)
{
    // 业务逻辑
}
```
- 插件通过 JSON-RPC `{"method":"game/new/feature","params":{...}}` 自动路由
- 前端 C# 通过 `session.Api.NewFeatureAsync(param1, param2)` 调用（需先在 SessionScopedGameApi 中暴露）

### 加新静态内容

- 背景：`openLuo/data/archetypes`
- 物品 mod：`openLuo/data/item-packs`
- 插件：`openLuo/data/plugins`
- 技能/工具/子代理说明：`openLuo/data/skills`、`tools`、`subagents`

## 3. 当前约束

- 角色人格应通过背景和预加载 skill 注入，不应重新写死到 kernel prompt。
- 高风险能力需要确认，默认不能在普通聊天里直接执行。
- 插件通过 stdio 运行，不要假定它与宿主共享内存。
- 设计文档应优先引用模块边界，而不是引用旧重构名称。

## 4. 调试建议

- 命令主链看 `GameEngine`
- 角色回合主链看 `DefaultAgentMessageHandler -> CharacterAgent -> CharacterTurnCoordinator`
- 阶段推理看 `Executor`
- 插件协议看 `PluginProcess` 和 `McpPluginHost`
- 反向 API 看 `GameApiHandler` 和 `GameApiDispatcher`（路由表）
- 前端 Session API 看 `ISessionGameApi` 和 `SessionScopedGameApi`
- 记忆检索看 `Memory` 模块的 recall/write service
- 状态/时间线看 `WorldState`
