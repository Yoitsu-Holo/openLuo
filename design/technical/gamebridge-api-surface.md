# GameBridge API Surface

本文档用于收口 `GameBridge` 当前对插件暴露的 `game/*` 接口面。

目标不是继续扩张接口，而是明确：

- 哪些接口仍然保留
- 哪些接口只是过渡
- 哪些接口已经移除
- 后续应该向哪一层靠拢

## 1. 设计原则

`GameBridge` 的职责只有一个：

- 把稳定的宿主业务能力映射为 `game/*` 反向 API

`GameBridge` 不负责：

- 承载第二套 AI 主链
- 暴露任意底层 LLM 能力
- 长期保留历史叙事旁路

因此，`game/*` 接口应优先暴露：

- 状态查询与状态应用
- 时间与时间线
- 资产
- 角色/库存/商店等宿主业务
- 受控的命令桥接

不应继续暴露：

- 旧 narrative beat
- 旧 narrative evaluate
- 裸 `llm complete`
- 旧 `agent plan`
- 旧 `give`

## 2. 接口分级

### 2.1 保留接口

这些接口仍属于当前稳定 API 面：

#### Session / Time

- `game/session/get`
- `game/session/update`
- `game/time/get`
- `game/time/advance`

#### Character / Inventory / Shop / Gift

- `game/character/get`
- `game/character/update`
- `game/inventory/get`
- `game/inventory/add`
- `game/inventory/remove`
- `game/items/list`
- `game/shop/categories`
- `game/shop/list`
- `game/shop/buy`
- `game/gift/execute`
- `game/affection/record`

#### Commands / Host Bridge

- `game/commands/list`
- `game/commands/confirm`
- `game/commands/execute`
- `game/ui/read_input`
- `game/log`

#### Lifecycle / Diary

- `game/lifecycle/sleep`
- `game/diary/write`
- `game/diary/list`

#### State

- `game/state/register`
- `game/state/query`
- `game/state/apply`
- `game/state/get`

#### Resource Views

- `game/resource/definitions`
- `game/resource/status`
- `game/resource/values`
- `game/resource/lifecycle`

`game/resource/*` 是 `StateDef` / `StateValue` 之上的只读资源视图 API。它不替代 `game/state/*`：

- `game/state/*` 负责注册、查询、写入底层状态
- `game/resource/*` 负责给插件、状态面板和外部界面提供统一资源视图

其中 `game/resource/lifecycle` 是定义级管理接口，用于切换资源的 `active/hidden/frozen/retired` 状态。它不会删除旧值。

#### Asset

- `game/asset/register`
- `game/asset/create`
- `game/asset/get`
- `game/asset/query`
- `game/asset/blob_put`
- `game/asset/meta_put`
- `game/asset/link`
- `game/asset/unlock`

#### Timeline

- `game/timeline/create`
- `game/timeline/query`
- `game/timeline/poll_due`
- `game/timeline/ack`
- `game/timeline/cancel`

### 2.2 已删除接口

这些接口已不再由 `GameBridge` 提供，调用会进入未知接口错误，不保留兼容 stub。

当前列表：

- `game/memory/recall`
- `game/memory/store`
- `game/memory/highlights`
- `game/narrative/beat`
- `game/narrative/evaluate`
- `game/narrative/diary_generate`
- `game/llm/complete`
- `game/agent/plan`
- `game/give`

这些接口的共同问题是：

- 它们来自旧 `Interaction` 模块
- 它们绕过了新的 `Executor` / `Agent` / `Gameplay` 分层
- 它们把 `GameBridge` 变成第二套 AI 主链入口

## 3. 替代方向

### 3.1 Narrative / Dialogue

旧：

- `game/narrative/beat`
- `game/narrative/evaluate`

替代方向：

- 由 `Agent + Executor` 统一承接角色回合
- `GameBridge` 最终只暴露稳定的宿主能力，而不是一步到位的叙事黑盒

### 3.2 Memory

旧：

- `game/memory/recall`
- `game/memory/store`
- `game/memory/highlights`

替代方向：

- `Memory` 当前先服务 `Executor`
- 是否重新对插件暴露新的 memory API，需要基于新 memory DTO 重新设计
- 不复用旧 narrative memory 接口壳

### 3.3 Raw LLM

旧：

- `game/llm/complete`

替代方向：

- 默认不再对插件开放裸 LLM 入口
- 如果未来确实需要，应单独设计受控的 executor 型接口，而不是直接透传 prompt

### 3.4 Agent Planning

旧：

- `game/agent/plan`

替代方向：

- 由新的 executor/pipeline 提供结构化规划阶段
- 不再保留旧 `AiApiHandler` 的直接规划实现

### 3.5 Give

旧：

- `game/give`

替代方向：

- 送礼类能力统一走 `game/gift/execute`
- 角色回复由宿主业务链控制，不再保留旧的独立 give 接口

## 4. 当前内置插件迁移状态

内置插件已移除对旧 narrative/memory/agent/give 接口的直接调用。

迁移后的取舍：

- `/status` 走 `game/resource/status`
- `/give` 走 `game/gift/execute`
- `/plan_date` 保留 timeline 提醒能力，但不再自动建议旧 `/date`
- 旧插件内 `/chat` 叙事链已删除，聊天走宿主 Agent 主链
- 旧即时 `/date` 命令已从插件注册中移除，等待新的 executor-backed 约会流程
- 旧 `/memories` 命令已从插件注册中移除，等待新的稳定 Memory API

## 5. 下一步收口顺序

1. 设计 executor-backed 约会流程，替代旧插件内 `/date` 叙事链。
2. 设计新的 Memory API，再恢复 `/memories` 和自动日记类能力。
3. 保持 `GameBridge` 只暴露稳定宿主业务能力，避免重新出现第二套 AI 主链。

## 6. 属性驱动的 API 路由架构（2026-06 重构）

### 6.1 改造动机

`game/*` 接口暴露给插件本质上是 JSON-RPC 代理层：插件调用 `game/xxx/yyy`，宿主找到对应的 handler 方法并执行，返回 JSON 结果。改造前这个过程完全由手动 `switch` 语句完成。

**改造前**，`GameApiHandler.HandleAsync` 内含约 230 行的 `switch` 语句手动路由全部 44 条 `game/*` 路由。每条路由需要手写参数提取代码，例如：

```csharp
// 改造前：GameApiHandler 中的 switch 路由（逐条手写）
case "game/asset/create":
{
    var assetType   = p?["assetType"]?.GetValue<string>();
    var ns          = p?["namespace"]?.GetValue<string>();
    var ownerKind   = p?["ownerKind"]?.GetValue<string>() ?? "game";
    // ... 手写参数提取 ...
    return await assetHandler.CreateGameAssetAsync(gameId, assetType, ns, ...);
}
```

新增一个 API 需要修改三处：handler 方法本身、switch case、参数提取逻辑。

**改造后**，只需在 handler 方法上加一个 attribute：

```csharp
// 改造后：AssetApiHandler 中的方法
[GameApi("game/asset/create")]
public async Task<JsonNode?> CreateGameAssetAsync(
    string gameId,        // dispatcher 自动从 bridgeContext.GameId 注入
    string assetType,     // 自动从 JSON params 按名称绑定
    string @namespace,
    string ownerKind = "game",
    string ownerId = "global",
    string? label = null,
    string sourceType = "manual")
{
    // 业务逻辑...
}
```

### 6.2 GameApiAttribute

`[GameApi]` 是一个极简的标记属性，定义在 `GameBridge.Core.Attributes` 中：

```csharp
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class GameApiAttribute : Attribute
{
    public string Route { get; }         // JSON-RPC method route，如 "game/state/get"
    public string? Description { get; init; }  // 帮助文档描述
    public GameApiAttribute(string route) => Route = route;
}
```

语义：该方法对外暴露为一个 `game/*` API 端点，可通过 JSON-RPC（插件）直接 dispatch。

### 6.3 GameApiDispatcher —— 路由扫描与运行时绑定

`GameApiDispatcher` 在构造时反射扫描所有目标 handler 类型，一次性建立全量路由表：

**注册**（`AppShell.ServiceCollectionExtensions`）：

```csharp
services.AddSingleton<GameApiDispatcher>(sp => new GameApiDispatcher(
    typeof(GameStateApiHandler),
    typeof(PlayerApiHandler),
    typeof(ShopApiHandler),
    typeof(GiftApiHandler),
    typeof(HostBridgeApiHandler),
    typeof(LifecycleApiHandler),
    typeof(StateApiHandler),
    typeof(ResourceApiHandler),
    typeof(AssetApiHandler),
    typeof(TimelineApiHandler)));
```

10 个 handler 的 44 条 `[GameApi]` 路由在启动时全量扫描完成，重复路由抛 `InvalidOperationException`。

**运行时 dispatch**：

```csharp
// GameApiDispatcher.DispatchAsync 核心流程
public async Task<object?> DispatchAsync(
    string route,                     // "game/asset/create"
    JsonNode? paramsNode,             // 插件传来的 JSON params
    GameBridgeRequestContext? bridgeContext, // 包含 GameId、SessionId 等
    IServiceProvider services,
    CancellationToken ct)
{
    var entry = _routes[route];       // 查路由表
    var instance = services.GetRequiredService(targetType);  // 从 DI 获取 handler 实例
    var argValues = new object?[entry.Parameters.Length];

    for (int i = 0; i < entry.Parameters.Length; i++)
    {
        if (i == entry.GameIdParamIndex)
            argValues[i] = bridgeContext?.GameId;    // gameId 自动注入
        else if (i == entry.CancellationTokenParamIndex)
            argValues[i] = ct;                        // CancellationToken 透传
        else
            argValues[i] = BindParameter(binding, paramDict);  // 其他参数按名称绑定
    }

    var result = entry.Method.Invoke(instance, argValues);
    // 处理 Task<T> 异步方法...
    return result;
}
```

关键参数绑定规则：

| 参数类型 | 绑定来源 |
|----------|----------|
| `string gameId` | 自动从 `bridgeContext.GameId` 注入 |
| `CancellationToken` | 自动从 `DispatchAsync` 的 `ct` 参数注入 |
| `JsonNode?` | 原生透传（不做类型转换） |
| `string` / `int` / `long` / `double` / `bool` | 从 JSON params 按 camelCase 名称绑定并自动转换 |
| 有默认值的参数 | params 缺失时使用 C# 默认值 |

### 6.4 GameApiHandler —— 退化为薄层

改造后 `GameApiHandler` 从约 230 行缩减到约 70 行，唯一职责是 `dispatch -> result -> JsonNode` 转换：

```csharp
public class GameApiHandler(GameApiDispatcher dispatcher, IServiceProvider services,
    LifecycleApiHandler lifecycleHandler, HostBridgeApiHandler hostBridgeHandler)
    : IGameApiMediator
{
    public async Task<JsonNode?> HandleAsync(
        string method, JsonNode? @params, GameBridgeRequestContext? context = null)
    {
        var result = await dispatcher.DispatchAsync(
            method, @params, context, services, CancellationToken.None);

        return result switch
        {
            null => null,
            JsonNode n => n,
            string s => JsonNode.Parse(s),
            _ => JsonSerializer.SerializeToNode(result, _serOpts)
        };
    }
}
```

### 6.5 双层访问控制

同一个 handler 方法可以同时被插件（JSON-RPC）和前端 C#（直接调用）使用，也可以只对其中一方暴露。

**第一层：[GameApi] 属性 —— 插件 JSON-RPC 入口**

加 `[GameApi("route")]` = 插件可以通过 `GameApiDispatcher` 以 JSON-RPC 协议调用该方法。不加 = 插件无法访问（即使方法是 public）。

**第二层：ISessionGameApi / SessionScopedGameApi —— 前端 C# 入口**

`ISessionGameApi` 是前端可调用的方法白名单。`SessionScopedGameApi` 对每个 handler 写一个委托方法，自动从 `SessionHandle` 解析 `gameId` 后注入 handler。前端代码通过 `session.Api.*` 调用，例如：

```csharp
// 前端代码不传 gameId — 自动从 session 注入
var state = await session.Api.GetStateAsync();
var shop  = await session.Api.ListShopItemsAsync(categoryId: "food");
var diary = await session.Api.WriteDiaryAsync(day: 3, content: "今天...");
```

`IGameSession.Api` 定义：

```csharp
public interface IGameSession
{
    string SessionId { get; }
    string? GameId { get; }

    /// Session-scoped GameApi — gameId is auto-resolved.
    ISessionGameApi Api { get; }

    // ... 其他 session 方法 ...
}
```

`SessionScopedGameApi` 内部实现：

```csharp
public sealed class SessionScopedGameApi(
    SessionHandle handle,
    ISessionRegistry sessionRegistry,
    GameStateApiHandler stateHandler,
    PlayerApiHandler playerHandler,
    // ... 其他 handler ...
    AssetApiHandler assetHandler,
    LifecycleApiHandler lifecycleHandler) : ISessionGameApi
{
    private string ResolveGameId()
    {
        if (!string.IsNullOrWhiteSpace(handle.GameId))
            return handle.GameId;
        var fresh = sessionRegistry.Get(handle.SessionId)?.GameId;
        if (!string.IsNullOrWhiteSpace(fresh))
            handle.GameId = fresh;
        return fresh ?? throw new InvalidOperationException(
            "Session is not bound to a game. Call InitGameAsync first.");
    }

    public Task<JsonNode?> GetStateAsync(CancellationToken ct) =>
        stateHandler.GetGameStateAsync(ResolveGameId());

    public Task<JsonNode?> AddInventoryAsync(string itemId, int quantity = 1,
        CancellationToken ct = default) =>
        playerHandler.AddPlayerInventoryAsync(ResolveGameId(), itemId, quantity);
    // ... 21 个方法 ...
}
```

**暴露策略矩阵**：

| Handler 方法标注 | `ISessionGameApi` 有无委托 | 暴露范围 |
|------------------|---------------------------|----------|
| 有 `[GameApi]` | 有 | 插件（JSON-RPC）+ 前端 C# 均可调用 |
| 有 `[GameApi]` | 无 | 仅插件可调用（如 `game/asset/link`、`game/timeline/ack`） |
| 无 `[GameApi]` | 有 | 仅前端 C# 可调用（如 `ListItems`） |
| 无 `[GameApi]` | 无 | 不暴露（纯内部方法） |

### 6.6 Handler 签名改造

全部 44 个方法的参数从统一的 `JsonNode? p` 改为强类型参数：

**改造前**（全部手写参数提取）：
```csharp
public async Task<JsonNode?> CreateGameAssetAsync(string gameId, JsonNode? p)
{
    var assetType    = p?["assetType"]?.GetValue<string>();
    var ns           = p?["namespace"]?.GetValue<string>();
    var ownerKind    = p?["ownerKind"]?.GetValue<string>() ?? "game";
    // ...
}
```

**改造后**（强类型参数，dispatcher 自动绑定）：
```csharp
[GameApi("game/asset/create")]
public async Task<JsonNode?> CreateGameAssetAsync(
    string gameId, string assetType, string @namespace,
    string ownerKind = "game", string ownerId = "global",
    string? label = null, string sourceType = "manual")
{
    // 参数已经解好，直接使用
}
```

剩余 `JsonNode?` 类型参数仅用于真正自由格式的 JSON 数据，dispatcher 原生支持透传：

- `metadata`（asset/register、asset/link、asset/unlock）
- `payload`（asset/meta_put、state/apply）
- `action`（state/apply）
- `mutations`（state/apply）
- blob 类型的 body（blob_put）

### 6.7 改造效果汇总

| 维度 | 改造前 | 改造后 |
|------|--------|--------|
| 路由方式 | `GameApiHandler` 内 ~230 行 switch 语句 | 10 个 handler 上的 `[GameApi]` 属性，启动时反射扫描 |
| 参数提取 | 每条路由手写 `p?["key"]?.GetValue<T>()` | Dispatcher 自动按名称绑定，支持默认值 |
| 新增 API 成本 | 改三处：方法、switch case、参数提取 | 一处：在方法上加 `[GameApi]` 即可 |
| `GameApiHandler` 大小 | ~230 行 | ~70 行（dispatch -> result -> JsonNode 薄层） |
| `gameId` 注入 | 每条路由手写 | 命名约定 `string gameId` 自动从 bridgeContext 注入 |
| 前端调用 | 无标准入口 | `session.Api.*` 通过 `ISessionGameApi` 的 21 个方法 |
| 插件调用 | JSON-RPC -> switch 路由 | JSON-RPC -> dispatcher 反射路由 |

### 6.8 扩展指南

**新增一个对插件暴露的 API**：
1. 在对应 handler 中写 public 方法，添加 `[GameApi("game/xxx/yyy")]`
2. 参数按需要声明即可（`string gameId` 会自动注入，其他按名称绑定）
3. 重新启动 — dispatcher 自动扫描新路由

**新增一个对前端暴露的 API**：
1. 在 `ISessionGameApi` 中声明方法签名（不含 `gameId`）
2. 在 `SessionScopedGameApi` 中写委托，调用 handler 方法时注入 `ResolveGameId()`

**限制插件访问**：不加 `[GameApi]` 属性即可。方法仍然是 public，可以从前端直接调用，但插件无法通过 JSON-RPC 访问。

**限制前端访问**：不在 `ISessionGameApi` 中声明即可。方法上的 `[GameApi]` 属性不受影响，插件仍然可以调用。
