# Executor Contract Guidelines

## Goal

定义 `Executor` 的边界约束，避免 executor 之间通过结构化输出形成隐式状态耦合。

这份文档只回答三件事：

1. `Executor` 应该做什么
2. `Executor` 不应该做什么
3. `Executor` 之间允许传什么，不允许传什么

## Core Rule

`Executor` 只输出“该任务对系统公开成立的结果”，不输出“仅供下游 executor 理解的内部控制状态”。

换句话说：

- 可以输出业务结果
- 不可以把自己的内部推理状态伪装成业务结果传给下游

## Required Properties

### 1. Executor Must Be Stateless

`Executor` 是无状态任务单元：

- 不持有跨调用状态
- 不依赖上一次调用残留
- 不把自身内部进度缓存成外部可见契约

说明：
- 对话历史、角色设定、工具结果、世界状态摘要都属于输入上下文
- 这些是调用方显式提供的输入，不属于 executor 自身状态

### 2. Executor Must Do One Task

一个 executor 只做一个明确任务。

允许：
- `PlanExecutor` 负责计划决策
- `ToolUseExecutor` 负责工具调用决策
- `CharacterResponseExecutor` 负责对白生成
- `StateUpdateExecutor` 负责状态变化建议

不允许：
- 一个 executor 同时承担“决策 + 执行 + 渲染”
- 一个 executor 同时承担“对白生成 + 演出信号生成 + 状态判断”

### 3. Executor Boundaries Must Be Explicit

每个 executor 都必须有稳定的最小输入输出契约：

- 输入必须显式建模
- 输出必须显式建模
- 不允许依赖未声明的 side-channel

## What Executors May Exchange

executor 之间只允许通过上层编排显式传递“任务结果”。

允许的结果类型：

- 计划结果
- 工具调用决策
- 工具执行结果
- 角色最终回复文本
- 状态变化建议
- 召回出的记忆结果

这些结果都必须满足一个条件：

- 它们对系统其他部分是公开成立的，不依赖特定下游 executor 才有意义

## What Executors Must Not Exchange

不允许跨 executor 传递以下类型的信息：

- 仅供某个下游 executor 理解的私有控制字段
- 中间推理痕迹
- 临时解释性标签被下游当作硬控制输入
- 一个 executor 的内部状态机阶段
- 只有“约定俗成”但没有正式建模的隐式字段

典型危险信号：

- 字段存在的唯一理由是“方便下一个 executor 用”
- 字段对系统没有独立业务意义
- 字段名看似是结果，实际是内部调度信号

## Output Mode Rules

### Text Output

当任务的主要消费者是玩家或表现层时，优先使用纯文本输出。

适用：
- 角色对白
- 动作前确认台词
- 最终面向玩家的自然语言回复

原因：
- 这类任务的主产物就是可见文本
- 不应为了附带元数据而提高主链脆弱性

### Structured Output

当任务的主要消费者是程序控制流时，使用结构化输出。

适用：
- 是否调用工具
- 选择哪个工具
- flow 路由选择
- 状态 delta
- 计划决策

要求：
- 输出字段必须尽量少
- 只保留驱动下一步所需的最小结果

### Rule-Based Output

如果任务足够确定、可本地化表达，则优先规则化，不必默认交给 LLM。

适用候选：
- 浅层意图识别
- 有限状态流转判断
- 简单候选匹配

## Diagnostic Fields

`reason`、`confidence`、`trace` 这类字段只能是可选诊断信息。

规则：

- 不能成为主流程继续运行的硬前提
- 不能承担跨 executor 控制信号
- 不能替代真正的业务结果字段

如果一个 executor 的主流程必须依赖 `reason` 才能继续，说明边界已经设计错误。

## Tool Decision vs Tool Result

必须明确分离：

- 工具调用决策
- 工具实际执行结果

### Tool Decision

属于 `ToolUseExecutor`：

- 是否调用工具
- 调哪个工具
- 带什么参数

### Tool Result

属于能力执行层 / tool execution 层：

- 工具是否成功
- 返回了什么文本
- 返回了什么结构化事实
- 错误信息是什么

规则：

- `ToolUseExecutor` 不负责提前伪造工具结果
- 工具结果不能由工具决策字段冒充

## Allowed Upstream/Downstream Impact

修改一个 executor 的契约时，允许影响：

- 该 executor 本身
- 它的直接调用方
- 与旧契约直接绑定的测试

不允许出现的情况：

- 为了改一个 executor，必须修改多个其他 executor 的内部逻辑
- 为了兼容改动，新增跨 executor 共享隐藏状态
- 调用方必须理解该 executor 的内部实现细节才能继续工作

## Current Design Implications

基于当前 `openLuo` 架构，下面这些方向是符合本约束的：

- `CharacterResponseExecutor` 输出纯文本
- `ToolUseExecutor` 只做工具决策，不承载对白结果
- `StateUpdateExecutor` 只输出状态变化建议
- 工具结果通过能力执行层返回给后续 response/state update 阶段

## Review Checklist

新增或修改 executor 时，至少检查：

1. 这个 executor 是否无状态？
2. 它是否只做一个任务？
3. 它的输出是否对系统公开成立？
4. 下游是否在依赖它的内部控制状态？
5. 这个任务是否更适合文本输出？
6. 这个任务是否其实更适合规则化？

如果第 3 或第 4 条回答不通过，说明契约边界需要重做。
