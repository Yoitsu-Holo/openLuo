# 时间系统

## 1. 当前时间模型

时间能力由 `Modules.WorldState` 提供，核心服务是 `TimeService`。

当前支持三种模式：

- `virtual`
- `realtime`
- `disabled`

## 2. 当前状态存储

时间模式并不是写死配置，而是通过状态系统保存：

- `system_time.mode`
- `system_time.timezone`
- `system_time.realtime_advance_policy`

## 3. 当前快照与推进

### 快照

- `GetSnapshotAsync`
- `TickAsync`

### 推进

- `AdvanceAsync(minutes, source)`

其中：

- virtual 模式会真实推进游戏时间
- realtime 模式会同步现实时间快照
- disabled 模式只返回 no-op 结果

## 4. 时间线系统

当前还存在独立 `TimelineService`，用于：

- 创建事件
- 查询事件
- 轮询到期事件
- ack / cancel

## 5. 命令与时间的关系

- `CommandGate` 可根据时间和事件控制命令可执行性
- 插件可通过 `game/time/*` 与 `game/timeline/*` 读写时间相关信息

## 6. 当前特点

- 时间是世界状态的一部分，而不是 UI 附加字段
- 时间推进已经可以被不同 provider 替换
- 时间与命令门控、timeline 和插件 hooks 可组合
